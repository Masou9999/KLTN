using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.Domain;
using KLTN.Game.Domain.Economy;
using KLTN.Game.Domain.Ranking;
using UnityEngine;

namespace KLTN.Infrastructure.CloudCode
{
    /// <summary>
    /// Local stand-in for MatchServerModule so the dedicated flow (admission, settlement,
    /// result screen) can be tested without Cloud Code. Computes rewards and points with the
    /// domain policies but persists nothing. Enabled with MATCH_REPORTER_STUB=1 or the
    /// Editor toggle on DedicatedServerBootstrap.
    /// </summary>
    public sealed class StubMatchResultReporter : IMatchResultReporter
    {
        #region Configuration

        private readonly int delayMilliseconds;
        private readonly bool failReports;
        private readonly IReadOnlyList<string> ownedCardIds;

        #endregion

        public StubMatchResultReporter(
            int delayMilliseconds = 1000,
            bool failReports = false,
            IReadOnlyList<string> ownedCardIds = null
        )
        {
            this.delayMilliseconds = Math.Max(0, delayMilliseconds);
            this.failReports = failReports;
            this.ownedCardIds = ownedCardIds;
        }

        public bool IsRanked => true;

        #region IMatchResultReporter

        public async Task<PlayerAdmission> AdmitPlayerAsync(
            string matchId,
            string playerId,
            string playerAccessToken,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(delayMilliseconds, cancellationToken);
            Debug.Log($"[StubReporter] Admitted {playerId} to {matchId}.");

            // A null list keeps the client's report (stub cannot know real ownership).
            return new PlayerAdmission(true, string.Empty, playerId, playerId, null, ownedCardIds, 0);
        }

        public async Task<MatchSettlementResult> ReportResultAsync(
            MatchResultReport report,
            CancellationToken cancellationToken
        )
        {
            await Task.Delay(delayMilliseconds, cancellationToken);

            if (failReports)
            {
                return MatchSettlementResult.Fail("Stub: lỗi giả lập.");
            }

            var players = new List<PlayerSettlement>
            {
                Settle(report, SeatId.Host, report.HostPlayerId),
                Settle(report, SeatId.Guest, report.GuestPlayerId),
            };

            Debug.Log($"[StubReporter] Reported {report.MatchId}: {report.Outcome}, round {report.RoundNumber}.");
            return MatchSettlementResult.Ok(players, alreadyReported: false);
        }

        #endregion

        #region Helpers

        private static PlayerSettlement Settle(MatchResultReport report, SeatId seat, string playerId)
        {
            SeatId? surrendered = null;

            if (!string.IsNullOrEmpty(report.SurrenderedPlayerId))
            {
                surrendered = report.SurrenderedPlayerId == report.HostPlayerId ? SeatId.Host : SeatId.Guest;
            }

            MatchReward reward = MatchRewardPolicy.Compute(report.Outcome, seat, surrendered, report.RoundNumber);
            RankedResult ranked = RankPointsPolicy.Compute(report.Outcome, seat, surrendered);
            int after = RankPointsPolicy.ApplyDelta(0, ranked.Delta);

            return new PlayerSettlement
            {
                PlayerId = playerId,
                Result = reward.Kind == MatchResultKind.EarlySurrender
                    ? "surrender_early"
                    : RankPointsPolicy.WireName(ranked.Kind),
                Coins = reward.Coins,
                Silver = reward.Silver,
                Ranked = true,
                RankDelta = after,
                PointsBefore = 0,
                PointsAfter = after,
                TierBefore = (int)RankTier.Bronze,
                TierAfter = (int)RankPolicy.TierFor(after),
            };
        }

        #endregion
    }
}
