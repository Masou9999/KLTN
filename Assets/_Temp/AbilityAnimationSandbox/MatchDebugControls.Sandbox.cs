#if UNITY_EDITOR
using KLTN.Sandbox;

namespace KLTN.Game.Presentation
{
    public sealed partial class MatchDebugControls
    {
        /// <summary>
        /// Reuses normal drag/target drafts with an Editor-only local runner.
        /// </summary>
        public void BindAnimationSandbox(AbilitySandboxRunner runner)
        {
            EditorSubmitOverride = () =>
            {
                if (runner.IsBusy || submitting)
                {
                    return;
                }
                if (projection.Current.viewerMustSelectAbilityTargets)
                {
                    if (!pendingAbilitySelectionComplete)
                    {
                        return;
                    }
                    submitting = true;
                    runner.Select(pendingAbilityRequestId, pendingAbilityPrimaryTargets,
                        pendingAbilitySecondaryTargets);
                }
                else if (pendingSummonCard != null)
                {
                    submitting = true;
                    runner.Summon(pendingSummonCardInstanceId);
                }
            };
            EditorCancelOverride = runner.Cancel;
        }

        /// <summary>
        /// Removes pass/turn controls while retaining normal summon selection.
        /// </summary>
        public void RefreshAnimationSandbox(bool busy)
        {
            if (!busy && pendingSummonCard == null
                && projection?.Current?.pendingAbilitySelection == null)
            {
                if (actionButton != null)
                {
                    actionButton.interactable = false;
                }
                if (actionLabel != null)
                {
                    actionLabel.text = "KÉO LÁ\nĐỂ TEST";
                }
            }
        }

        /// <summary>
        /// Clears uncommitted visuals before editing or resetting the fixture.
        /// </summary>
        public void ClearAnimationSandboxDraft()
        {
            submitting = false;
            ClearPendingSelection();
            ClearAbilityTargetDraft();
            SetLocalStatus(string.Empty);
        }
    }
}
#endif
