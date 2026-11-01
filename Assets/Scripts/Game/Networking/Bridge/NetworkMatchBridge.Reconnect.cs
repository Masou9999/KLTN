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

        /// <summary>Seats are bound to player keys, held for 30 s after a drop (FR-NET-06/07).</summary>
        private void BindSeat(MatchSession session, ulong clientId, SeatId seat)
        {
            if (session.Seats.IsBound(seat))
            {
                return;
            }

            string playerKey = PlayerKeyFor(clientId);

            if (!string.IsNullOrEmpty(playerKey))
            {
                session.Seats.Bind(seat, playerKey);
            }
        }

        /// <summary>
        /// Gives a returning player their held seat back (FR-NET-07, FR-MM-08), then sends the
        /// full snapshot at the current revision to both players and resumes the match.
        /// Returns true when the client was handled here (reseated or refused).
        /// </summary>
        private bool TryReclaimSeat(MatchSession session, ulong clientId)
        {
            string playerKey = PlayerKeyFor(clientId);
            SeatConnectionTracker seats = session.Seats;

            if (
                string.IsNullOrEmpty(playerKey)
                || !seats.TryFindSeat(playerKey, out SeatId heldSeat)
                || seats.StateOf(heldSeat) == SeatConnectionState.Connected
            )
            {
                return false;
            }

            ReconnectResult result = seats.TryReconnect(playerKey, NowMs(), out SeatId seat);

            if (result != ReconnectResult.Reconnected)
            {
                Debug.LogWarning($"[Reconnect] Client {clientId} refused for {heldSeat} of {session.MatchId}: {result}.");
                NetworkManager.DisconnectClient(clientId, SeatExpiredReason);
                return true;
            }

            session.Reseat(clientId, seat);
            Debug.Log($"[Reconnect] Client {clientId} took back {seat} of {session.MatchId}. Paused={seats.IsPaused}.");

            BroadcastUpdate(session);
            ResendSettlement(session, clientId, seat);
            return true;
        }

        #endregion

        #region Disconnect Handling

        /// <summary>
        /// Holds the seat instead of discarding the match: commands pause, the opponent is told
        /// at once through a snapshot, and the server timer decides if nobody comes back.
        /// </summary>
        private void HoldSeat(MatchSession session, SeatId seat)
        {
            if (!session.Seats.MarkDisconnected(seat, NowMs()))
            {
                return;
            }

            Debug.Log(
                $"[Reconnect] {seat} of {session.MatchId} disconnected; holding the seat for "
                    + $"{session.Seats.GracePeriodMilliseconds / 1000} s."
            );

            MatchRuntime runtime = RuntimeOf(session);

            if (runtime.GraceCoroutine == null)
            {
                runtime.GraceCoroutine = StartCoroutine(RunGraceTimer(session, runtime));
            }
        }

        private IEnumerator RunGraceTimer(MatchSession session, MatchRuntime runtime)
        {
            var wait = new WaitForSecondsRealtime(GraceCheckIntervalSeconds);
            SeatConnectionTracker seats = session.Seats;

            while (IsSpawned && !seats.IsResolved && !session.IsClosed)
            {
                if (!seats.IsPaused && !seats.IsHoldingSeats)
                {
                    break;
                }

                yield return wait;

                GraceExpiryDecision decision = seats.Evaluate(NowMs());

                if (decision.Kind != GraceExpiryKind.None)
                {
                    ApplyGraceExpiry(session, decision);
                    break;
                }
            }

            runtime.GraceCoroutine = null;
        }

        private void ApplyGraceExpiry(MatchSession session, GraceExpiryDecision decision)
        {
            if (!session.IsStarted)
            {
                AbandonUnstartedMatch(session);
                return;
            }

            if (session.IsFinished)
            {
                return;
            }

            CommandResult result = decision.Kind == GraceExpiryKind.Forfeit
                ? session.Rules.TryForfeitAbandonedSeat(session.State, decision.ForfeitingSeat)
                : session.Rules.TryDrawAbandonedMatch(session.State);

            if (!result.Accepted)
            {
                Debug.LogWarning($"[Reconnect] Could not end match {session.MatchId}: {result.RejectionReason}.");
                return;
            }

            Debug.Log(
                $"[Reconnect] Grace period over for {session.MatchId}: {decision.Kind}"
                    + (decision.Kind == GraceExpiryKind.Forfeit ? $" ({decision.ForfeitingSeat})." : ".")
            );

            BroadcastUpdate(session);

            if (decision.Kind == GraceExpiryKind.Draw)
            {
                SkipSettlementForAbandonedDraw(session);
                return;
            }

            TrySettleFinishedMatch(session);
        }

        /// <summary>
        /// A player left before the match was created and did not return. Listen-host frees
        /// the seat for a new guest; a dedicated match cannot admit anyone else, so it ends
        /// (the other matches of the server keep running).
        /// </summary>
        private void AbandonUnstartedMatch(MatchSession session)
        {
            Debug.LogWarning($"[Reconnect] A player never returned before match {session.MatchId} started.");

            if (MatchServerContext.IsDedicated)
            {
                session.Close();

                foreach (ulong clientId in new List<ulong>(session.Connections.Keys))
                {
                    NetworkManager.DisconnectClient(clientId, "Đối thủ không vào trận. Trận đấu bị hủy.");
                }

                return;
            }

            MatchRuntime runtime = RuntimeOf(session);

            if (runtime.LoadoutTimeoutCoroutine != null)
            {
                StopCoroutine(runtime.LoadoutTimeoutCoroutine);
                runtime.LoadoutTimeoutCoroutine = null;
            }

            runtime.LoadoutTimeoutElapsed = false;
            session.ResetUnstarted();

            foreach (KeyValuePair<ulong, SeatId> pair in session.Connections)
            {
                BindSeat(session, pair.Key, pair.Value);
            }

            BroadcastUpdate(session);
        }

        #endregion

        #region Snapshot Connection State

        private static SeatConnectionState ConnectionStateOf(MatchSession session, SeatId seat)
        {
            if (session.Seats.IsBound(seat))
            {
                return session.Seats.StateOf(seat);
            }

            return session.HasConnectedSeat(seat) ? SeatConnectionState.Connected : SeatConnectionState.Empty;
        }

        /// <summary>FR-NET-04: both seats' connection state and the pause countdown.</summary>
        private static void ApplyConnectionState(MatchSession session, MatchSnapshotDto snapshot, SeatId viewerSeat)
        {
            SeatId opponentSeat = viewerSeat.Opponent();
            long deadline = session.Seats.DeadlineOf(opponentSeat);

            snapshot.selfConnection = (int)ConnectionStateOf(session, viewerSeat);
            snapshot.opponentConnection = (int)ConnectionStateOf(session, opponentSeat);
            snapshot.isPaused = session.Seats.IsPaused;
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
