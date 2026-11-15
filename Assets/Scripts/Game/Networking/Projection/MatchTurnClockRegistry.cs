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
            if (Current != null && Current.matchId == clock.matchId && Current.revision > clock.revision) return;
            Current = clock;
            if (acknowledgesSubmission || clock.self?.decisionId != PendingDecisionId)
                PendingDecisionId = 0;
            Changed?.Invoke();
        }
        public static void Reset()
        {
            Current = null;
            PendingDecisionId = 0;
            Changed?.Invoke();
        }
    }
}
