using KLTN.Game.Networking;
using TMPro;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    public sealed class MatchHudPresenter : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Dependencies")]
        [SerializeField]
        private MatchDebugControls actionControls;

        [Header("Self - Bottom Side")]
        [SerializeField]
        private TMP_Text selfHealthLabel;

        [SerializeField]
        private TMP_Text selfManaLabel;

        [Header("Opponent - Top Side")]
        [SerializeField]
        private TMP_Text opponentHealthLabel;

        [SerializeField]
        private TMP_Text opponentManaLabel;

        [Header("Formatting")]
        [SerializeField]
        private bool showMaximumMana;

        [SerializeField]
        private string unavailableText = "--";

        #endregion

        #region Runtime State

        private MatchClientProjection projection;
        private int pendingManaCost;

        // A started match keeps showing a disconnected opponent's values while the seat is held.
        private bool matchStarted;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            projection = MatchProjectionRegistry.Current;
            projection.SnapshotChanged += OnSnapshotChanged;

            if (actionControls != null)
            {
                actionControls.PendingManaCostChanged += OnPendingManaCostChanged;
                pendingManaCost = actionControls.PendingManaCost;
            }

            if (projection.Current != null)
            {
                Render(projection.Current);
            }
            else
            {
                RenderWaiting();
            }
        }

        private void OnDisable()
        {
            if (projection != null)
            {
                projection.SnapshotChanged -= OnSnapshotChanged;
            }

            if (actionControls != null)
            {
                actionControls.PendingManaCostChanged -= OnPendingManaCostChanged;
            }

            projection = null;
            pendingManaCost = 0;
            matchStarted = false;
        }

        #endregion

        #region Projection Events

        private void OnSnapshotChanged(MatchSnapshotDto snapshot)
        {
            // A new authoritative snapshot replaces local preview state.
            pendingManaCost = 0;
            Render(snapshot);
        }

        private void OnPendingManaCostChanged(int totalPendingCost)
        {
            pendingManaCost = Mathf.Max(0, totalPendingCost);
            RenderSelfMana(projection?.Current?.self);
        }

        #endregion

        #region Snapshot Rendering

        private void Render(MatchSnapshotDto snapshot)
        {
            if (snapshot == null)
            {
                RenderWaiting();
                return;
            }

            matchStarted = !string.IsNullOrEmpty(snapshot.matchId);
            RenderHealth(snapshot.self, selfHealthLabel);
            RenderSelfMana(snapshot.self);
            RenderHealth(snapshot.opponent, opponentHealthLabel);
            RenderMana(snapshot.opponent, opponentManaLabel, reservedMana: 0);
        }

        private void RenderHealth(PlayerViewDto player, TMP_Text healthLabel)
        {
            if (!IsAvailable(player))
            {
                SetText(healthLabel, unavailableText);
                return;
            }

            SetText(healthLabel, Mathf.Max(0, player.nexusHealth).ToString());
        }

        private void RenderSelfMana(PlayerViewDto self)
        {
            RenderMana(self, selfManaLabel, pendingManaCost);
        }

        private void RenderMana(
            PlayerViewDto player,
            TMP_Text manaLabel,
            int reservedMana
        )
        {
            if (!IsAvailable(player))
            {
                SetText(manaLabel, unavailableText);
                return;
            }

            int displayedMana = Mathf.Max(0, player.mana - reservedMana);

            string manaText = showMaximumMana
                ? $"{displayedMana}/{player.maxMana}"
                : displayedMana.ToString();
            SetText(manaLabel, manaText);
        }

        private void RenderWaiting()
        {
            matchStarted = false;
            SetText(selfHealthLabel, unavailableText);
            SetText(selfManaLabel, unavailableText);
            SetText(opponentHealthLabel, unavailableText);
            SetText(opponentManaLabel, unavailableText);
        }

        #endregion

        #region Helpers

        private bool IsAvailable(PlayerViewDto player)
        {
            return player != null && (player.connected || matchStarted);
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }

        #endregion
    }
}
