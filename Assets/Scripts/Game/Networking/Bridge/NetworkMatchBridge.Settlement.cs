using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.Domain;
using KLTN.Infrastructure.CloudCode;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    /// <summary>Client side: latest settlement received from the server for the result screen.</summary>
    public static class MatchSettlementRegistry
    {
        public static event Action<MatchSettlementDto> SettlementChanged;

        public static MatchSettlementDto Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            SettlementChanged = null;
        }

        public static void Publish(MatchSettlementDto settlement)
        {
            Current = settlement;
            SettlementChanged?.Invoke(settlement);
        }

        public static void Reset()
        {
            Current = null;
        }
    }

    public sealed partial class NetworkMatchBridge : NetworkBehaviour
    {
        #region Settlement State

        // Server: match whose settlement already started, so it is reported exactly once.
        private string settlementStartedForMatchId;
        private readonly Dictionary<SeatId, MatchSettlementDto> settlementBySeat =
            new Dictionary<SeatId, MatchSettlementDto>();

        #endregion

        #region Server Side

        /// <summary>
        /// Called after every accepted command. When the match has just finished, a dedicated
        /// server reports it to the backend (FR-SHOP-09, FR-RANK-11) and forwards each player's
        /// settlement; a listen-host match is private and settles nothing.
        /// </summary>
        private void TrySettleFinishedMatch()
        {
            if (!IsServer || matchState == null || !matchState.IsFinished)
            {
                return;
            }

            if (string.IsNullOrEmpty(currentMatchId) || currentMatchId == settlementStartedForMatchId)
            {
                return;
            }

            settlementStartedForMatchId = currentMatchId;
            settlementBySeat.Clear();

            if (!MatchServerContext.IsDedicated)
            {
                PublishSettlementToAll(MatchSettlementState.PrivateMatch, null);
                return;
            }

            if (!TryBuildReport(out MatchResultReport report))
            {
                PublishSettlementToAll(MatchSettlementState.Failed, "Thiếu thông tin người chơi để lưu kết quả.");
                return;
            }

            PublishSettlementToAll(MatchSettlementState.Saving, null);
            Task reporting = ReportAndPublishAsync(report);
            MatchServerContext.PendingReport = reporting;
        }

        private bool TryBuildReport(out MatchResultReport report)
        {
            report = null;
            string hostPlayerId = PlayerIdForSeat(SeatId.Host);
            string guestPlayerId = PlayerIdForSeat(SeatId.Guest);

            if (string.IsNullOrEmpty(hostPlayerId) || string.IsNullOrEmpty(guestPlayerId))
            {
                Debug.LogError("[Settlement] Cannot report: a seat has no admitted player.");
                return false;
            }

            string surrenderedPlayerId = matchState.SurrenderedSeat.HasValue
                ? PlayerIdForSeat(matchState.SurrenderedSeat.Value)
                : null;

            report = new MatchResultReport(
                currentMatchId,
                hostPlayerId,
                guestPlayerId,
                matchState.Outcome,
                surrenderedPlayerId,
                matchState.RoundNumber,
                currentMatchStartedUtcMs
            );
            return true;
        }

        private async Task ReportAndPublishAsync(MatchResultReport report)
        {
            MatchSettlementResult result;

            try
            {
                result = await MatchServerContext.Reporter.ReportResultAsync(report, CancellationToken.None);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                result = MatchSettlementResult.Fail(exception.Message);
            }

            Debug.Log(
                $"[Settlement] Match {report.MatchId}: success={result.Success}, "
                    + $"alreadyReported={result.AlreadyReported}, message={result.Message}."
            );

            if (this == null || !IsSpawned || report.MatchId != settlementStartedForMatchId)
            {
                return;
            }

            if (!result.Success)
            {
                PublishSettlementToAll(MatchSettlementState.Failed, result.Message);
                return;
            }

            PublishSeat(SeatId.Host, Build(report.MatchId, result.For(report.HostPlayerId)));
            PublishSeat(SeatId.Guest, Build(report.MatchId, result.For(report.GuestPlayerId)));
        }

        private static MatchSettlementDto Build(string matchId, PlayerSettlement player)
        {
            if (player == null)
            {
                return new MatchSettlementDto
                {
                    matchId = matchId,
                    state = (int)MatchSettlementState.Failed,
                    message = "Máy chủ không trả kết quả cho người chơi này.",
                };
            }

            return new MatchSettlementDto
            {
                matchId = matchId,
                state = (int)MatchSettlementState.Saved,
                result = player.Result,
                coins = player.Coins,
                silver = player.Silver,
                ranked = player.Ranked,
                rankDelta = player.RankDelta,
                pointsBefore = player.PointsBefore,
                pointsAfter = player.PointsAfter,
                tierBefore = player.TierBefore,
                tierAfter = player.TierAfter,
                notScoredReason = player.NotScoredReason,
            };
        }

        private void PublishSettlementToAll(MatchSettlementState state, string message)
        {
            foreach (SeatId seat in new[] { SeatId.Host, SeatId.Guest })
            {
                PublishSeat(
                    seat,
                    new MatchSettlementDto
                    {
                        matchId = currentMatchId,
                        state = (int)state,
                        message = message,
                    }
                );
            }
        }

        private void PublishSeat(SeatId seat, MatchSettlementDto settlement)
        {
            settlementBySeat[seat] = settlement;

            foreach (KeyValuePair<ulong, SeatId> pair in seatByClient)
            {
                if (pair.Value == seat)
                {
                    SendSettlement(pair.Key, settlement);
                }
            }
        }

        /// <summary>Resends the known settlement, e.g. after a late snapshot request.</summary>
        private void ResendSettlement(ulong clientId, SeatId seat)
        {
            if (settlementBySeat.TryGetValue(seat, out MatchSettlementDto settlement))
            {
                SendSettlement(clientId, settlement);
            }
        }

        private void SendSettlement(ulong clientId, MatchSettlementDto settlement)
        {
            if (NetworkManager == null || !NetworkManager.ConnectedClients.ContainsKey(clientId))
            {
                return;
            }

            if (NetworkManager.IsHost && clientId == NetworkManager.LocalClientId)
            {
                MatchSettlementRegistry.Publish(settlement);
                return;
            }

            ReceiveSettlementRpc(
                JsonUtility.ToJson(settlement),
                RpcTarget.Single(clientId, RpcTargetUse.Temp)
            );
        }

        private string PlayerIdForSeat(SeatId seat)
        {
            foreach (KeyValuePair<ulong, SeatId> pair in seatByClient)
            {
                if (pair.Value == seat && MatchServerContext.TryGetAdmission(pair.Key, out PlayerAdmission admission))
                {
                    return admission.PlayerId;
                }
            }

            return null;
        }

        #endregion

        #region Client Side

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveSettlementRpc(string json, RpcParams rpcParams = default)
        {
            MatchSettlementDto settlement = JsonUtility.FromJson<MatchSettlementDto>(json);

            if (settlement == null)
            {
                Debug.LogWarning("Received an invalid match settlement.");
                return;
            }

            Debug.Log(
                $"Received settlement for {settlement.matchId}: "
                    + $"state={(MatchSettlementState)settlement.state}, result={settlement.result}."
            );
            MatchSettlementRegistry.Publish(settlement);
        }

        #endregion
    }
}
