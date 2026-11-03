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
        #region Ability Selection Draft

        public void SetAbilityTargetDraft(
            string requestId,
            string[] primaryTargets,
            string[] secondaryTargets,
            bool complete
        )
        {
            pendingAbilityRequestId = requestId;

            pendingAbilityPrimaryTargets =
                primaryTargets == null
                    ? Array.Empty<string>()
                    : (string[])primaryTargets.Clone();

            pendingAbilitySecondaryTargets =
                secondaryTargets == null
                    ? Array.Empty<string>()
                    : (string[])secondaryTargets.Clone();

            pendingAbilitySelectionComplete = complete;

            RefreshControls();
        }

        public void ClearAbilityTargetDraft()
        {
            ClearAbilityTargetDraftInternal();
            RefreshControls();
        }

        public bool CancelPendingAbilitySelection(string requestId)
        {
#if UNITY_EDITOR
            if (EditorCancelOverride != null)
            {
                return EditorCancelOverride(requestId);
            }
#endif
            MatchSnapshotDto snapshot = projection?.Current;

            bool validRequest =
                snapshot != null
                && snapshot.viewerMustSelectAbilityTargets
                && snapshot.pendingAbilitySelection != null
                && snapshot.pendingAbilitySelection.canCancel
                && snapshot.pendingAbilitySelection.requestId == requestId;

            if (!validRequest || submitting || bridge == null)
            {
                return false;
            }

            // Must be set before sending because host RPC may update the snapshot immediately.
            submitting = true;
            SetLocalStatus("Đang chờ host hủy kỹ năng...");
            RefreshControls();

            bool sent = bridge.RequestCancelAbilitySelection(requestId);

            if (!sent)
            {
                submitting = false;
                SetLocalStatus("Không thể gửi yêu cầu hủy kỹ năng.");
                RefreshControls();
                return false;
            }

            return true;
        }

        #endregion
    }
}
