using System;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Presenter for local action drafts and the context-sensitive action button. It
    /// stages visual intent only; the host remains responsible for rule validation.
    /// </summary>
    public sealed partial class MatchDebugControls : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField]
        private NetworkMatchBridge bridge;

        [SerializeField]
        private Button actionButton;

        [SerializeField]
        private TMP_Text actionLabel;

        [SerializeField]
        private TMP_Text localStatusLabel;

        #endregion

        #region Runtime State

        private MatchClientProjection projection;
        private DraggableHandCard pendingSummonCard;
        private string pendingSummonCardInstanceId;

        private readonly DraggableHandCard[] pendingBoardCards = new DraggableHandCard[
            MatchState.BoardSlotCount
        ];
        private int pendingManaCost;
        private bool submitting;
        private string pendingAbilityRequestId;

        private string[] pendingAbilityPrimaryTargets = Array.Empty<string>();

        private string[] pendingAbilitySecondaryTargets = Array.Empty<string>();

        private bool pendingAbilitySelectionComplete;

        #endregion

        private bool HasPendingBoardDraft
        {
            get
            {
                for (int i = 0; i < pendingBoardCards.Length; i++)
                {
                    if (pendingBoardCards[i] != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        #region Public State

        public int PendingManaCost => pendingManaCost;
        public event Action<int> PendingManaCostChanged;

#if UNITY_EDITOR
        // Optional local harness; never compiled into a player build.
        public Action EditorSubmitOverride { get; set; }
        public Func<string, bool> EditorCancelOverride { get; set; }
#endif

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            projection = MatchProjectionRegistry.Current;
            projection.SnapshotChanged += OnSnapshotChanged;
            projection.CommandRejected += OnCommandRejected;

            if (actionButton != null)
            {
                actionButton.onClick.AddListener(CommitOrPass);
            }

            SetLocalStatus(string.Empty);
            SetPendingManaCost(0);
            RefreshControls();
        }

        private void OnDisable()
        {
            if (projection != null)
            {
                projection.SnapshotChanged -= OnSnapshotChanged;
                projection.CommandRejected -= OnCommandRejected;
            }

            if (actionButton != null)
            {
                actionButton.onClick.RemoveListener(CommitOrPass);
            }
        }

        #endregion
    }
}
