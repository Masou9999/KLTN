#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace KLTN.CloudCode.Shared
{
    /// <summary>A running match server as stored in servers/{serverId}.</summary>
    public sealed class ServerInfo
    {
        public string ServerId { get; set; }

        /// <summary>Edgegap request id, used by Matchmaker_PollAllocation to find IP and port.</summary>
        public string DeploymentId { get; set; }
        public int Capacity { get; set; }

        /// <summary>Matches placed on the server and not finished yet.</summary>
        public List<string> Slots { get; set; } = new List<string>();
        public long LastHeartbeatMs { get; set; }
        public bool Draining { get; set; }
        public string UpdateTime { get; set; }

        public int FreeSlots => Math.Max(0, Capacity - Slots.Count);
    }

    /// <summary>
    /// servers/{serverId} (FR-MM-10, SDS HLD 10.2): written by the allocator when it starts a
    /// multi-match deployment and by MatchServerModule.GetAssignedMatches on every heartbeat.
    /// { deploymentId, capacity, slots[], tokenHash (SHA-256 of SERVER_TOKEN), lastHeartbeat,
    /// draining, createdAt }. Clients cannot read it (Firestore Rules).
    /// </summary>
    public static class ServerRecords
    {
        /// <summary>A server that has not sent a heartbeat for this long is treated as dead.</summary>
        public const long StaleAfterMs = 10_000;

        public const int MaxCapacity = 16;

        private const int ReserveAttempts = 4;

        public static string ServerPath(string serverId)
        {
            return "servers/" + serverId;
        }

        /// <summary>Record for a new deployment; its first match (MATCH_ID) already fills a slot.</summary>
        public static Task CreateAsync(
            FirestoreAdmin firestore,
            string serverId,
            string deploymentId,
            string tokenHash,
            int capacity,
            string firstMatchId,
            long nowUtcMs
        )
        {
            var fields = new JsonObject
            {
                ["deploymentId"] = FirestoreAdmin.String(deploymentId),
                ["capacity"] = FirestoreAdmin.Integer(Math.Clamp(capacity, 1, MaxCapacity)),
                ["slots"] = FirestoreAdmin.StringArray(new[] { firstMatchId }),
                ["tokenHash"] = FirestoreAdmin.String(tokenHash),
                // No heartbeat yet: the server is not reused before it reports for the first time.
                ["lastHeartbeat"] = FirestoreAdmin.Timestamp(0),
                ["draining"] = FirestoreAdmin.Boolean(false),
                ["createdAt"] = FirestoreAdmin.Timestamp(nowUtcMs),
            };

            return firestore.CommitAsync(new JsonArray { firestore.Write(ServerPath(serverId), fields, exists: false) });
        }

        public static ServerInfo Parse(JsonObject document, string updateTime = null)
        {
            JsonObject fields = FirestoreAdmin.Fields(document);

            if (fields == null)
            {
                return null;
            }

            return new ServerInfo
            {
                ServerId = FirestoreAdmin.DocumentId(document),
                DeploymentId = FirestoreAdmin.GetString(fields, "deploymentId") ?? string.Empty,
                Capacity = (int)FirestoreAdmin.GetLong(fields, "capacity", 1),
                Slots = FirestoreAdmin.GetStrings(fields, "slots"),
                LastHeartbeatMs = FirestoreAdmin.Has(fields, "lastHeartbeat") ? FirestoreAdmin.ParseTimestamp(fields["lastHeartbeat"]) : 0,
                Draining = FirestoreAdmin.GetBool(fields, "draining"),
                UpdateTime = updateTime ?? document["updateTime"]?.GetValue<string>(),
            };
        }

        /// <summary>
        /// Servers that may take one more match, best first: alive (recent heartbeat), not
        /// draining, with a free slot. The fullest server comes first so matches are packed and
        /// idle servers can shut down (NFR-21).
        /// </summary>
        public static List<ServerInfo> Candidates(IEnumerable<ServerInfo> servers, long nowUtcMs)
        {
            return servers
                .Where(server => server != null
                    && !server.Draining
                    && server.FreeSlots > 0
                    && nowUtcMs - server.LastHeartbeatMs <= StaleAfterMs)
                .OrderBy(server => server.FreeSlots)
                .ThenByDescending(server => server.LastHeartbeatMs)
                .ToList();
        }

        /// <summary>
        /// Puts the match in a free slot of a running server. The slot list is written with the
        /// document's updateTime as precondition, so two allocations can never overfill a server
        /// (the loser re-reads and tries again). Null when no server has room.
        /// </summary>
        public static async Task<ServerInfo> TryReserveAsync(FirestoreAdmin firestore, string matchId, long nowUtcMs)
        {
            for (int attempt = 0; attempt < ReserveAttempts; attempt++)
            {
                var servers = new List<ServerInfo>();

                foreach (JsonObject document in await firestore.ListAsync("servers"))
                {
                    servers.Add(Parse(document));
                }

                List<ServerInfo> candidates = Candidates(servers, nowUtcMs);

                if (candidates.Count == 0)
                {
                    return null;
                }

                bool conflicted = false;

                foreach (ServerInfo server in candidates)
                {
                    var slots = new List<string>(server.Slots) { matchId };
                    var fields = new JsonObject { ["slots"] = FirestoreAdmin.StringArray(slots) };

                    try
                    {
                        await firestore.CommitAsync(new JsonArray
                        {
                            firestore.Patch(ServerPath(server.ServerId), fields, server.UpdateTime),
                        });
                        server.Slots = slots;
                        return server;
                    }
                    catch (FirestoreConflictException)
                    {
                        // Another allocation or a heartbeat changed this server: list again.
                        conflicted = true;
                        break;
                    }
                }

                if (!conflicted)
                {
                    return null;
                }
            }

            return null;
        }
    }
}
