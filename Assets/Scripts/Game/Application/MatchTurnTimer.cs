using System;
using KLTN.Game.Domain;

namespace KLTN.Game.ApplicationLayer
{
    /// <summary>
    /// Per-match server clock. All times use one monotonic clock supplied by the adapter.
    /// No client timestamps participate in acceptance. Readiness may SHORTEN the bounded
    /// presentation wait, but can never extend it or refresh an existing decision.
    /// </summary>
    public sealed class MatchTurnTimer
    {
        public const double DecisionSeconds = 20;
        public const double NetworkToleranceSeconds = 0.3;
        public const double MaximumPresentationWaitSeconds = 8;

        public sealed class Window
        {
            public ulong Id { get; internal set; }
            public SeatId Seat { get; internal set; }
            public double StartsAt { get; internal set; }
            public double Deadline { get; internal set; }
            public bool AwaitingPresentation { get; internal set; }
            internal MatchPhase Phase;
            internal int Round;
            internal ulong RequestId;
        }

        private readonly Window[] windows = new Window[2];
        private ulong nextId = 1;
        private double pausedAt;
        public bool IsPaused { get; private set; }
        public ulong Revision { get; private set; }
        public double EffectiveNow(double now) => IsPaused ? pausedAt : now;
        public Window For(SeatId seat) => windows[seat == SeatId.Host ? 0 : 1];

        public void Reconcile(MatchSession session, double now)
        {
            SetPaused(session.IsPaused, now);
            MatchState state = session.State;
            foreach (SeatId seat in new[] { SeatId.Host, SeatId.Guest })
            {
                bool wanted = state != null && !session.IsClosed && !state.IsFinished;
                MatchPhase phase = state?.Phase ?? MatchPhase.Finished;
                ulong request = 0;
                if (wanted && phase == MatchPhase.Mulligan)
                    wanted = !(seat == SeatId.Host ? state.HostMulliganDone : state.GuestMulliganDone);
                else if (wanted && phase == MatchPhase.AbilitySelection)
                {
                    wanted = state.PendingSelection != null && state.PendingSelection.ChoosingSeat == seat;
                    if (wanted && state.PendingSelection.CanCancel)
                        phase = MatchPhase.Priority; // Opening/cancelling a draft shares the original budget.
                    else if (wanted) request = state.PendingSelection.RequestId;
                }
                else
                    wanted = wanted && (phase == MatchPhase.Priority || phase == MatchPhase.BlockDeclaration)
                        && state.ActiveSeat == seat;

                Window existing = For(seat);
                if (!wanted) { Remove(seat); continue; }
                if (existing != null && existing.Phase == phase && existing.Round == state.RoundNumber
                    && existing.RequestId == request) continue;
                double start = EffectiveNow(now) + MaximumPresentationWaitSeconds;
                windows[seat == SeatId.Host ? 0 : 1] = new Window
                {
                    Id = nextId++, Seat = seat, Phase = phase, Round = state.RoundNumber,
                    RequestId = request, StartsAt = start, Deadline = start + DecisionSeconds,
                    AwaitingPresentation = true,
                };
                Revision++;
            }
        }

        public void Remove(SeatId seat)
        {
            int index = seat == SeatId.Host ? 0 : 1;
            if (windows[index] == null) return;
            windows[index] = null;
            Revision++;
        }

        public void SetPaused(bool paused, double now)
        {
            if (paused == IsPaused) return;
            if (paused) pausedAt = now;
            else
            {
                double shift = Math.Max(0, now - pausedAt);
                foreach (Window window in windows)
                {
                    if (window == null) continue;
                    window.StartsAt += shift;
                    window.Deadline += shift;
                }
            }
            IsPaused = paused;
            Revision++;
        }

        public bool MarkPresented(SeatId seat, ulong id, double now)
        {
            Window window = For(seat);
            if (IsPaused || window == null || window.Id != id || !window.AwaitingPresentation) return false;
            window.AwaitingPresentation = false;
            window.StartsAt = Math.Min(window.StartsAt, now);
            window.Deadline = window.StartsAt + DecisionSeconds;
            Revision++;
            return true;
        }

        public bool IsExpired(SeatId seat, double now)
        {
            Window window = For(seat);
            return !IsPaused && window != null && now >= window.Deadline + NetworkToleranceSeconds;
        }

        public CommandRejectionReason Validate(SeatId seat, ulong decisionId, double now)
        {
            if (IsPaused) return CommandRejectionReason.MatchPaused;
            Window window = For(seat);
            if (window == null || decisionId == 0 || window.Id != decisionId)
                return CommandRejectionReason.StaleDecision;
            return IsExpired(seat, now) ? CommandRejectionReason.DecisionTimedOut : CommandRejectionReason.None;
        }

        /// <summary>Applies expiry once. A new window gets a new ID even for the same player.</summary>
        public CommandResult Expire(MatchSession session, SeatId seat, double now)
        {
            Reconcile(session, now);
            if (!IsExpired(seat, now)) return null;
            CommandResult result = session.Rules.TryTimeout(session.State, seat);
            if (result.Accepted)
            {
                Remove(seat);
                Reconcile(session, now);
            }
            return result;
        }
    }
}
