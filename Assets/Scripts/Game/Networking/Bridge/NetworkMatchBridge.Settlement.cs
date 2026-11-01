using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KLTN.Game.ApplicationLayer;
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
        #region Server Side

        /// <summary>
        /// Called after every accepted command. When the match has just finished, a dedicated
        /// server reports it to the backend (FR-SHOP-09, FR-RANK-11) and forwards each player's
        /// settlement; a listen-host match is private and settles nothing. Each match of the
        /// process is reported exactly once, under its own match ID and MATCH_TOKEN.
        /// </summary>
        private void TrySettleFinishedMatch(MatchSession session)
        {
            if (!IsServer || session == null || !session.TryBeginSettlement())
            {
                return;
            }

            RuntimeOf(session).SettlementBySeat.Clear();

            if (!MatchServerContext.IsDedicated)
            {
                PublishSettlementToAll(session, MatchSettlementState.PrivateMatch, null);
                return;
            }

            if (!TryBuildReport(session, out MatchResultReport report))
            {
                PublishSettlementToAll(session, MatchSettlementState.Failed, "Thiếu thông tin người chơi để lưu kết quả.");
                return;
            }

            PublishSettlementToAll(session, MatchSettlementState.Saving, null);
            MatchServerContext.TrackReport(ReportAndPublishAsync(session, report));
        }

        private bool TryBuildReport(MatchSession session, out MatchResultReport report)
        {
            report = null;
            string hostPlayerId = PlayerIdForSeat(session, SeatId.Host);
            string guestPlayerId = PlayerIdForSeat(session, SeatId.Guest);

            if (string.IsNullOrEmpty(hostPlayerId) || string.IsNullOrEmpty(guestPlayerId))
            {
                Debug.LogError($"[Settlement] Cannot report {session.MatchId}: a seat has no admitted player.");
                return false;
            }

            MatchState state = session.State;
            string surrenderedPlayerId = state.SurrenderedSeat.HasValue
                ? PlayerIdForSeat(session, state.SurrenderedSeat.Value)
                : null;

            report = new MatchResultReport(
                session.MatchId,
                hostPlayerId,
                guestPlayerId,
                state.Outcome,
                surrenderedPlayerId,
                state.RoundNumber,
                session.StartedUtcMs
            );
            return true;
        }

        private async Task ReportAndPublishAsync(MatchSession session, MatchResultReport report)
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

            if (this == null || !IsSpawned)
            {
                return;
            }

            if (!result.Success)
            {
                PublishSettlementToAll(session, MatchSettlementState.Failed, result.Message);
                return;
            }

            PublishSeat(session, SeatId.Host, Build(report.MatchId, result.For(report.HostPlayerId)));
            PublishSeat(session, SeatId.Guest, Build(report.MatchId, result.For(report.GuestPlayerId)));
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

        private void PublishSettlementToAll(MatchSession session, MatchSettlementState state, string message)
        {
            foreach (SeatId seat in new[] { SeatId.Host, SeatId.Guest })
            {
                PublishSeat(
                    session,
                    seat,
                    new MatchSettlementDto
                    {
                        matchId = session.MatchId,
                        state = (int)state,
                        message = message,
                    }
                );
            }
        }

        private void PublishSeat(MatchSession session, SeatId seat, MatchSettlementDto settlement)
        {
            RuntimeOf(session).SettlementBySeat[seat] = settlement;

            foreach (ulong clientId in session.ConnectionsOf(seat))
            {
                SendSettlement(clientId, settlement);
            }
        }

        /// <summary>Resends the known settlement, e.g. after a late snapshot request.</summary>
        private void ResendSettlement(MatchSession session, ulong clientId, SeatId seat)
        {
            if (runtimeByMatch.TryGetValue(session.MatchId, out MatchRuntime runtime)
                && runtime.SettlementBySeat.TryGetValue(seat, out MatchSettlementDto settlement))
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

        private static string PlayerIdForSeat(MatchSession session, SeatId seat)
        {
            foreach (ulong clientId in session.ConnectionsOf(seat))
            {
                if (MatchServerContext.TryGetAdmission(clientId, out PlayerAdmission admission))
                {
                    return admission.PlayerId;
                }
            }

            // The seat's player may be gone (forfeit after the grace period); on a dedicated
            // server the seat is bound to their admitted PlayerId.
            return MatchServerContext.IsDedicated ? session.Seats.PlayerKeyOf(seat) : null;
        }

        /// <summary>
        /// Both players stayed away: the draw changes no points and pays nothing (HLD 10.1),
        /// so nothing is reported. Marked as settled so a late command cannot report it.
        /// </summary>
        private void SkipSettlementForAbandonedDraw(MatchSession session)
        {
            if (!session.TryBeginSettlement())
            {
                return;
            }

            RuntimeOf(session).SettlementBySeat.Clear();
            Debug.Log($"[Settlement] Match {session.MatchId} ended as an abandoned draw; not reported.");
            PublishSettlementToAll(
                session,
                MatchSettlementState.PrivateMatch,
                "Cả hai người chơi mất kết nối: không tính thưởng/điểm."
            );
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
