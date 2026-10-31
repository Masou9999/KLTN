using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KLTN.CloudCode.Shared;
using KLTN.Game.Domain;
using KLTN.Game.Domain.Economy;
using KLTN.Game.Domain.Ranking;
using Newtonsoft.Json;

namespace KLTN.CloudCode.MatchServer
{
    /// <summary>Player-facing lookup: which Firebase account and tier a UGS player has.</summary>
    public static class TicketTier
    {
        public static async Task<TicketTierResult> GetAsync(
            FirestoreAdmin firestore,
            IPlayerIdentityLookup lookup,
            string playerId,
            string accessToken,
            string projectId,
            long nowUtcMs
        )
        {
            string uid = await MatchService.ResolveUidAsync(firestore, lookup, playerId, accessToken, projectId, nowUtcMs);

            if (uid == null)
            {
                throw new MatchRejectedException("no_firebase_link");
            }

            (RankTier tier, int seasonId) = await SeasonDocuments.TierAsync(firestore, uid, nowUtcMs);
            return new TicketTierResult { Tier = (int)tier, SeasonId = seasonId };
        }
    }

    /// <summary>
    /// Match server calls, independent of the Cloud Code runtime (testable against the emulator).
    /// Every call proves it comes from the allocated server with MATCH_TOKEN, checked against
    /// matches/{matchId}.tokenHash.
    /// </summary>
    public static class MatchService
    {
        private const int MaxCommitAttempts = 4;

        /// <summary>Allowed difference between the match server's clock and ours for matchStartedUtcMs.</summary>
        private const long ClockSkewMs = RankPointsPolicy.MaxHostClockSkewSeconds * 1000L;

        #region Admission

        public static async Task<AdmitResult> AdmitAsync(
            FirestoreAdmin firestore,
            IPlayerTokenVerifier verifier,
            IPlayerIdentityLookup lookup,
            string projectId,
            string matchId,
            string matchToken,
            string playerId,
            string playerAccessToken,
            long nowUtcMs
        )
        {
            (JsonObject match, _) = await firestore.GetAsync(MatchRecords.MatchPath(matchId));
            string refusal = CheckMatch(match, matchToken, new[] { playerId });

            if (refusal != null)
            {
                return AdmitResult.Deny(refusal);
            }

            if (FirestoreAdmin.Has(FirestoreAdmin.Fields(match), "reportedAt"))
            {
                return AdmitResult.Deny("match_finished");
            }

            string tokenProblem = await verifier.VerifyAsync(playerAccessToken, playerId, projectId, nowUtcMs);

            if (tokenProblem != null)
            {
                return AdmitResult.Deny("bad_player_token:" + tokenProblem);
            }

            string uid = await ResolveUidAsync(firestore, lookup, playerId, playerAccessToken, projectId, nowUtcMs);

            if (uid == null)
            {
                return AdmitResult.Deny("no_firebase_link");
            }

            (JsonObject user, _) = await firestore.GetAsync("users/" + uid);

            if (user == null)
            {
                return AdmitResult.Deny("no_profile");
            }

            List<JsonObject> cards = await firestore.ListAsync($"users/{uid}/ownedCards");
            (RankTier tier, _) = await SeasonDocuments.TierAsync(firestore, uid, nowUtcMs);

            return new AdmitResult
            {
                Admitted = true,
                Reason = "ok",
                Uid = uid,
                Username = FirestoreAdmin.GetString(FirestoreAdmin.Fields(user), "username") ?? string.Empty,
                OwnedCardIds = cards.Select(FirestoreAdmin.DocumentId).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                Tier = (int)tier,
            };
        }

        /// <summary>
        /// ugsPlayers/{playerId} when known, otherwise the oidc-firebase link read with the player's
        /// own token (then remembered for the allocator and ReportResult).
        /// </summary>
        public static async Task<string> ResolveUidAsync(
            FirestoreAdmin firestore,
            IPlayerIdentityLookup lookup,
            string playerId,
            string accessToken,
            string projectId,
            long nowUtcMs
        )
        {
            string uid = await PlayerDirectory.GetUidAsync(firestore, playerId);

            if (uid != null)
            {
                return uid;
            }

            uid = await lookup.GetFirebaseUidAsync(playerId, accessToken, projectId);

            if (uid != null)
            {
                await PlayerDirectory.RecordAsync(firestore, playerId, uid, nowUtcMs);
            }

            return uid;
        }

        #endregion

        #region Settlement

        public sealed class Report
        {
            public string MatchId { get; set; }
            public string MatchToken { get; set; }
            public string HostPlayerId { get; set; }
            public string GuestPlayerId { get; set; }
            public string Outcome { get; set; }
            public string SurrenderedPlayerId { get; set; }
            public int RoundNumber { get; set; }
            public long MatchStartedUtcMs { get; set; }
        }

        /// <summary>
        /// Rewards (Appendix C) and season points for both players in one Firestore commit, once per
        /// match: a repeated report returns the stored settlement with alreadyReported = true.
        /// </summary>
        public static async Task<ReportResultResponse> ReportAsync(FirestoreAdmin firestore, Report report, long nowUtcMs)
        {
            MatchOutcome outcome = ParseOutcome(report.Outcome);

            if (report.RoundNumber < 0)
            {
                throw new MatchRejectedException("bad_round_number");
            }

            if (string.IsNullOrEmpty(report.HostPlayerId) || report.HostPlayerId == report.GuestPlayerId)
            {
                throw new MatchRejectedException("bad_players");
            }

            SeatId? surrenderedSeat = SurrenderedSeat(report);

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await TryReportAsync(firestore, report, outcome, surrenderedSeat, nowUtcMs);
                }
                catch (FirestoreConflictException) when (attempt < MaxCommitAttempts)
                {
                    // Lost a race (duplicate report, purchase, roster fill): re-read everything.
                }
            }
        }

        private static async Task<ReportResultResponse> TryReportAsync(
            FirestoreAdmin firestore,
            Report report,
            MatchOutcome outcome,
            SeatId? surrenderedSeat,
            long nowUtcMs
        )
        {
            string matchPath = MatchRecords.MatchPath(report.MatchId);
            (JsonObject match, string matchVersion) = await firestore.GetAsync(matchPath);
            string refusal = CheckMatch(match, report.MatchToken, new[] { report.HostPlayerId, report.GuestPlayerId });

            if (refusal != null)
            {
                throw new MatchRejectedException(refusal);
            }

            JsonObject matchFields = FirestoreAdmin.Fields(match);

            if (FirestoreAdmin.Has(matchFields, "reportedAt"))
            {
                ReportResultResponse stored = JsonConvert.DeserializeObject<ReportResultResponse>(
                    FirestoreAdmin.GetString(matchFields, "settlement") ?? "{}"
                ) ?? new ReportResultResponse();
                stored.AlreadyReported = true;
                return stored;
            }

            long createdAt = FirestoreAdmin.ParseTimestamp(matchFields["createdAt"]);
            long startedAt = SaneStart(report.MatchStartedUtcMs, createdAt, nowUtcMs);
            bool ranked = FirestoreAdmin.GetBool(matchFields, "ranked", true);
            SeasonWindow? season = await SeasonDocuments.RunningAsync(firestore, nowUtcMs);
            RankedEligibility eligibility = RankedEligibilityPolicy.Check(season, startedAt, nowUtcMs);

            var writes = new JsonArray();
            var response = new ReportResultResponse();
            var uids = new HashSet<string>(StringComparer.Ordinal);

            foreach (SeatId seat in new[] { SeatId.Host, SeatId.Guest })
            {
                string playerId = seat == SeatId.Host ? report.HostPlayerId : report.GuestPlayerId;
                string uid = await PlayerDirectory.GetUidAsync(firestore, playerId);

                if (uid == null)
                {
                    throw new MatchRejectedException("unknown_player:" + playerId);
                }

                if (!uids.Add(uid))
                {
                    throw new MatchRejectedException("same_account");
                }

                (JsonObject user, _) = await firestore.GetAsync("users/" + uid);

                if (user == null)
                {
                    throw new MatchRejectedException("no_profile:" + playerId);
                }

                var player = new PlayerSettlement { PlayerId = playerId };
                await AddRewardAsync(firestore, writes, player, uid, report, outcome, seat, surrenderedSeat, nowUtcMs);
                await AddStandingAsync(
                    firestore,
                    writes,
                    player,
                    uid,
                    FirestoreAdmin.GetString(FirestoreAdmin.Fields(user), "username"),
                    report.MatchId,
                    outcome,
                    seat,
                    surrenderedSeat,
                    ranked,
                    season,
                    eligibility,
                    startedAt,
                    nowUtcMs
                );
                response.Players.Add(player);
            }

            // updateTime precondition: of two concurrent reports only one commits.
            writes.Add(firestore.Patch(
                matchPath,
                new JsonObject
                {
                    ["reportedAt"] = FirestoreAdmin.Timestamp(nowUtcMs),
                    ["outcome"] = FirestoreAdmin.String(report.Outcome),
                    ["roundNumber"] = FirestoreAdmin.Integer(report.RoundNumber),
                    ["matchStartedAt"] = FirestoreAdmin.Timestamp(startedAt),
                    ["settlement"] = FirestoreAdmin.String(JsonConvert.SerializeObject(response)),
                },
                matchVersion
            ));

            await firestore.CommitAsync(writes);
            return response;
        }

        private static async Task AddRewardAsync(
            FirestoreAdmin firestore,
            JsonArray writes,
            PlayerSettlement player,
            string uid,
            Report report,
            MatchOutcome outcome,
            SeatId seat,
            SeatId? surrenderedSeat,
            long nowUtcMs
        )
        {
            MatchReward reward = MatchRewardPolicy.Compute(outcome, seat, surrenderedSeat, report.RoundNumber);
            player.Result = SettlementResult(reward.Kind, seat == surrenderedSeat);
            player.Coins = reward.Coins;
            player.Silver = reward.Silver;

            string rewardPath = $"users/{uid}/matchRewards/{report.MatchId}";
            (JsonObject existing, _) = await firestore.GetAsync(rewardPath);

            if (existing != null)
            {
                // Granted already (e.g. by an old client build): never pay twice.
                return;
            }

            writes.Add(firestore.Write(
                rewardPath,
                new JsonObject
                {
                    ["result"] = FirestoreAdmin.String(RewardResult(reward.Kind)),
                    ["coins"] = FirestoreAdmin.Integer(reward.Coins),
                    ["silver"] = FirestoreAdmin.Integer(reward.Silver),
                    ["createdAt"] = FirestoreAdmin.Timestamp(nowUtcMs),
                },
                exists: false
            ));

            JsonObject wallet = firestore.Patch(
                "users/" + uid,
                new JsonObject
                {
                    ["lastRewardMatchId"] = FirestoreAdmin.String(report.MatchId),
                    ["updatedAt"] = FirestoreAdmin.Timestamp(nowUtcMs),
                }
            );
            writes.Add(FirestoreAdmin.WithIncrements(
                wallet,
                new Dictionary<string, long> { ["coins"] = reward.Coins, ["silver"] = reward.Silver }
            ));
        }

        private static async Task AddStandingAsync(
            FirestoreAdmin firestore,
            JsonArray writes,
            PlayerSettlement player,
            string uid,
            string username,
            string matchId,
            MatchOutcome outcome,
            SeatId seat,
            SeatId? surrenderedSeat,
            bool ranked,
            SeasonWindow? season,
            RankedEligibility eligibility,
            long startedAt,
            long nowUtcMs
        )
        {
            JsonObject entry = null;
            string entryVersion = null;

            if (season.HasValue)
            {
                (entry, entryVersion) = await firestore.GetAsync(SeasonDocuments.EntryPath(season.Value.SeasonId, uid));
            }

            JsonObject fields = FirestoreAdmin.Fields(entry);
            int before = (int)FirestoreAdmin.GetLong(fields, "points");
            player.PointsBefore = before;
            player.PointsAfter = before;
            player.TierBefore = (int)RankPolicy.TierFor(before);
            player.TierAfter = player.TierBefore;
            player.NotScoredReason = NotScoredReason(ranked, eligibility);

            if (player.NotScoredReason != null)
            {
                return;
            }

            int seasonId = season.Value.SeasonId;
            string resultPath = SeasonDocuments.ResultPath(seasonId, uid, matchId);
            (JsonObject previous, _) = await firestore.GetAsync(resultPath);

            if (previous != null)
            {
                player.NotScoredReason = "already_scored";
                return;
            }

            // FR-RANK-06: at least 60 s between two scored results of the same player.
            string lastMatchId = FirestoreAdmin.GetString(fields, "lastMatchId");

            if (!string.IsNullOrEmpty(lastMatchId)
                && FirestoreAdmin.Has(fields, "updatedAt")
                && nowUtcMs - FirestoreAdmin.ParseTimestamp(fields["updatedAt"]) < RankPointsPolicy.MinSecondsBetweenResults * 1000L)
            {
                player.NotScoredReason = "cooldown";
                return;
            }

            RankedResult result = RankPointsPolicy.Compute(outcome, seat, surrenderedSeat);
            int after = RankPointsPolicy.ApplyDelta(before, result.Delta);
            int wins = (int)FirestoreAdmin.GetLong(fields, "wins") + (result.Kind == RankedResultKind.Win ? 1 : 0);
            int draws = (int)FirestoreAdmin.GetLong(fields, "draws") + (result.Kind == RankedResultKind.Draw ? 1 : 0);
            int losses = (int)FirestoreAdmin.GetLong(fields, "losses")
                + (result.Kind == RankedResultKind.Loss || result.Kind == RankedResultKind.Surrender ? 1 : 0);
            int matches = (int)FirestoreAdmin.GetLong(fields, "matches") + 1;

            var row = new JsonObject
            {
                ["username"] = FirestoreAdmin.String(FirestoreAdmin.GetString(fields, "username") ?? username ?? string.Empty),
                ["points"] = FirestoreAdmin.Integer(after),
                ["wins"] = FirestoreAdmin.Integer(wins),
                ["losses"] = FirestoreAdmin.Integer(losses),
                ["draws"] = FirestoreAdmin.Integer(draws),
                ["matches"] = FirestoreAdmin.Integer(matches),
                ["winRate"] = FirestoreAdmin.Integer(SeasonStanding.WinRateOf(wins, matches)),
                ["lastMatchId"] = FirestoreAdmin.String(matchId),
                ["updatedAt"] = FirestoreAdmin.Timestamp(nowUtcMs),
            };

            string entryPath = SeasonDocuments.EntryPath(seasonId, uid);
            writes.Add(entry == null
                ? firestore.Write(entryPath, row, exists: false)
                : firestore.Write(entryPath, row, updateTime: entryVersion));
            writes.Add(firestore.Write(
                resultPath,
                new JsonObject
                {
                    ["result"] = FirestoreAdmin.String(RankPointsPolicy.WireName(result.Kind)),
                    ["delta"] = FirestoreAdmin.Integer(result.Delta),
                    ["matchStartedAt"] = FirestoreAdmin.Timestamp(startedAt),
                    ["createdAt"] = FirestoreAdmin.Timestamp(nowUtcMs),
                },
                exists: false
            ));

            player.Ranked = true;
            player.PointsAfter = after;
            player.RankDelta = after - before;
            player.TierAfter = (int)RankPolicy.TierFor(after);
        }

        #endregion

        #region Helpers

        /// <summary>Null when the match exists, the token matches and every player belongs to it.</summary>
        private static string CheckMatch(JsonObject match, string matchToken, IEnumerable<string> playerIds)
        {
            if (match == null)
            {
                return "unknown_match";
            }

            JsonObject fields = FirestoreAdmin.Fields(match);

            if (!MatchRecords.TokenMatches(matchToken, FirestoreAdmin.GetString(fields, "tokenHash")))
            {
                return "bad_token";
            }

            List<string> players = FirestoreAdmin.GetStrings(fields, "players");

            foreach (string playerId in playerIds)
            {
                if (string.IsNullOrEmpty(playerId) || !players.Contains(playerId))
                {
                    return "not_in_match";
                }
            }

            return null;
        }

        private static MatchOutcome ParseOutcome(string outcome)
        {
            switch (outcome)
            {
                case "HostWon":
                    return MatchOutcome.HostWon;
                case "GuestWon":
                    return MatchOutcome.GuestWon;
                case "Draw":
                    return MatchOutcome.Draw;
                default:
                    throw new MatchRejectedException("bad_outcome");
            }
        }

        private static SeatId? SurrenderedSeat(Report report)
        {
            if (string.IsNullOrEmpty(report.SurrenderedPlayerId))
            {
                return null;
            }

            if (report.SurrenderedPlayerId == report.HostPlayerId)
            {
                return SeatId.Host;
            }

            if (report.SurrenderedPlayerId == report.GuestPlayerId)
            {
                return SeatId.Guest;
            }

            throw new MatchRejectedException("bad_surrendered_player");
        }

        /// <summary>
        /// The match cannot have started before the allocation or after now. An impossible value
        /// falls back to the allocation time (the earliest the match could have started).
        /// </summary>
        private static long SaneStart(long reported, long createdAt, long nowUtcMs)
        {
            return reported < createdAt - ClockSkewMs || reported > nowUtcMs + ClockSkewMs ? createdAt : reported;
        }

        private static string NotScoredReason(bool ranked, RankedEligibility eligibility)
        {
            if (!ranked)
            {
                return "unranked";
            }

            switch (eligibility)
            {
                case RankedEligibility.Eligible:
                    return null;
                case RankedEligibility.NoActiveSeason:
                    return "no_active_season";
                case RankedEligibility.StartedBeforeSeason:
                    return "started_before_season";
                case RankedEligibility.StartedAfterLock:
                    return "started_after_lock";
                default:
                    return "finished_after_season_end";
            }
        }

        private static string SettlementResult(MatchResultKind kind, bool surrendered)
        {
            switch (kind)
            {
                case MatchResultKind.Win:
                    return "win";
                case MatchResultKind.Draw:
                    return "draw";
                case MatchResultKind.EarlySurrender:
                    return "surrender_early";
                default:
                    return surrendered ? "surrender" : "loss";
            }
        }

        /// <summary>users/{uid}/matchRewards/{matchId}.result, same vocabulary the client wrote before.</summary>
        private static string RewardResult(MatchResultKind kind)
        {
            switch (kind)
            {
                case MatchResultKind.Win:
                    return "win";
                case MatchResultKind.Draw:
                    return "draw";
                case MatchResultKind.EarlySurrender:
                    return "surrender_early";
                default:
                    return "loss";
            }
        }

        #endregion
    }
}
