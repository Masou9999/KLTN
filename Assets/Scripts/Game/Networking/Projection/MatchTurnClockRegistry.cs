using System;

namespace KLTN.Game.Networking
{
    /// <summary>Fresh clock data bypasses animation queues; it never mutates the board projection.</summary>
    public static class MatchTurnClockRegistry
    {
        public static MatchTurnClockDto Current { get; private set; }
        public static ulong PendingDecisionId { get; private set; }
        public static event Action Changed;
        public static void MarkSubmitted(ulong decisionId)
        {
            PendingDecisionId = decisionId;
            Changed?.Invoke();
        }
        public static void ClearSubmission()
        {
            PendingDecisionId = 0;
            Changed?.Invoke();
        }
        public static void Apply(MatchTurnClockDto clock, bool acknowledgesSubmission = false)
        {
            if (clock == null) return;
            Normalize(clock);
            if (Current != null && Current.matchId == clock.matchId && Current.revision > clock.revision) return;
            Current = clock;
            if (acknowledgesSubmission || clock.self?.decisionId != PendingDecisionId)
                PendingDecisionId = 0;
            Changed?.Invoke();
        }
        /// <summary>
        /// Restores "no window" after JSON transport. JsonUtility turns a null
        /// <see cref="TurnWindowDto"/> into a default instance, and decision IDs start at 1,
        /// so a window with ID 0 means the seat has no running clock.
        /// </summary>
        public static void Normalize(MatchTurnClockDto clock)
        {
            if (clock == null) return;
            if (clock.self != null && clock.self.decisionId == 0) clock.self = null;
            if (clock.opponent != null && clock.opponent.decisionId == 0) clock.opponent = null;
        }

        public static void Reset()
        {
            Current = null;
            PendingDecisionId = 0;
            Changed?.Invoke();
        }
    }
}
