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
            SetLocalStatus("Đang chờ host xác nhận...");
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
                SetLocalStatus("Không thể gửi command.");
                RefreshControls();
            }
        }

        #endregion

        #region Projection Events

        private void OnSnapshotChanged(MatchSnapshotDto snapshot)
        {
            bool completedLocalSubmission = submitting;
            submitting = false;

            if (completedLocalSubmission)
            {
                SetLocalStatus(string.Empty);
            }

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

            SetLocalStatus($"Host từ chối: {reason}");

            RefreshControls();
        }

        #endregion
    }
}
