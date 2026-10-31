#nullable disable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KLTN.Game.Domain.Ranking;

namespace KLTN.CloudCode.Shared
{
    /// <summary>
    /// matches/{matchId}: written by the allocator (players, tokenHash, serverId, createdAt),
    /// completed by MatchServerModule.ReportResult (reportedAt, settlement). Clients cannot read it.
    /// The plain MATCH_TOKEN only lives in the match server container; Firestore keeps its SHA-256.
    /// </summary>
    public static class MatchRecords
    {
        public const int TokenBytes = 32;

        public static string MatchPath(string matchId)
        {
            return "matches/" + matchId;
        }

        /// <summary>GUID "N" format (32 hex chars), the MATCH_ID format DedicatedServerBootstrap expects.</summary>
        public static string NewMatchId()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>32 random bytes, base64url without padding.</summary>
        public static string NewToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(TokenBytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Lowercase hex SHA-256 of the UTF-8 token.</summary>
        public static string HashToken(string token)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>Constant-time comparison of a presented token with the stored hash.</summary>
        public static bool TokenMatches(string token, string storedHash)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            byte[] expected = Encoding.ASCII.GetBytes(storedHash.ToLowerInvariant());
            byte[] actual = Encoding.ASCII.GetBytes(HashToken(token));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        /// <summary>Allocation record, written before the deployment so the server can admit players at once.</summary>
        public static async Task CreateAsync(
            FirestoreAdmin firestore,
            string matchId,
            string tokenHash,
            IReadOnlyList<string> players,
            IReadOnlyList<RankTier> tiers,
            bool ranked,
            string matchmakerMatchId,
            long nowUtcMs
        )
        {
            var tierValues = new JsonArray();

            foreach (RankTier tier in tiers)
            {
                tierValues.Add(FirestoreAdmin.Integer((int)tier));
            }

            var fields = new JsonObject
            {
                ["players"] = FirestoreAdmin.StringArray(players),
                ["tiers"] = new JsonObject { ["arrayValue"] = new JsonObject { ["values"] = tierValues } },
                ["tokenHash"] = FirestoreAdmin.String(tokenHash),
                ["serverId"] = FirestoreAdmin.String(string.Empty),
                ["ranked"] = FirestoreAdmin.Boolean(ranked),
                ["matchmakerMatchId"] = FirestoreAdmin.String(matchmakerMatchId),
                ["createdAt"] = FirestoreAdmin.Timestamp(nowUtcMs),
            };

            await firestore.CommitAsync(new JsonArray { firestore.Write(MatchPath(matchId), fields, exists: false) });
        }

        /// <summary>Records which server (Edgegap request id) hosts the match.</summary>
        public static Task SetServerAsync(FirestoreAdmin firestore, string matchId, string serverId)
        {
            var fields = new JsonObject { ["serverId"] = FirestoreAdmin.String(serverId) };
            return firestore.CommitAsync(new JsonArray { firestore.Patch(MatchPath(matchId), fields) });
        }

        /// <summary>FR-RANK-12: every pair of players is within <see cref="RankMatchmakingPolicy.MaxTierDifference"/>.</summary>
        public static bool TiersCanMeet(IReadOnlyList<RankTier> tiers)
        {
            for (int left = 0; left < tiers.Count; left++)
            {
                for (int right = left + 1; right < tiers.Count; right++)
                {
                    if (!RankMatchmakingPolicy.CanMeet(tiers[left], tiers[right]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>
    /// ugsPlayers/{playerId} = { uid, updatedAt }: UGS PlayerId → Firebase uid, written by
    /// MatchServerModule after it verified the link with the player's own token (GetTicketTier,
    /// AdmitPlayer). The allocator and ReportResult only read it.
    /// </summary>
    public static class PlayerDirectory
    {
        public static string PlayerPath(string playerId)
        {
            return "ugsPlayers/" + playerId;
        }

        public static async Task<string> GetUidAsync(FirestoreAdmin firestore, string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return null;
            }

            (JsonObject document, _) = await firestore.GetAsync(PlayerPath(playerId));
            string uid = FirestoreAdmin.GetString(FirestoreAdmin.Fields(document), "uid");
            return string.IsNullOrEmpty(uid) ? null : uid;
        }

        public static Task RecordAsync(FirestoreAdmin firestore, string playerId, string uid, long nowUtcMs)
        {
            var fields = new JsonObject
            {
                ["uid"] = FirestoreAdmin.String(uid),
                ["updatedAt"] = FirestoreAdmin.Timestamp(nowUtcMs),
            };
            return firestore.CommitAsync(new JsonArray { firestore.Write(PlayerPath(playerId), fields) });
        }
    }
}
