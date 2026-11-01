using System;
using System.Collections;
using System.Collections.Generic;
using KLTN.Game.ApplicationLayer;
using KLTN.Game.Domain;
using KLTN.Infrastructure.CloudCode;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    public sealed partial class NetworkMatchBridge : NetworkBehaviour
    {
        #region Reconnect State

        private const float GraceCheckIntervalSeconds = 0.25f;

        // Listen-host matches have no verified identity: the host keeps its own seat and the
        // only other seat belongs to whoever rejoins the private session (join code).
        private const string ListenHostPlayerKey = "listen-host";
        private const string ListenGuestPlayerKey = "listen-guest";

        private const string SeatExpiredReason =
            "Đã hết thời gian giữ chỗ. Trận đấu đã kết thúc.";

        // Server: seats bound to player keys, held for 30 s after a drop (FR-NET-06/07).
        private readonly SeatConnectionTracker seatConnections = new SeatConnectionTracker();
        private Coroutine graceCoroutine;

        #endregion

        #region Lifecycle

        private void BeginSeatTracking()
        {
            seatConnections.Reset();
            MatchServerContext.CanRejoin = playerId => seatConnections.CanReconnect(playerId, NowMs());
            MatchServerContext.IsHoldingSeats = () =>
                seatConnections.IsHoldingSeats && (matchState == null || !matchState.IsFinished);
        }

        private void EndSeatTracking()
        {
            if (graceCoroutine != null)
            {
                StopCoroutine(graceCoroutine);
                graceCoroutine = null;
            }

            MatchServerContext.ClearSeatCallbacks();
        }

        #endregion

        #region Seat Binding

        /// <summary>
        /// Stable identity of a connection: the admitted UGS PlayerId on a dedicated server,
        /// a fixed key per seat on listen-host. Null when the client was never admitted.
        /// </summary>
        private string PlayerKeyFor(ulong clientId)
        {
            if (MatchServerContext.IsDedicated)
            {
                return MatchServerContext.TryGetAdmission(clientId, out PlayerAdmission admission)
                    ? admission.PlayerId
                    : null;
            }

            return clientId == NetworkManager.LocalClientId ? ListenHostPlayerKey : ListenGuestPlayerKey;
        }

        private void BindSeat(ulong clientId, SeatId seat)
        {
            if (seatConnections.IsBound(seat))
            {
                return;
            }

            string playerKey = PlayerKeyFor(clientId);

            if (!string.IsNullOrEmpty(playerKey))
            {
                seatConnections.Bind(seat, playerKey);
            }
        }

        /// <summary>A bound seat stays taken while its player is away.</summary>
        private bool IsSeatTaken(SeatId seat)
        {
            return HasSeat(seat) || seatConnections.IsBound(seat);
        }

        /// <summary>
        /// Gives a returning player their held seat back (FR-NET-07, FR-MM-08), then sends the
        /// full snapshot at the current revision to both players and resumes the match.
        /// Returns true when the client was handled here (reseated or refused).
        /// </summary>
        private bool TryReclaimSeat(ulong clientId)
        {
            string playerKey = PlayerKeyFor(clientId);

            if (
                string.IsNullOrEmpty(playerKey)
                || !seatConnections.TryFindSeat(playerKey, out SeatId heldSeat)
                || seatConnections.StateOf(heldSeat) == SeatConnectionState.Connected
            )
            {
                return false;
            }

            ReconnectResult result = seatConnections.TryReconnect(playerKey, NowMs(), out SeatId seat);

            if (result != ReconnectResult.Reconnected)
            {
                Debug.LogWarning($"[Reconnect] Client {clientId} refused for {heldSeat}: {result}.");
                NetworkManager.DisconnectClient(clientId, SeatExpiredReason);
                return true;
            }

            seatByClient[clientId] = seat;
            lastCommandIdByClient.Remove(clientId);
            Debug.Log($"[Reconnect] Client {clientId} took back {seat}. Paused={seatConnections.IsPaused}.");

            BroadcastUpdate();
            ResendSettlement(clientId, seat);
            return true;
        }

        #endregion

        #region Disconnect Handling

        /// <summary>
        /// Holds the seat instead of discarding the match: commands pause, the opponent is told
        /// at once through a snapshot, and the server timer decides if nobody comes back.
        /// </summary>
        private void HoldSeat(SeatId seat)
        {
            if (!seatConnections.MarkDisconnected(seat, NowMs()))
            {
                return;
            }

            Debug.Log(
                $"[Reconnect] {seat} disconnected; holding the seat for "
                    + $"{seatConnections.GracePeriodMilliseconds / 1000} s."
            );

            if (graceCoroutine == null)
            {
                graceCoroutine = StartCoroutine(RunGraceTimer());
            }
        }

        private IEnumerator RunGraceTimer()
        {
            var wait = new WaitForSecondsRealtime(GraceCheckIntervalSeconds);

            while (IsSpawned && !seatConnections.IsResolved)
            {
                if (!seatConnections.IsPaused && !seatConnections.IsHoldingSeats)
                {
                    break;
                }

                yield return wait;

                GraceExpiryDecision decision = seatConnections.Evaluate(NowMs());

                if (decision.Kind != GraceExpiryKind.None)
                {
                    ApplyGraceExpiry(decision);
                    break;
                }
            }

            graceCoroutine = null;
        }

        private void ApplyGraceExpiry(GraceExpiryDecision decision)
        {
            if (matchState == null || rulesEngine == null)
            {
                AbandonUnstartedMatch();
                return;
            }

            if (matchState.IsFinished)
            {
                return;
            }

            CommandResult result = decision.Kind == GraceExpiryKind.Forfeit
                ? rulesEngine.TryForfeitAbandonedSeat(matchState, decision.ForfeitingSeat)
                : rulesEngine.TryDrawAbandonedMatch(matchState);

            if (!result.Accepted)
            {
                Debug.LogWarning($"[Reconnect] Could not end the match: {result.RejectionReason}.");
                return;
            }

            Debug.Log(
                $"[Reconnect] Grace period over: {decision.Kind}"
                    + (decision.Kind == GraceExpiryKind.Forfeit ? $" ({decision.ForfeitingSeat})." : ".")
            );

            BroadcastUpdate();

            if (decision.Kind == GraceExpiryKind.Draw)
            {
                SkipSettlementForAbandonedDraw();
                return;
            }

            TrySettleFinishedMatch();
        }

        /// <summary>
        /// A player left before the match was created and did not return. Listen-host frees
        /// the seat for a new guest; a dedicated server cannot admit anyone else, so it ends.
        /// </summary>
        private void AbandonUnstartedMatch()
        {
            Debug.LogWarning("[Reconnect] A player never returned before the match started.");

            if (MatchServerContext.IsDedicated)
            {
                foreach (ulong clientId in new List<ulong>(seatByClient.Keys))
                {
                    NetworkManager.DisconnectClient(clientId, "Đối thủ không vào trận. Trận đấu bị hủy.");
                }

                return;
            }

            seatConnections.Reset();
            loadoutBySeat.Clear();
            loadoutTimeoutElapsed = false;
            currentMatchId = null;

            foreach (KeyValuePair<ulong, SeatId> pair in seatByClient)
            {
                BindSeat(pair.Key, pair.Value);
            }

            BroadcastUpdate();
        }

        #endregion

        #region Snapshot Connection State

        private SeatConnectionState ConnectionStateOf(SeatId seat)
        {
            if (seatConnections.IsBound(seat))
            {
                return seatConnections.StateOf(seat);
            }

            return HasSeat(seat) ? SeatConnectionState.Connected : SeatConnectionState.Empty;
        }

        /// <summary>FR-NET-04: both seats' connection state and the pause countdown.</summary>
        private void ApplyConnectionState(MatchSnapshotDto snapshot, SeatId viewerSeat)
        {
            SeatId opponentSeat = viewerSeat.Opponent();
            long deadline = seatConnections.DeadlineOf(opponentSeat);

            snapshot.selfConnection = (int)ConnectionStateOf(viewerSeat);
            snapshot.opponentConnection = (int)ConnectionStateOf(opponentSeat);
            snapshot.isPaused = seatConnections.IsPaused;
            snapshot.opponentReconnectDeadlineUtcMs = deadline;
            snapshot.opponentReconnectRemainingMs = deadline > 0 ? Math.Max(0, deadline - NowMs()) : 0;
        }

        private static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        #endregion
    }
}
