using KLTN.Game.Domain;
using KLTN.Game.Networking;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Shows the comet hint on board slots that can accept the Reserve card being
    /// dragged. Visual guidance only; staging still goes through TryStageBoardCard.
    /// </summary>
    public sealed partial class MatchDebugControls
    {
        #region Runtime State

        private readonly BoardSlotDropZone[] boardSlotZones = new BoardSlotDropZone[
            MatchState.BoardSlotCount
        ];
        private readonly bool[] dropHintOccupied = new bool[MatchState.BoardSlotCount];
        private readonly bool[] dropHintDefendable = new bool[MatchState.BoardSlotCount];
        private DraggableHandCard activeReserveDrag;

        #endregion

        #region Slot Registration

        public void RegisterBoardSlot(BoardSlotDropZone zone)
        {
            if (zone == null || zone.SlotIndex < 0 || zone.SlotIndex >= boardSlotZones.Length)
            {
                return;
            }

            boardSlotZones[zone.SlotIndex] = zone;
            RefreshBoardDropHints();
        }

        public void UnregisterBoardSlot(BoardSlotDropZone zone)
        {
            if (
                zone != null
                && zone.SlotIndex >= 0
                && zone.SlotIndex < boardSlotZones.Length
                && boardSlotZones[zone.SlotIndex] == zone
            )
            {
                boardSlotZones[zone.SlotIndex] = null;
            }
        }

        #endregion

        #region Drag Events

        private void SubscribeReserveDragEvents()
        {
            DraggableHandCard.ReserveDragBegan += OnReserveDragBegan;
            DraggableHandCard.ReserveDragEnded += OnReserveDragEnded;
        }

        private void UnsubscribeReserveDragEvents()
        {
            DraggableHandCard.ReserveDragBegan -= OnReserveDragBegan;
            DraggableHandCard.ReserveDragEnded -= OnReserveDragEnded;
            activeReserveDrag = null;
            RefreshBoardDropHints();
        }

        private void OnReserveDragBegan(DraggableHandCard card)
        {
            activeReserveDrag = card;
            RefreshBoardDropHints();
        }

        private void OnReserveDragEnded(DraggableHandCard card)
        {
            if (activeReserveDrag == card)
            {
                activeReserveDrag = null;
            }

            RefreshBoardDropHints();
        }

        #endregion

        #region Hint Evaluation

        private void RefreshBoardDropHints()
        {
            MatchSnapshotDto snapshot = projection?.Current;
            BoardDropHintMode mode = ResolveDropHintMode(snapshot);
            string draggedId =
                activeReserveDrag != null && activeReserveDrag.Visual != null
                    ? activeReserveDrag.Visual.InstanceId
                    : null;

            for (int slot = 0; slot < MatchState.BoardSlotCount; slot++)
            {
                DraggableHandCard staged = pendingBoardCards[slot];

                // The dragged card's own slot counts as free: dropping back or swapping is valid.
                dropHintOccupied[slot] =
                    (staged != null && staged != activeReserveDrag)
                    || FindBoardCard(snapshot?.self?.board, slot) != null;

                dropHintDefendable[slot] =
                    mode == BoardDropHintMode.Block
                    && CanStageBlocker(snapshot, draggedId, slot, out _);
            }

            bool[] eligible = BoardDropHintRules.ComputeEligibleSlots(
                mode,
                MatchState.BoardSlotCount,
                dropHintOccupied,
                dropHintDefendable
            );

            for (int slot = 0; slot < boardSlotZones.Length; slot++)
            {
                BoardSlotDropZone zone = boardSlotZones[slot];

                if (zone != null)
                {
                    zone.SetDropHint(eligible[slot]);
                }
            }
        }

        private BoardDropHintMode ResolveDropHintMode(MatchSnapshotDto snapshot)
        {
            if (
                activeReserveDrag == null
                || activeReserveDrag.Visual == null
                || snapshot == null
                || submitting
                || pendingSummonCard != null
                || !IsCardInSelfReserve(snapshot, activeReserveDrag.Visual.InstanceId)
            )
            {
                return BoardDropHintMode.None;
            }

            if (snapshot.viewerCanDeclareAttack)
            {
                return BoardDropHintMode.Attack;
            }

            return snapshot.viewerCanDeclareBlock
                ? BoardDropHintMode.Block
                : BoardDropHintMode.None;
        }

        #endregion
    }
}
