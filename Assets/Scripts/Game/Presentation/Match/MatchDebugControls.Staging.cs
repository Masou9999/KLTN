using System;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    public sealed partial class MatchDebugControls : MonoBehaviour
    {
        #region Staging API

        /// <summary>
        /// Stages a hand card in Reserve as a summon preview without mutating domain
        /// state. A successful server command later rebuilds the view from a snapshot.
        /// </summary>
        public bool TryStageSummon(
            DraggableHandCard card,
            RectTransform reserveContentRoot
        )
        {
            MatchSnapshotDto snapshot = projection?.Current;

            if (card == null || snapshot == null || !snapshot.viewerCanAct || submitting)
            {
                RejectLocalAction(BuildSummonUnavailableStatus(snapshot));

                return false;
            }

            if (HasPendingBoardDraft)
            {
                RejectLocalAction("Chỉ có thể thực hiện 1 hành động trong lượt.");

                return false;
            }

            if (!IsCardInSelfHand(snapshot, card.Visual.InstanceId))
            {
                RejectLocalAction();

                return false;
            }

            if (snapshot.self.activeRosterCount >= MatchState.MaximumActiveRosterSize)
            {
                RejectLocalAction("Doanh trại đã đầy.");

                return false;
            }

            if (card.Visual.Energy > snapshot.self.mana)
            {
                RejectLocalAction("Không đủ năng lượng.");
                return false;
            }

            if (pendingSummonCard != null && pendingSummonCard != card)
            {
                pendingSummonCard.RestoreToHome();
            }

            pendingSummonCard = card;
            pendingSummonCardInstanceId = card.Visual.InstanceId;

            card.PlacePending(reserveContentRoot, CardVisualLocation.Reserve);

            SetPendingManaCost(card.Visual.Energy);

            RefreshControls();
            return true;
        }

        /// <summary>
        /// Places or swaps a Reserve card in the local attack/block draft and recalculates
        /// adjacent Support stat previews.
        /// </summary>
        public bool TryStageBoardCard(
            DraggableHandCard card,
            int slotIndex,
            RectTransform slotContent
        )
        {
            MatchSnapshotDto snapshot = projection?.Current;

            bool isAttackDraft = snapshot != null && snapshot.viewerCanDeclareAttack;

            bool isBlockDraft = snapshot != null && snapshot.viewerCanDeclareBlock;

            if (
                card == null
                || slotContent == null
                || snapshot == null
                || (!isAttackDraft && !isBlockDraft)
                || submitting
            )
            {
                RejectLocalAction();

                return false;
            }

            if (pendingSummonCard != null)
            {
                RejectLocalAction("Hãy nhấn Triệu hồi hoặc hủy bỏ triệu hồi.");

                return false;
            }

            if (slotIndex < 0 || slotIndex >= MatchState.BoardSlotCount)
            {
                return false;
            }

            if (card.HomeLocation != CardVisualLocation.Reserve)
            {
                RejectLocalAction();

                return false;
            }

            if (!IsCardInSelfReserve(snapshot, card.Visual.InstanceId))
            {
                RejectLocalAction();

                return false;
            }

            if (isBlockDraft && !HasOpponentAttackerAtSlot(snapshot, slotIndex))
            {
                RejectLocalAction(BuildDefendableSlotsStatus(snapshot));

                return false;
            }

            if (
                isBlockDraft
                && !CanStageBlocker(
                    snapshot,
                    card.Visual.InstanceId,
                    slotIndex,
                    out string reason
                )
            )
            {
                RejectLocalAction(reason);

                return false;
            }

            ClearPendingBoardStatPreviews();

            int sourceSlot = FindPendingBoardSlot(card);

            DraggableHandCard targetCard = pendingBoardCards[slotIndex];

            if (sourceSlot == slotIndex)
            {
                card.PlacePending(slotContent, CardVisualLocation.Board);

                ApplyPendingBoardStatPreviews();

                return true;
            }

            RectTransform sourceContent = card.PendingParent;

            if (sourceSlot >= 0)
            {
                if (targetCard != null && sourceContent != null)
                {
                    pendingBoardCards[sourceSlot] = targetCard;

                    targetCard.MovePending(sourceContent, CardVisualLocation.Board);
                }
                else
                {
                    pendingBoardCards[sourceSlot] = null;

                    if (targetCard != null)
                    {
                        targetCard.RestoreToHome();
                    }
                }
            }
            else if (targetCard != null)
            {
                targetCard.RestoreToHome();
            }

            pendingBoardCards[slotIndex] = card;

            card.PlacePending(slotContent, CardVisualLocation.Board);

            ApplyPendingBoardStatPreviews();

            RefreshControls();
            return true;
        }

        public bool TryReturnBoardCardToReserve(DraggableHandCard card)
        {
            MatchSnapshotDto snapshot = projection?.Current;

            if (
                card == null
                || card.HomeLocation != CardVisualLocation.Reserve
                || submitting
                || snapshot == null
                || (!snapshot.viewerCanDeclareAttack && !snapshot.viewerCanDeclareBlock)
                || !IsCardInSelfReserve(snapshot, card.Visual.InstanceId)
            )
            {
                return false;
            }

            ClearPendingBoardStatPreviews();

            int slotIndex = FindPendingBoardSlot(card);

            if (slotIndex >= 0)
            {
                pendingBoardCards[slotIndex] = null;
            }

            card.RestoreToHome();

            ApplyPendingBoardStatPreviews();

            RefreshControls();
            return true;
        }

        public void ClearPendingSelection()
        {
            RestorePendingVisuals();
            ClearPendingReferences();

            RefreshControls();
        }

        public bool TryReturnPendingSummon(DraggableHandCard card)
        {
            if (
                card == null
                || card != pendingSummonCard
                || !card.IsPending
                || submitting
            )
            {
                return false;
            }

            card.RestoreToHome();

            pendingSummonCard = null;
            pendingSummonCardInstanceId = null;

            SetPendingManaCost(0);

            RefreshControls();
            return true;
        }

        #endregion
    }
}
