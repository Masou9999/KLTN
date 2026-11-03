#if UNITY_EDITOR
using System;
using System.Linq;
using CMCMProductions;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Sandbox
{
    /// <summary>
    /// Feeds the normal presentation with local, rules-generated updates.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class AbilitySandboxRunner : MonoBehaviour
    {
        #region Scene References

        public MatchDebugControls controls;
        public MatchBoardPresenter board;
        public CombatAnimationDirector director;
        public AbilitySandboxModal modal;
        public Button addButton;
        public Button resetButton;
        public GameObject settingsPanel;

        #endregion

        #region Runtime State

        public AbilitySandboxModel Model { get; private set; }
        private ulong publishedRevision;
        private CombatVfxPresenter particles;
        public bool IsBusy => Model == null
            || MatchProjectionRegistry.Current.Current?.revision != publishedRevision;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            MatchUpdateInboxRegistry.Current.Reset();
            MatchProjectionRegistry.Current.Reset();
            Model = new AbilitySandboxModel(Resources.LoadAll<Card>("Cards")
                .Select(CardDefinitionMapper.Create).ToDictionary(card => card.Id));
            particles = FindAnyObjectByType<CombatVfxPresenter>();
            controls.BindAnimationSandbox(this);
            addButton.onClick.AddListener(OpenModal);
            resetButton.onClick.AddListener(ResetScene);
        }

        private void Start()
        {
            Publish();
        }

        private void LateUpdate()
        {
            bool modalOpen = modal.gameObject.activeSelf;
            addButton.interactable = !IsBusy && !modalOpen
                && Model.State.PendingSelection == null
                && (settingsPanel == null || !settingsPanel.activeSelf);
            resetButton.interactable = !modalOpen
                && (settingsPanel == null || !settingsPanel.activeSelf);
            controls.RefreshAnimationSandbox(IsBusy);
        }

        private void OnDestroy()
        {
            if (controls != null)
            {
                controls.EditorSubmitOverride = null;
                controls.EditorCancelOverride = null;
            }
            if (addButton != null)
            {
                addButton.onClick.RemoveListener(OpenModal);
            }
            if (resetButton != null)
            {
                resetButton.onClick.RemoveListener(ResetScene);
            }
            MatchUpdateInboxRegistry.Current.Reset();
            MatchProjectionRegistry.Current.Reset();
        }

        #endregion

        #region Local Commands

        public void Summon(string cardId)
        {
            if (IsBusy || !ulong.TryParse(cardId, out ulong id))
            {
                return;
            }
            Complete(Model.Summon(id));
        }

        public void Select(string request, string[] primary, string[] secondary)
        {
            if (IsBusy || !ulong.TryParse(request, out ulong id))
            {
                return;
            }
            Complete(Model.Select(id, primary.Select(ulong.Parse).ToArray(),
                secondary.Select(ulong.Parse).ToArray()));
        }

        public bool Cancel(string request)
        {
            if (IsBusy || !ulong.TryParse(request, out ulong id))
            {
                return false;
            }
            Complete(Model.Cancel(id));
            return true;
        }

        private void Complete(CommandResult result)
        {
            if (!result.Accepted)
            {
                MatchProjectionRegistry.Current.Reject(result.RejectionReason);
                return;
            }
            Publish();
        }

        private void Publish()
        {
            MatchUpdateDto update = Model.BuildUpdate();
            publishedRevision = update.snapshot.revision;
            MatchUpdateInboxRegistry.Current.Enqueue(update);
        }

        #endregion

        #region Fixture UI

        public void OpenModal()
        {
            if (IsBusy || Model.State.PendingSelection != null)
            {
                return;
            }
            controls.ClearAnimationSandboxDraft();
            modal.Open(this);
        }

        public bool ApplyCounts(int[] reserve, int[] hand)
        {
            if (IsBusy || !Model.ApplyCounts(reserve, hand))
            {
                return false;
            }
            Publish();
            return true;
        }

        /// <summary>
        /// Stops animation before resetting registries, so an old final snapshot cannot
        /// overwrite the new fixture. Rebuilds views including death and revive feedback.
        /// </summary>
        public void ResetScene()
        {
            modal.gameObject.SetActive(false);
            director.enabled = false;
            if (particles != null && particles.enabled)
            {
                // OnDisable returns pooled hits/dust and stops their lifetime coroutines.
                particles.enabled = false;
                particles.enabled = true;
            }
            controls.ClearAnimationSandboxDraft();
            board.enabled = false;
            MatchUpdateInboxRegistry.Current.Reset();
            MatchProjectionRegistry.Current.Reset();
            Model.Reset();
            board.enabled = true;
            director.enabled = true;
            Publish();
        }

        #endregion
    }
}
#endif
