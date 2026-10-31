using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KLTN.Infrastructure.CloudCode
{
    /// <summary>
    /// Used when the server cannot reach the backend (listen-host, or a dedicated server
    /// without UGS credentials). Admits any identified player with unverified cards and
    /// scores nothing.
    /// </summary>
    public sealed class NullMatchResultReporter : IMatchResultReporter
    {
        public const string NotScoredReason = "Máy chủ chưa cấu hình xếp hạng: không tính thưởng/điểm.";

        public bool IsRanked => false;

        public Task<PlayerAdmission> AdmitPlayerAsync(
            string matchId,
            string playerId,
            string playerAccessToken,
            CancellationToken cancellationToken
        )
        {
            return Task.FromResult(new PlayerAdmission(true, string.Empty, playerId));
        }

        public Task<MatchSettlementResult> ReportResultAsync(
            MatchResultReport report,
            CancellationToken cancellationToken
        )
        {
            var players = new List<PlayerSettlement>
            {
                Unscored(report.HostPlayerId),
                Unscored(report.GuestPlayerId),
            };

            return Task.FromResult(MatchSettlementResult.Ok(players, alreadyReported: false));
        }

        private static PlayerSettlement Unscored(string playerId)
        {
            return new PlayerSettlement
            {
                PlayerId = playerId,
                Ranked = false,
                NotScoredReason = NotScoredReason,
            };
        }
    }
}
