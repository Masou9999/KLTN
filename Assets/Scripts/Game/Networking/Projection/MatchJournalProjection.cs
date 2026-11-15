using System;
using System.Collections.Generic;
using KLTN.Game.Domain;

namespace KLTN.Game.Networking
{
    [Serializable]
    public sealed class MatchLogDto
    {
        public long sequence;
        public int round;
        public int category;
        public string text;
    }

    public static class MatchJournalProjection
    {
        public static MatchLogDto[] Build(IReadOnlyList<MatchLogEntry> entries, SeatId viewer)
        {
            var result = new List<MatchLogDto>();
            if (entries == null) return result.ToArray();
            foreach (var entry in entries)
            {
                string text = entry.TextFor(viewer);
                if (string.IsNullOrEmpty(text)) continue;
                result.Add(new MatchLogDto { sequence = entry.Sequence, round = entry.Round,
                    category = (int)entry.Category, text = text });
            }
            return result.ToArray();
        }
    }

    /// <summary>Independent of modal lifetime and animation consumption; reconnect replay is idempotent.</summary>
    public sealed class MatchJournalClient
    {
        private readonly List<MatchLogDto> entries = new List<MatchLogDto>();
        private readonly HashSet<long> sequences = new HashSet<long>();
        private string matchId;
        private int viewer = -1;
        public IReadOnlyList<MatchLogDto> Entries => entries.AsReadOnly();
        public event Action Changed;

        public void Accept(MatchUpdateDto update)
        {
            var snapshot = update?.snapshot;
            if (snapshot == null || string.IsNullOrEmpty(snapshot.matchId)) return;
            bool changed = false;
            if (matchId != snapshot.matchId || viewer != snapshot.viewerSeat)
            {
                entries.Clear(); sequences.Clear();
                matchId = snapshot.matchId; viewer = snapshot.viewerSeat; changed = true;
            }
            if (update.logs != null)
                foreach (var entry in update.logs)
                    if (entry != null && !string.IsNullOrEmpty(entry.text) && sequences.Add(entry.sequence))
                    { entries.Add(entry); changed = true; }
            if (!changed) return;
            entries.Sort((a, b) => a.sequence.CompareTo(b.sequence));
            Changed?.Invoke();
        }

        public void Reset()
        {
            entries.Clear(); sequences.Clear(); matchId = null; viewer = -1;
            Changed?.Invoke();
        }
    }
}
