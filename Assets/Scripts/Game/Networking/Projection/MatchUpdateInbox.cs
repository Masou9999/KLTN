using System;
using System.Collections.Generic;

namespace KLTN.Game.Networking
{
    public sealed class MatchUpdateInbox
    {
        #region Events

        public event Action UpdateAvailable;
        public MatchJournalClient Journal { get; } = new MatchJournalClient();

        #endregion

        #region Fields

        private readonly Queue<MatchUpdateDto> updates = new Queue<MatchUpdateDto>();

        private ulong latestAcceptedRevision;
        private bool hasAcceptedRevision;

        #endregion

        #region Queue Operations

        public void Enqueue(MatchUpdateDto update)
        {
            if (update?.snapshot == null)
            {
                return;
            }

            Journal.Accept(update);
            ulong revision = update.snapshot.revision;

            if (hasAcceptedRevision && revision <= latestAcceptedRevision)
            {
                return;
            }

            latestAcceptedRevision = revision;
            hasAcceptedRevision = true;

            updates.Enqueue(update);
            UpdateAvailable?.Invoke();
        }

        public bool TryDequeue(out MatchUpdateDto update)
        {
            if (updates.Count == 0)
            {
                update = null;
                return false;
            }

            update = updates.Dequeue();
            return true;
        }

        public void Reset()
        {
            updates.Clear();
            Journal.Reset();
            latestAcceptedRevision = 0;
            hasAcceptedRevision = false;
        }

        #endregion
    }

    public static class MatchUpdateInboxRegistry
    {
        public static MatchUpdateInbox Current { get; } = new MatchUpdateInbox();
    }
}
