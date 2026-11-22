using System;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace KLTN.Game.Presentation
{
    public sealed partial class MatchDebugControls : MonoBehaviour
    {
        #region Pending State

        private void ClearAbilityTargetDraftInternal()
        {
            pendingAbilityRequestId = null;

            pendingAbilityPrimaryTargets = Array.Empty<string>();

            pendingAbilitySecondaryTargets = Array.Empty<string>();

            pendingAbilitySelectionComplete = false;
        }

        private void ClearPendingReferences()
        {
            ClearPendingBoardStatPreviews();

            pendingSummonCard = null;
            pendingSummonCardInstanceId = null;

            Array.Clear(pendingBoardCards, 0, pendingBoardCards.Length);

            SetPendingManaCost(0);
        }

        private void ClearPendingBoardStatPreviews()
        {
            for (int i = 0; i < pendingBoardCards.Length; i++)
            {
                DraggableHandCard card = pendingBoardCards[i];

                if (card != null && card.Visual != null)
                {
                    card.Visual.ClearStatPreview();
                }
            }
        }

        private void ApplyPendingBoardStatPreviews()
        {
            MatchSnapshotDto snapshot = projection?.Current;

            if (
                snapshot == null
                || (!snapshot.viewerCanDeclareAttack && !snapshot.viewerCanDeclareBlock)
            )
            {
                return;
            }

            for (
                int supporterSlot = 0;
                supporterSlot < pendingBoardCards.Length - 1;
                supporterSlot++
            )
            {
                DraggableHandCard supporter = pendingBoardCards[supporterSlot];
                DraggableHandCard supportedUnit = pendingBoardCards[supporterSlot + 1];

                if (
                    supporter == null
                    || supportedUnit == null
                    || supporter.Visual == null
                    || supportedUnit.Visual == null
                )
                {
                    continue;
                }

                int damageBonus = supporter.Visual.SupportDamageBonus;
                int healthBonus = supporter.Visual.SupportHealthBonus;

                if (damageBonus == 0 && healthBonus == 0)
                {
                    continue;
                }

                supportedUnit.Visual.SetStatPreview(damageBonus, healthBonus);
            }
        }

        private void SetPendingManaCost(int value)
        {
            int sanitizedValue = Mathf.Max(0, value);

            if (pendingManaCost == sanitizedValue)
            {
                return;
            }

            pendingManaCost = sanitizedValue;
            PendingManaCostChanged?.Invoke(pendingManaCost);
        }

        private void RestorePendingVisuals()
        {
            if (pendingSummonCard != null)
            {
                pendingSummonCard.RestoreToHome();
            }

            for (int i = 0; i < pendingBoardCards.Length; i++)
            {
                if (pendingBoardCards[i] != null)
                {
                    pendingBoardCards[i].RestoreToHome();
                }
            }
        }

        #endregion

        #region Control Rendering

        private void RefreshControls()
        {
            MatchSnapshotDto snapshot = projection?.Current;

            if (snapshot != null && snapshot.roundNumber == 0)
            {
                return;
            }

            bool canPriorityAction = snapshot != null && snapshot.viewerCanAct;

            bool canBlock = snapshot != null && snapshot.viewerCanDeclareBlock;

            bool mustSelectAbilityTargets =
                snapshot != null
                && snapshot.viewerMustSelectAbilityTargets
                && snapshot.pendingAbilitySelection != null;

            bool abilityDraftReady =
                mustSelectAbilityTargets
                && pendingAbilitySelectionComplete
                && string.Equals(
                    pendingAbilityRequestId,
                    snapshot.pendingAbilitySelection.requestId,
                    StringComparison.Ordinal
                );

            bool canInteract =
                !submitting
                && (
                    mustSelectAbilityTargets
                        ? abilityDraftReady
                        : canPriorityAction || canBlock
                );

            if (actionButton != null)
            {
                actionButton.interactable = canInteract;
            }

            if (actionLabel == null)
            {
                return;
            }

            if (submitting)
            {
                actionLabel.text = "ĐANG XỬ LÝ";
            }
            else if (mustSelectAbilityTargets)
            {
                actionLabel.text = abilityDraftReady
                    ? "XÁC NHẬN KỸ NĂNG"
                    : "CHỌN MỤC TIÊU";
            }
            else if (canBlock)
            {
                actionLabel.text = "XÁC NHẬN PHÒNG THỦ";
            }
            else if (!canInteract)
            {
                actionLabel.text = "ĐỢI ĐỐI THỦ";
            }
            else if (pendingSummonCard != null)
            {
                actionLabel.text = "TRIỆU HỒI";
            }
            else if (HasPendingBoardDraft)
            {
                actionLabel.text = "TẤN CÔNG";
            }
            else if (snapshot.viewerCanEndRound)
            {
                actionLabel.text = "KẾT THÚC VÒNG";
            }
            else
            {
                actionLabel.text = "BỎ LƯỢT";
            }
        }

        #endregion

        #region Validation Helpers

        private static bool IsCardInSelfHand(MatchSnapshotDto snapshot, string instanceId)
        {
            foreach (CardViewDto card in snapshot.self.hand)
            {
                if (card.instanceId == instanceId)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        private int FindPendingBoardSlot(DraggableHandCard card)
        {
            for (int slotIndex = 0; slotIndex < pendingBoardCards.Length; slotIndex++)
            {
                if (pendingBoardCards[slotIndex] == card)
                {
                    return slotIndex;
                }
            }

            return -1;
        }

        private static bool IsCardInSelfReserve(
            MatchSnapshotDto snapshot,
            string instanceId
        )
        {
            if (snapshot?.self?.reserve == null)
            {
                return false;
            }

            foreach (CardViewDto card in snapshot.self.reserve)
            {
                if (card.instanceId == instanceId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasOpponentAttackerAtSlot(
            MatchSnapshotDto snapshot,
            int slotIndex
        )
        {
            if (snapshot?.opponent?.board == null)
            {
                return false;
            }

            foreach (CardViewDto card in snapshot.opponent.board)
            {
                if (card.boardSlotIndex == slotIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Mirrors the visible CannotBlock and Fearsome checks before a blocker is
        /// staged. The server repeats these checks when the declaration is committed.
        /// </summary>
        private static bool CanStageBlocker(
            MatchSnapshotDto snapshot,
            string blockerInstanceId,
            int slotIndex,
            out string rejectionReason
        )
        {
            rejectionReason = string.Empty;

            CardViewDto blocker = FindCard(snapshot?.self?.reserve, blockerInstanceId);

            if (blocker == null)
            {
                return false;
            }

            UnitKeyword blockerKeywords = (UnitKeyword)blocker.keywords;

            if ((blockerKeywords & UnitKeyword.CannotBlock) != 0)
            {
                rejectionReason = $"Không thể dùng {blocker.displayName} để phòng thủ.";

                return false;
            }

            CardViewDto attacker = FindBoardCard(snapshot?.opponent?.board, slotIndex);

            if (attacker == null)
            {
                rejectionReason = BuildDefendableSlotsStatus(snapshot);

                return false;
            }

            bool attackerIsFearsome =
                ((UnitKeyword)attacker.keywords & UnitKeyword.Fearsome) != 0;

            if (
                attackerIsFearsome
                && blocker.damage < MatchRulesEngine.FearsomeMinimumBlockerDamage
            )
            {
                rejectionReason =
                    $"{blocker.displayName} cần có chỉ số tấn công ít nhất là "
                    + $"{MatchRulesEngine.FearsomeMinimumBlockerDamage} để chặn "
                    + $"{attacker.displayName} có hiệu ứng Đáng sợ.";

                return false;
            }

            return true;
        }

        private static CardViewDto FindCard(CardViewDto[] cards, string instanceId)
        {
            if (cards == null || string.IsNullOrEmpty(instanceId))
            {
                return null;
            }

            foreach (CardViewDto card in cards)
            {
                if (card != null && card.instanceId == instanceId)
                {
                    return card;
                }
            }

            return null;
        }

        private static CardViewDto FindBoardCard(CardViewDto[] cards, int slotIndex)
        {
            if (cards == null)
            {
                return null;
            }

            foreach (CardViewDto card in cards)
            {
                if (card != null && card.boardSlotIndex == slotIndex)
                {
                    return card;
                }
            }

            return null;
        }

        private string[] BuildBoardCardIdsBySlot()
        {
            var result = new string[MatchState.BoardSlotCount];

            for (int slotIndex = 0; slotIndex < pendingBoardCards.Length; slotIndex++)
            {
                DraggableHandCard card = pendingBoardCards[slotIndex];

                result[slotIndex] = card?.Visual?.InstanceId;
            }

            return result;
        }

        #region Status Output

        private void SetLocalStatus(string message)
        {
            if (localStatusLabel == null)
            {
                return;
            }

            if (localStatusCanvasGroup == null)
            {
                localStatusContainer = localStatusLabel.transform.parent as RectTransform;
                if (localStatusContainer == null)
                {
                    localStatusContainer = localStatusLabel.rectTransform;
                }

                localStatusCanvasGroup = localStatusContainer.GetComponent<CanvasGroup>();
                if (localStatusCanvasGroup == null)
                {
                    localStatusCanvasGroup = localStatusContainer.gameObject.AddComponent<CanvasGroup>();
                }

                localStatusCanvasGroup.interactable = false;
                localStatusCanvasGroup.blocksRaycasts = false;
                localStatusCanvasGroup.alpha = 0f;
                localStatusBaseY = localStatusContainer.anchoredPosition.y;
            }

            localStatusSequence?.Kill();
            bool isVisible = localStatusCanvasGroup.alpha > 0.01f;

            if (string.IsNullOrWhiteSpace(message))
            {
                if (!isVisible)
                {
                    localStatusLabel.text = string.Empty;
                    return;
                }

                localStatusSequence = DOTween
                    .Sequence()
                    .Append(localStatusCanvasGroup.DOFade(0f, 0.18f).SetEase(Ease.InQuad))
                    .Join(localStatusContainer.DOScale(0.75f, 0.18f).SetEase(Ease.InBack))
                    .Join(
                        localStatusContainer
                            .DOAnchorPosY(localStatusBaseY - 20f, 0.18f)
                            .SetEase(Ease.InQuad)
                    )
                    .OnComplete(() => localStatusLabel.text = string.Empty);

                return;
            }

            localStatusLabel.text = message;
            localStatusSequence = DOTween.Sequence();

            if (!isVisible)
            {
                localStatusCanvasGroup.alpha = 0f;
                localStatusContainer.localScale = new Vector3(0.65f, 0.65f, 1f);
                localStatusContainer.anchoredPosition = new Vector2(
                    localStatusContainer.anchoredPosition.x,
                    localStatusBaseY - 20f
                );

                localStatusSequence
                    .Join(localStatusCanvasGroup.DOFade(1f, 0.12f).SetEase(Ease.OutQuad))
                    .Join(localStatusContainer.DOScale(1f, 0.22f).SetEase(Ease.OutBack))
                    .Join(
                        localStatusContainer
                            .DOAnchorPosY(localStatusBaseY, 0.22f)
                            .SetEase(Ease.OutCubic)
                    );
            }
            else
            {
                localStatusCanvasGroup.alpha = 1f;
                localStatusContainer.localScale = Vector3.one;
                localStatusContainer.anchoredPosition = new Vector2(
                    localStatusContainer.anchoredPosition.x,
                    localStatusBaseY
                );

                localStatusSequence
                    .Append(localStatusContainer.DOScale(1.06f, 0.06f).SetEase(Ease.OutQuad))
                    .Append(localStatusContainer.DOScale(1f, 0.08f).SetEase(Ease.InOutQuad));
            }

            localStatusSequence
                .AppendInterval(2.0f)
                .Append(localStatusCanvasGroup.DOFade(0f, 0.18f).SetEase(Ease.InQuad))
                .Join(localStatusContainer.DOScale(0.75f, 0.18f).SetEase(Ease.InBack))
                .Join(
                    localStatusContainer
                        .DOAnchorPosY(localStatusBaseY - 20f, 0.18f)
                        .SetEase(Ease.InQuad)
                )
                .OnComplete(() => localStatusLabel.text = string.Empty);
        }

        #endregion
    }
}
