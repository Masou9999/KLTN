using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.Domain;

namespace KLTN.Infrastructure.CloudCode
{
    /// <summary>Backend answer to AdmitPlayer: who the player is and what they may play with.</summary>
    public sealed class PlayerAdmission
    {
        public bool Admitted { get; }
        public string Reason { get; }
        public string PlayerId { get; }
        public string Uid { get; }
        public string Username { get; }

        /// <summary>Verified owned card IDs; null when the backend did not verify them (unranked).</summary>
        public IReadOnlyList<string> OwnedCardIds { get; }
        public int Tier { get; }

        public PlayerAdmission(
            bool admitted,
            string reason,
            string playerId,
            string uid = null,
            string username = null,
            IReadOnlyList<string> ownedCardIds = null,
            int tier = 0
        )
        {
            Admitted = admitted;
            Reason = reason ?? string.Empty;
            PlayerId = playerId;
            Uid = uid;
            Username = username;
            OwnedCardIds = ownedCardIds;
            Tier = tier;
        }

        public static PlayerAdmission Reject(string playerId, string reason)
        {
            return new PlayerAdmission(false, reason, playerId);
        }
    }

    /// <summary>One finished dedicated match, sent to ReportResult.</summary>
    public sealed class MatchResultReport
    {
        public string MatchId { get; }
        public string HostPlayerId { get; }
        public string GuestPlayerId { get; }
        public MatchOutcome Outcome { get; }

        /// <summary>UGS PlayerId of the seat that surrendered; null when nobody did.</summary>
        public string SurrenderedPlayerId { get; }
        public int RoundNumber { get; }
        public long MatchStartedUtcMs { get; }

        public MatchResultReport(
            string matchId,
            string hostPlayerId,
            string guestPlayerId,
            MatchOutcome outcome,
            string surrenderedPlayerId,
            int roundNumber,
            long matchStartedUtcMs
        )
        {
            MatchId = matchId;
            HostPlayerId = hostPlayerId;
            GuestPlayerId = guestPlayerId;
            Outcome = outcome;
            SurrenderedPlayerId = surrenderedPlayerId;
            RoundNumber = roundNumber;
            MatchStartedUtcMs = matchStartedUtcMs;
        }
    }

    /// <summary>Rewards and season points applied to one player by ReportResult.</summary>
    public sealed class PlayerSettlement
    {
        public string PlayerId { get; set; }

        /// <summary>"win", "loss", "draw", "surrender_early" or "surrender".</summary>
        public string Result { get; set; }
        public int Coins { get; set; }
        public int Silver { get; set; }
        public bool Ranked { get; set; }
        public int RankDelta { get; set; }
        public int PointsBefore { get; set; }
        public int PointsAfter { get; set; }
        public int TierBefore { get; set; }
        public int TierAfter { get; set; }
        public string NotScoredReason { get; set; }
    }

    public sealed class MatchSettlementResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        public bool AlreadyReported { get; private set; }
        public IReadOnlyList<PlayerSettlement> Players { get; private set; }

        public static MatchSettlementResult Ok(IReadOnlyList<PlayerSettlement> players, bool alreadyReported)
        {
            return new MatchSettlementResult
            {
                Success = true,
                Players = players ?? new List<PlayerSettlement>(),
                AlreadyReported = alreadyReported,
            };
        }

        public static MatchSettlementResult Fail(string message)
        {
            return new MatchSettlementResult
            {
                Success = false,
                Message = message,
                Players = new List<PlayerSettlement>(),
            };
        }

        public PlayerSettlement For(string playerId)
        {
            foreach (PlayerSettlement player in Players)
            {
                if (player != null && player.PlayerId == playerId)
                {
                    return player;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Port from the dedicated match server to the trusted backend (SDS HLD 10.3).
    /// Implemented by <see cref="CloudCodeMatchResultReporter"/>; listen-host and
    /// unconfigured servers use <see cref="NullMatchResultReporter"/>.
    /// </summary>
    public interface IMatchResultReporter
    {
        /// <summary>True when results go to the backend and change rewards/season points.</summary>
        bool IsRanked { get; }

        Task<PlayerAdmission> AdmitPlayerAsync(
            string matchId,
            string playerId,
            string playerAccessToken,
            CancellationToken cancellationToken
        );

        Task<MatchSettlementResult> ReportResultAsync(
            MatchResultReport report,
            CancellationToken cancellationToken
        );
    }
}
