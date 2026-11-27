using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KLTN.CloudCode.Shared;

namespace KLTN.CloudCode.MatchServer
{
    public static class MatchRecovery
    {
        public const long GraceMs = 45000;

        // Only the backend clock participates in a host lease decision.
        internal static bool Expired(JsonObject fields, long now)
        {
            long due = FirestoreAdmin.GetLong(fields, "hostDueMs");
            return due > 0 && now >= due;
        }

        public static async Task<string> PlayerAsync(FirestoreAdmin db, string matchId,
            string player, bool surrender, long now)
        {
            Validate(matchId);
            string state = await db.RunTransactionAsync(async tx =>
            {
                var (doc, version) = await tx.GetAsync(MatchRecords.MatchPath(matchId));
                var fields = FirestoreAdmin.Fields(doc);
                var players = FirestoreAdmin.GetStrings(fields, "players");
                if (string.IsNullOrEmpty(player) || !players.Contains(player))
                    throw new MatchRejectedException("not_in_match");
                if (FirestoreAdmin.Has(fields, "reportedAt"))
                    return Result(fields, player);
                if (Expired(fields, now) && players.Count == 2) return "expired";
                if (!FirestoreAdmin.GetBool(fields, "recoveryActive")) return "preparing";
                if (FirestoreAdmin.Has(fields, "requestedLoser")) return "pending";
                if (!surrender) return "active";
                var update = new JsonObject {
                    ["requestedLoser"] = FirestoreAdmin.String(player),
                    ["surrenderAtMs"] = FirestoreAdmin.Integer(now)
                };
                bool hostLeaving = players.Count == 2 && FirestoreAdmin.GetString(fields, "mode") == "listen"
                    && FirestoreAdmin.GetString(fields, "hostPlayerId") == player;
                if (hostLeaving) update["hostDueMs"] = FirestoreAdmin.Integer(now);
                await tx.CommitAsync(new JsonArray { tx.Patch(MatchRecords.MatchPath(matchId), update, version) });
                if (hostLeaving) return "expired";
                return "pending";
            });
            if (state != "expired") return state;
            await ResolveExpiredAsync(db, matchId, now);
            return await PlayerAsync(db, matchId, player, false, now);
        }

        private static string Result(JsonObject fields, string player)
        {
            string winner = FirestoreAdmin.GetString(fields, "winnerPlayerId");
            if (string.IsNullOrEmpty(winner)) return "finished";
            return winner == player ? "won" : "lost";
        }

        public static async Task<string> PollAsync(FirestoreAdmin db, string id, string host,
            string token, int round, long now)
        {
            Validate(id);
            string result = await db.RunTransactionAsync(async tx =>
            {
                var (doc, version) = await tx.GetAsync(MatchRecords.MatchPath(id));
                var f = FirestoreAdmin.Fields(doc);
                if (host != null) ListenMatchService.CheckHost(f, host);
                else if (f == null || !MatchRecords.TokenMatches(token,
                    FirestoreAdmin.GetString(f, "tokenHash"))) throw new MatchRejectedException("bad_token");
                if (FirestoreAdmin.Has(f, "reportedAt"))
                    return FirestoreAdmin.GetString(f, "decidedLoser") ?? "";
                if (host != null)
                {
                    if (FirestoreAdmin.GetStrings(f, "players").Count < 2) return "waiting";
                    if (Expired(f, now)) return "expired";
                    await tx.CommitAsync(new JsonArray { tx.Patch(MatchRecords.MatchPath(id),
                        new JsonObject {
                            ["recoveryActive"] = FirestoreAdmin.Boolean(true),
                            ["hostDueMs"] = FirestoreAdmin.Integer(
                                Math.Max(FirestoreAdmin.GetLong(f, "hostDueMs"), now + GraceMs)),
                            ["lastRound"] = FirestoreAdmin.Integer(Math.Max(0, round))
                        }, version) });
                }
                else if (!FirestoreAdmin.GetBool(f, "recoveryActive"))
                {
                    await tx.CommitAsync(new JsonArray { tx.Patch(MatchRecords.MatchPath(id),
                        new JsonObject { ["recoveryActive"] = FirestoreAdmin.Boolean(true) }, version) });
                }
                return FirestoreAdmin.GetString(f, "requestedLoser") ?? "";
            });
            if (result != "expired") return result;
            await ResolveExpiredAsync(db, id, now);
            return await PollAsync(db, id, host, token, round, now);
        }

        public static async Task SweepAsync(FirestoreAdmin db, long now)
        {
            var due = await db.DueListenMatchesAsync(now);
            await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (id, _) => await ResolveExpiredAsync(db, id, now));
        }

        private static async Task ResolveExpiredAsync(FirestoreAdmin db, string id, long now)
        {
            var (doc, _) = await db.GetAsync(MatchRecords.MatchPath(id));
            var f = FirestoreAdmin.Fields(doc);
            var players = FirestoreAdmin.GetStrings(f, "players");
            if (!Expired(f, now) || FirestoreAdmin.Has(f, "reportedAt") || players.Count != 2) return;
            await MatchService.ReportExpiredAsync(db, new MatchService.Report {
                MatchId = id, HostPlayerId = players[0], GuestPlayerId = players[1],
                Outcome = "GuestWon", SurrenderedPlayerId = players[0],
                RoundNumber = (int)FirestoreAdmin.GetLong(f, "lastRound"),
                MatchStartedUtcMs = FirestoreAdmin.ParseTimestamp(f["createdAt"])
            }, now);
        }

        private static void Validate(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Contains("/")
                || id == "." || id == ".." || Uri.EscapeDataString(id) != id)
                throw new MatchRejectedException("bad_match_id");
        }
    }
}
