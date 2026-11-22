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
        #region Command Submission

        private void CommitOrPass()
        {
#if UNITY_EDITOR
            if (EditorSubmitOverride != null)
            {
                EditorSubmitOverride();
                return;
            }
#endif
            MatchSnapshotDto snapshot = projection?.Current;

            if (snapshot != null && snapshot.roundNumber == 0)
            {
                return;
            }

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

            bool canSubmit =
                snapshot != null
                && !submitting
                && bridge != null
                && (
                    mustSelectAbilityTargets
                        ? abilityDraftReady
                        : snapshot.viewerCanAct || snapshot.viewerCanDeclareBlock
                );

            if (!canSubmit)
            {
                return;
            }

            // Must be set before sending because a host RPC can complete synchronously.
            submitting = true;
            RefreshControls();

            bool sent;

            if (mustSelectAbilityTargets)
            {
                sent = bridge.RequestSubmitAbilitySelection(
                    pendingAbilityRequestId,
                    pendingAbilityPrimaryTargets,
                    pendingAbilitySecondaryTargets
                );
            }
            else if (pendingSummonCard != null)
            {
                sent = bridge.RequestSummonUnit(pendingSummonCardInstanceId);
            }
            else if (snapshot.viewerCanDeclareBlock)
            {
                sent = bridge.RequestDeclareBlock(BuildBoardCardIdsBySlot());
            }
            else if (HasPendingBoardDraft)
            {
                sent = bridge.RequestDeclareAttack(BuildBoardCardIdsBySlot());
            }
            else
            {
                sent = bridge.RequestPass();
            }

            if (!sent)
            {
                submitting = false;
                RejectLocalAction("Không thể thực hiện thao tác.");
                RefreshControls();
            }
        }

        #endregion

        #region Projection Events

        private void OnSnapshotChanged(MatchSnapshotDto snapshot)
        {
            submitting = false;

            string authoritativeRequestId =
                snapshot != null
                && snapshot.viewerMustSelectAbilityTargets
                && snapshot.pendingAbilitySelection != null
                    ? snapshot.pendingAbilitySelection.requestId
                    : null;

            if (
                !string.Equals(
                    pendingAbilityRequestId,
                    authoritativeRequestId,
                    StringComparison.Ordinal
                )
            )
            {
                ClearAbilityTargetDraftInternal();
            }

            ClearPendingReferences();
            RefreshControls();
        }

        private void OnCommandRejected(CommandRejectionReason reason)
        {
            submitting = false;

            RestorePendingVisuals();
            ClearPendingReferences();

            // Lý do từ chối chỉ phục vụ gỡ lỗi; người chơi chỉ nghe âm báo lỗi.
            Debug.LogWarning(
                $"[MatchDebugControls] Host từ chối lệnh: {reason}",
                this
            );
            PlayActionRejectedSfx();

            RefreshControls();
        }

        #endregion
    }
}
