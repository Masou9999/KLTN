using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KLTN.CloudCode.Shared;

namespace KLTN.CloudCode.MatchServer
{
    /// <summary>
    /// Multi-match servers (FR-MM-10, SDS HLD 10.2): heartbeat, capacity report and hand-over of
    /// the matches the allocator placed on a running server. Independent of the Cloud Code
    /// runtime (testable against the emulator).
    /// </summary>
    public static class ServerService
    {
        /// <summary>A placed match not picked up by its server within this time is dropped.</summary>
        public const long AssignmentTtlMs = 120_000;

        private const int MaxCommitAttempts = 4;

        /// <summary>
        /// Records the heartbeat and returns the matches newly placed on the server, each with a
        /// fresh MATCH_TOKEN (only its SHA-256 is stored, as for the first match). A match the
        /// server already knows is never handed out again; its slot is kept while the server
        /// still runs it. Slots and tokens are written in one commit guarded by updateTime.
        /// </summary>
        public static async Task<AssignedMatchesResult> GetAssignedAsync(
            FirestoreAdmin firestore,
            string serverId,
            string serverToken,
            int capacity,
            IReadOnlyCollection<string> knownMatchIds,
            IReadOnlyCollection<string> activeMatchIds,
            bool draining,
            long nowUtcMs
        )
        {
            if (string.IsNullOrEmpty(serverId))
            {
                throw new MatchRejectedException("unknown_server");
            }

            var known = new HashSet<string>(knownMatchIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var active = new HashSet<string>(activeMatchIds ?? Array.Empty<string>(), StringComparer.Ordinal);

            for (int attempt = 0; ; attempt++)
            {
                (JsonObject document, string updateTime) = await firestore.GetAsync(ServerRecords.ServerPath(serverId));

                if (document == null)
                {
                    throw new MatchRejectedException("unknown_server");
                }

                if (!MatchRecords.TokenMatches(serverToken, FirestoreAdmin.GetString(FirestoreAdmin.Fields(document), "tokenHash")))
                {
                    throw new MatchRejectedException("bad_token");
                }

                ServerInfo server = ServerRecords.Parse(document, updateTime);
                var writes = new JsonArray();
                var issued = new List<AssignedMatch>();
                var slots = new List<string>();

                foreach (string matchId in server.Slots.Distinct(StringComparer.Ordinal))
                {
                    if (known.Contains(matchId))
                    {
                        if (active.Contains(matchId))
                        {
                            slots.Add(matchId);
                        }

                        continue;
                    }

                    (JsonObject match, string matchUpdateTime) = await firestore.GetAsync(MatchRecords.MatchPath(matchId));
                    JsonObject fields = FirestoreAdmin.Fields(match);

                    if (fields == null
                        || FirestoreAdmin.Has(fields, "reportedAt")
                        || FirestoreAdmin.GetString(fields, "serverId") != serverId
                        || !FirestoreAdmin.Has(fields, "createdAt")
                        || nowUtcMs - FirestoreAdmin.ParseTimestamp(fields["createdAt"]) > AssignmentTtlMs)
                    {
                        continue;
                    }

                    string token = MatchRecords.NewToken();
                    writes.Add(firestore.Patch(
                        MatchRecords.MatchPath(matchId),
                        new JsonObject
                        {
                            ["tokenHash"] = FirestoreAdmin.String(MatchRecords.HashToken(token)),
                            ["tokenIssuedAt"] = FirestoreAdmin.Timestamp(nowUtcMs),
                        },
                        matchUpdateTime
                    ));
                    issued.Add(new AssignedMatch
                    {
                        MatchId = matchId,
                        MatchmakerMatchId = FirestoreAdmin.GetString(fields, "matchmakerMatchId"),
                        MatchToken = token,
                        Players = FirestoreAdmin.GetStrings(fields, "players").ToArray(),
                    });
                    slots.Add(matchId);
                }

                // A match the server runs but the record lost still occupies a slot.
                foreach (string matchId in active)
                {
                    if (!slots.Contains(matchId))
                    {
                        slots.Add(matchId);
                    }
                }

                writes.Add(firestore.Patch(
                    ServerRecords.ServerPath(serverId),
                    new JsonObject
                    {
                        ["slots"] = FirestoreAdmin.StringArray(slots),
                        ["capacity"] = FirestoreAdmin.Integer(Math.Clamp(capacity, 1, ServerRecords.MaxCapacity)),
                        ["lastHeartbeat"] = FirestoreAdmin.Timestamp(nowUtcMs),
                        ["draining"] = FirestoreAdmin.Boolean(draining),
                    },
                    updateTime
                ));

                try
                {
                    await firestore.CommitAsync(writes);
                    return new AssignedMatchesResult { Matches = issued };
                }
                catch (FirestoreConflictException) when (attempt + 1 < MaxCommitAttempts)
                {
                    // The allocator placed a match (or a report landed) meanwhile: read again.
                }
            }
        }
    }
}
