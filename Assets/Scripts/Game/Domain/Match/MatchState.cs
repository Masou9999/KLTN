using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Aggregate root for one deterministic match generation. It contains no Unity or
    /// networking types and is mutated only by domain services and guarded zone methods.
    /// </summary>
    public sealed class MatchState
    {
        #region Constants

        public const int BoardSlotCount = 3;
        public const int MaximumActiveRosterSize = 4;

        #endregion

        #region Match State

        public MatchJournal Journal { get; } = new MatchJournal();

        public PlayerState Host { get; } = new PlayerState(SeatId.Host);
        public PlayerState Guest { get; } = new PlayerState(SeatId.Guest);

        public SeatId FirstSeat { get; }
        public SeatId ActiveSeat { get; set; }

        public SeatId AttackTokenOwner { get; set; }
        public bool AttackTokenAvailable { get; set; }

        public MatchPhase Phase { get; set; } = MatchPhase.Mulligan;

        public int ConsecutivePasses { get; set; }

        public bool CanEndRound => Phase == MatchPhase.Priority && ConsecutivePasses == 1;

        public int RoundNumber { get; set; } = 0;
        public RoundHistory RoundHistory { get; } = new RoundHistory();
        public bool HostMulliganDone { get; set; }
        public bool GuestMulliganDone { get; set; }
        public bool IsMulliganPhaseComplete => HostMulliganDone && GuestMulliganDone;

        public MatchOutcome Outcome { get; set; } = MatchOutcome.Running;
        public string LastEvent { get; set; } = "Đang chờ đối thủ đổi bài (Mulligan)...";
        public bool IsFinished => Outcome != MatchOutcome.Running;

        /// <summary>Seat that surrendered, or null when the match did not end by surrender.</summary>
        public SeatId? SurrenderedSeat { get; set; }
        public SeatId? TimedOutSeat { get; internal set; }
        private ulong nextAbilitySelectionRequestId = 1;
        private ulong nextGeneratedCardInstanceId = 1;
        private long nextAbilityResolutionSequence = 1;
        private readonly List<AbilityResolutionEvent> pendingAbilityResolutionEvents =
            new List<AbilityResolutionEvent>();
        public PendingAbilitySelection PendingSelection { get; private set; }

        #endregion

        #region Ability Resolution Publication

        public long NextAbilityResolutionSequence()
        {
            return nextAbilityResolutionSequence++;
        }

        public void RecordAbilityResolution(AbilityResolutionEvent resolutionEvent)
        {
            if (resolutionEvent == null)
            {
                throw new ArgumentNullException(nameof(resolutionEvent));
            }

            pendingAbilityResolutionEvents.Add(resolutionEvent);
            Journal.Ability(RoundNumber, resolutionEvent, resolutionEvent.JournalContext, resolutionEvent.Duration);
        }

        /// <summary>Returns and clears the effects applied since the last publication.</summary>
        public AbilityResolution TakeAbilityResolution()
        {
            if (pendingAbilityResolutionEvents.Count == 0)
            {
                return null;
            }

            var result = new AbilityResolution(pendingAbilityResolutionEvents.ToArray());
            pendingAbilityResolutionEvents.Clear();
            return result;
        }

        #endregion

        #region Construction

        public MatchState()
            : this(SeatId.Host) { }

        public MatchState(SeatId firstSeat)
        {
            if (firstSeat != SeatId.Host && firstSeat != SeatId.Guest)
            {
                throw new ArgumentOutOfRangeException(nameof(firstSeat));
            }

            FirstSeat = firstSeat;
            ActiveSeat = firstSeat;

            AttackTokenOwner = firstSeat;
            AttackTokenAvailable = false;

            Phase = MatchPhase.Mulligan;
            ConsecutivePasses = 0;
        }

        public void BeginFirstRound()
        {
            if (!IsMulliganPhaseComplete)
            {
                throw new InvalidOperationException(
                    "Both players must complete mulligan first."
                );
            }

            if (RoundNumber > 0)
            {
                return;
            }

            RoundNumber = 1;
            RoundHistory.BeginFirstRound(RoundNumber);
            ActiveSeat = AttackTokenOwner;
            AttackTokenAvailable = true;

            ConsecutivePasses = 0;
            Phase = MatchPhase.Priority;
        }

        #endregion

        #region Player Access

        public PlayerState Player(SeatId seat)
        {
            switch (seat)
            {
                case SeatId.Host:
                    return Host;

                case SeatId.Guest:
                    return Guest;

                default:
                    throw new ArgumentOutOfRangeException(nameof(seat));
            }
        }

        public void EnsureNextCardInstanceIdAtLeast(ulong nextInstanceId)
        {
            if (nextInstanceId == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nextInstanceId));
            }

            if (nextInstanceId > nextGeneratedCardInstanceId)
            {
                nextGeneratedCardInstanceId = nextInstanceId;
            }
        }

        public ulong AllocateCardInstanceId()
        {
            while (ContainsCardInstanceId(nextGeneratedCardInstanceId))
            {
                if (nextGeneratedCardInstanceId == ulong.MaxValue)
                {
                    throw new InvalidOperationException(
                        "Card instance ID space was exhausted."
                    );
                }

                nextGeneratedCardInstanceId++;
            }

            if (nextGeneratedCardInstanceId == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "Card instance ID space was exhausted."
                );
            }

            return nextGeneratedCardInstanceId++;
        }

        private bool ContainsCardInstanceId(ulong instanceId)
        {
            return Host.FindCardAnywhere(instanceId) != null
                || Guest.FindCardAnywhere(instanceId) != null;
        }

        #endregion

        /// <summary>
        /// Suspends the current phase for an atomic ability-target selection request.
        /// Only one request may be active at a time.
        /// </summary>
        public bool TryOpenPendingSelection(
            TriggeredAbility triggeredAbility,
            bool canCancel = false
        )
        {
            if (triggeredAbility == null)
            {
                throw new ArgumentNullException(nameof(triggeredAbility));
            }

            if (
                PendingSelection != null
                || triggeredAbility.Ability.RequiredTargets.Count == 0
            )
            {
                return false;
            }

            MatchPhase resumePhase = Phase;

            PendingSelection = new PendingAbilitySelection(
                nextAbilitySelectionRequestId++,
                triggeredAbility,
                resumePhase,
                canCancel
            );

            Phase = MatchPhase.AbilitySelection;

            return true;
        }

        /// <summary>
        /// Closes the matching request and restores the phase captured when it opened.
        /// </summary>
        public bool TryClosePendingSelection(ulong requestId)
        {
            if (PendingSelection == null || PendingSelection.RequestId != requestId)
            {
                return false;
            }

            MatchPhase resumePhase = PendingSelection.ResumePhase;

            PendingSelection = null;
            Phase = resumePhase;

            return true;
        }
    }
}
