using System;
using KLTN.Game.ApplicationLayer;
using KLTN.Game.Domain;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    public sealed partial class NetworkMatchBridge
    {
        // NGO network time on the server is independent of Time.timeScale and wall-clock edits.
        private double TurnNow => NetworkManager.LocalTime.Time;
        private ulong CurrentDecisionId => MatchProjectionRegistry.Current.Current?.turnClock?.self?.decisionId ?? 0;

        private void Update()
        {
            if (!IsServer || !IsSpawned || sessions == null) return;
            foreach (MatchSession session in sessions.Sessions) CheckTurnExpiry(session, TurnNow);
        }

        private void CheckTurnExpiry(MatchSession session, double now)
        {
            if (!session.IsStarted || session.IsClosed || session.IsFinished) return;
            session.TurnTimer.Reconcile(session, now);
            foreach (SeatId seat in new[] { SeatId.Host, SeatId.Guest })
            {
                CommandResult result = session.TurnTimer.Expire(session, seat, now);
                if (result == null) continue;
                if (!result.Accepted)
                {
                    Debug.LogError($"Timeout rejected in {session.MatchId}: {result.RejectionReason}");
                    continue;
                }
                BroadcastUpdate(session, result.Resolution, result.RoundTransition);
                TrySettleFinishedMatch(session);
            }
        }

        private MatchCommandOutcome ExecuteTimedCommand(ulong sender, ulong commandId, ulong decisionId,
            Func<MatchSession, SeatId, CommandResult> command, bool completesDecision = true)
        {
            double receivedAt = TurnNow;
            if (sessions.TryGetByConnection(sender, out MatchSession session))
                CheckTurnExpiry(session, receivedAt);
            return commandService.ExecuteTimed(sender, commandId, decisionId, receivedAt, command, completesDecision);
        }

        /// <summary>Only the owner of a decision may shorten its bounded presentation wait.</summary>
        public void RequestDecisionPresented(MatchSnapshotDto snapshot)
        {
            if (!IsSpawned || !IsClient || snapshot?.turnClock?.self == null) return;
            if (snapshot.turnClock.self.awaitingPresentation)
                SubmitDecisionPresentedRpc(snapshot.turnClock.self.decisionId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitDecisionPresentedRpc(ulong decisionId, RpcParams rpcParams = default)
        {
            if (!sessions.TryGetByConnection(rpcParams.Receive.SenderClientId, out MatchSession session)
                || !session.TryGetSeat(rpcParams.Receive.SenderClientId, out SeatId seat)
                || !session.IsStarted || session.IsFinished || session.IsClosed) return;
            double now = TurnNow;
            CheckTurnExpiry(session, now);
            if (!session.TurnTimer.MarkPresented(seat, decisionId, now)) return;
            // Clock-only publication must not clear a staged card or a pending Commit.
            foreach (var pair in session.Connections)
                ReceiveTurnClockRpc(JsonUtility.ToJson(BuildTurnClock(session, pair.Value)),
                    RpcTarget.Single(pair.Key, RpcTargetUse.Temp));
        }

        private MatchTurnClockDto BuildTurnClock(MatchSession session, SeatId viewer)
        {
            session.TurnTimer.Reconcile(session, TurnNow);
            return new MatchTurnClockDto
            {
                matchId = session.MatchId, revision = session.TurnTimer.Revision,
                paused = session.TurnTimer.IsPaused,
                serverNow = session.TurnTimer.EffectiveNow(TurnNow),
                self = BuildTurnWindow(session.TurnTimer.For(viewer)),
                opponent = BuildTurnWindow(session.TurnTimer.For(viewer.Opponent())),
                selfTimeouts = session.State?.Player(viewer).ConsecutiveTimeouts ?? 0,
                opponentTimeouts = session.State?.Player(viewer.Opponent()).ConsecutiveTimeouts ?? 0,
                timedOutSeat = session.State?.TimedOutSeat.HasValue == true ? (int)session.State.TimedOutSeat.Value : -1,
                outcome = (int)(session.State?.Outcome ?? MatchOutcome.Running), viewerSeat = (int)viewer,
            };
        }

        private static TurnWindowDto BuildTurnWindow(MatchTurnTimer.Window window)
        {
            return window == null ? null : new TurnWindowDto
            {
                decisionId = window.Id, startsAt = window.StartsAt, deadline = window.Deadline,
                awaitingPresentation = window.AwaitingPresentation,
            };
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveTurnClockRpc(string json, RpcParams rpcParams = default)
        {
            MatchTurnClockRegistry.Apply(JsonUtility.FromJson<MatchTurnClockDto>(json));
        }
    }
}
