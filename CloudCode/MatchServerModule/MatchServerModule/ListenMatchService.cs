using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KLTN.CloudCode.Shared;

namespace KLTN.CloudCode.MatchServer
{
    /// <summary>Authenticated private matches. Only the registered host can settle them.</summary>
    public static class ListenMatchService
    {
        public static async Task<string> RegisterAsync(
            FirestoreAdmin firestore, IPlayerIdentityLookup lookup, string projectId,
            string playerId, string accessToken, string matchId, string hostPlayerId,
            long nowUtcMs)
        {
            ValidateId(matchId);
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(hostPlayerId))
                throw new MatchRejectedException("not_signed_in");

            string uid = await MatchService.ResolveUidAsync(
                firestore, lookup, playerId, accessToken, projectId, nowUtcMs);
            if (uid == null) throw new MatchRejectedException("no_firebase_link");
            (JsonObject user, _) = await firestore.GetAsync("users/" + uid);
            if (user == null) throw new MatchRejectedException("no_profile");

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    string path = MatchRecords.MatchPath(matchId);
                    (JsonObject match, string version) = await firestore.GetAsync(path);
                    if (match == null)
                    {
                        if (playerId != hostPlayerId)
                            throw new MatchRejectedException("host_not_registered");
                        await firestore.CommitAsync(new JsonArray { firestore.Write(path,
                            new JsonObject
                            {
                                ["mode"] = FirestoreAdmin.String("listen"),
                                ["hostPlayerId"] = FirestoreAdmin.String(playerId),
                                ["players"] = FirestoreAdmin.StringArray(new[] { playerId }),
                                ["ranked"] = FirestoreAdmin.Boolean(true),
                                ["createdAt"] = FirestoreAdmin.Timestamp(nowUtcMs),
                            }, exists: false) });
                        return playerId;
                    }

                    JsonObject fields = FirestoreAdmin.Fields(match);
                    CheckHost(fields, hostPlayerId);
                    var players = FirestoreAdmin.GetStrings(fields, "players");
                    if (players.Contains(playerId)) return playerId;
                    if (FirestoreAdmin.Has(fields, "reportedAt") || players.Count != 1)
                        throw new MatchRejectedException("match_full_or_finished");
                    if (nowUtcMs - FirestoreAdmin.ParseTimestamp(fields["createdAt"]) > 120000)
                        throw new MatchRejectedException("registration_expired");
                    if (await PlayerDirectory.GetUidAsync(firestore, hostPlayerId) == uid)
                        throw new MatchRejectedException("same_account");
                    players.Add(playerId);
                    await firestore.CommitAsync(new JsonArray { firestore.Patch(path,
                        new JsonObject { ["players"] = FirestoreAdmin.StringArray(players) }, version) });
                    return playerId;
                }
                catch (FirestoreConflictException) when (attempt < 3) { }
            }
        }

        public static async Task<ReportResultResponse> ReportAsync(
            FirestoreAdmin firestore, string callerPlayerId, string matchId,
            string outcome, bool hostSurrendered, bool guestSurrendered,
            int roundNumber, long nowUtcMs)
        {
            ValidateId(matchId);
            (JsonObject match, _) = await firestore.GetAsync(MatchRecords.MatchPath(matchId));
            JsonObject fields = FirestoreAdmin.Fields(match);
            CheckHost(fields, callerPlayerId);
            var players = FirestoreAdmin.GetStrings(fields, "players");
            if (players.Count != 2 || players[0] != callerPlayerId)
                throw new MatchRejectedException("players_not_registered");
            if (hostSurrendered && guestSurrendered)
                throw new MatchRejectedException("bad_surrendered_player");

            return await MatchService.ReportListenAsync(firestore, new MatchService.Report
            {
                MatchId = matchId,
                HostPlayerId = players[0],
                GuestPlayerId = players[1],
                Outcome = outcome,
                SurrenderedPlayerId = hostSurrendered ? players[0] : guestSurrendered ? players[1] : null,
                RoundNumber = roundNumber,
                MatchStartedUtcMs = FirestoreAdmin.ParseTimestamp(fields["createdAt"]),
            }, callerPlayerId, nowUtcMs);
        }

        internal static void CheckHost(JsonObject fields, string playerId)
        {
            if (string.IsNullOrEmpty(playerId)
                || FirestoreAdmin.GetString(fields, "mode") != "listen"
                || FirestoreAdmin.GetString(fields, "hostPlayerId") != playerId)
                throw new MatchRejectedException("not_listen_host");
        }

        private static void ValidateId(string matchId)
        {
            if (!Guid.TryParseExact(matchId, "N", out _))
                throw new MatchRejectedException("bad_match_id");
        }
    }
}
