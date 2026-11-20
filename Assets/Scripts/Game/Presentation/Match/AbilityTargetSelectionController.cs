using System.Collections.Generic;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Presents host-provided legal targets, maintains a local ordered selection draft
    /// and delegates confirm/cancel commands to <see cref="MatchDebugControls"/>.
    /// </summary>
    public sealed class AbilityTargetSelectionController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("References")]
        [SerializeField]
        private MatchBoardPresenter boardPresenter;


        [SerializeField]
        private SkillTargetZoneHighlightController skillTargetGlow;


        [SerializeField]
        private MatchDebugControls actionControls;

        [SerializeField]
        private TMP_Text promptLabel;

        [SerializeField]
        private Button cancelButton;

        [Header("Target Link")]
        [SerializeField]
        private GameObject targetLinkPrefab;

        [Header("Target Colors")]
        [SerializeField]
        private Color validTargetColor = new Color(1f, 0.75f, 0.1f, 0.25f);

        [SerializeField]
        private Color primarySelectedColor = new Color(0.15f, 0.9f, 1f, 0.42f);

        [SerializeField]
        private Color secondarySelectedColor = new Color(1f, 0.2f, 0.55f, 0.42f);

        [Header("Target Emphasis")]
        [Min(0f)] [SerializeField] private float targetScaleBoost = 0.2f;
        [Min(0f)] [SerializeField] private float targetLift = 20f;

        #endregion

        #region Runtime State

        private MatchClientProjection projection;

        private PendingAbilitySelectionDto pendingSelection;

        private readonly List<string> primaryTargets = new List<string>();

        private readonly List<string> secondaryTargets = new List<string>();

        private readonly Dictionary<string, CardDeathFeedback> previewCracks =
            new Dictionary<string, CardDeathFeedback>();

        private AbilityTargetLinkGraphic targetLinks;
        private GameObject targetLinkInstance;
        private string hoveredTargetId;
        private bool linksSuspendedForResolution;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            CreateTargetLinks();
            projection = MatchProjectionRegistry.Current;

            if (projection != null)
            {
                projection.SnapshotChanged += OnSnapshotChanged;

                projection.CommandRejected += OnCommandRejected;
            }

            if (boardPresenter != null)
            {
                boardPresenter.FaceUpViewsRendered += OnFaceUpViewsRendered;
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(CancelSelection);
            }

            ApplySnapshot(projection != null ? projection.Current : null);
        }

        private void OnDisable()
        {
            if (projection != null)
            {
                projection.SnapshotChanged -= OnSnapshotChanged;

                projection.CommandRejected -= OnCommandRejected;
            }

            if (boardPresenter != null)
            {
                boardPresenter.FaceUpViewsRendered -= OnFaceUpViewsRendered;

                boardPresenter.ClearAbilityTargetVisuals();
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(CancelSelection);
            }

            ClearLocalState();

            if (targetLinkInstance != null)
            {
                Destroy(targetLinkInstance);
                targetLinks = null;
                targetLinkInstance = null;
            }
        }

        #endregion

        #region Projection Events

        private void OnSnapshotChanged(MatchSnapshotDto snapshot)
        {
            ApplySnapshot(snapshot);
        }

        private void OnCommandRejected(CommandRejectionReason reason)
        {
            RefreshPresentation();
        }

        private void OnFaceUpViewsRendered()
        {
            previewCracks.Clear();
            RefreshTargetVisuals();
        }

        #endregion

        #region Snapshot Application

        
private void ApplySnapshot(MatchSnapshotDto snapshot)
{
    PendingAbilitySelectionDto next =
        snapshot != null && snapshot.viewerMustSelectAbilityTargets
            ? snapshot.pendingAbilitySelection
            : null;

    if (next == null)
    {
        skillTargetGlow?.HideEffects();
        ClearLocalState();
        return;
    }

    bool changedRequest =
        pendingSelection == null || pendingSelection.requestId != next.requestId;

    pendingSelection = next;

    if (changedRequest)
    {
        primaryTargets.Clear();
        secondaryTargets.Clear();
        hoveredTargetId = null;
        linksSuspendedForResolution = false;
        targetLinks?.SetLinks(null, null, null);
    }

    // Target feedback belongs to individual cards and links, not the board zone.
    skillTargetGlow?.HideEffects();

    SanitizeSelections();
    RefreshPresentation();
}


        
private void ClearLocalState()
{
    skillTargetGlow?.HideEffects();

    pendingSelection = null;

    primaryTargets.Clear();
    secondaryTargets.Clear();
    hoveredTargetId = null;
    linksSuspendedForResolution = false;
    ClearCrackPreviews();
    targetLinks?.SetLinks(null, null, null);

    if (boardPresenter != null)
    {
        boardPresenter.ClearAbilityTargetVisuals();
    }

    if (actionControls != null)
    {
        actionControls.ClearAbilityTargetDraft();
    }

    SetPrompt(string.Empty);
    SetCancelButtonVisible(false);
}


        #endregion

        #region Selection

        private void HandleTargetClicked(string instanceId)
        {
            if (pendingSelection == null || string.IsNullOrWhiteSpace(instanceId))
            {
                return;
            }

            if (RemoveSelectedTarget(instanceId))
            {
                RefreshPresentation();
                return;
            }

            AbilityTargetRequirementDto requirement = FindCurrentRequirement();

            if (
                requirement == null
                || !Contains(requirement.validTargetIds, instanceId)
                || IsAlreadySelected(instanceId)
            )
            {
                return;
            }

            List<string> targets = GetTargets(requirement.slot);

            if (targets.Count >= requirement.count)
            {
                return;
            }

            targets.Add(instanceId);
            RefreshPresentation();
        }

        private void HandleTargetEntered(string instanceId)
        {
            hoveredTargetId = instanceId;
            RefreshTargetEmphasis(instanceId);
            RefreshLinks();
        }

        private void HandleTargetExited(string instanceId)
        {
            if (hoveredTargetId == instanceId)
            {
                hoveredTargetId = null;
                RefreshTargetEmphasis(instanceId);
                RefreshLinks();
            }
        }

        private bool RemoveSelectedTarget(string instanceId)
        {
            bool removedPrimary = primaryTargets.Remove(instanceId);

            bool removedSecondary = secondaryTargets.Remove(instanceId);

            return removedPrimary || removedSecondary;
        }

        private bool IsAlreadySelected(string instanceId)
        {
            return primaryTargets.Contains(instanceId)
                || secondaryTargets.Contains(instanceId);
        }

        #endregion

        #region Rendering

        /// <summary>
        /// Hides draft links as soon as the host confirms an ability. The old snapshot
        /// remains visible during the cast/death animation, so snapshot cleanup alone
        /// would leave beams attached to cards that are already shattering.
        /// </summary>
        public void SuspendLinksForResolution()
        {
            if (pendingSelection == null)
            {
                return;
            }

            linksSuspendedForResolution = true;
            hoveredTargetId = null;
            targetLinks?.SetLinks(null, null, null);
            SetPrompt(string.Empty);
            SetCancelButtonVisible(false);
        }

        private void RefreshPresentation()
        {
            RefreshTargetVisuals();

            bool complete = IsSelectionComplete();

            if (actionControls != null && pendingSelection != null)
            {
                actionControls.SetAbilityTargetDraft(
                    pendingSelection.requestId,
                    primaryTargets.ToArray(),
                    secondaryTargets.ToArray(),
                    complete
                );
            }

            SetPrompt(BuildPrompt());

            SetCancelButtonVisible(
                pendingSelection != null && pendingSelection.canCancel
            );
        }

        private void RefreshTargetVisuals()
        {
            if (boardPresenter == null)
            {
                return;
            }

            // A crack restores the scale it captured when selection began. Release it
            // before restoring normal target visuals, otherwise that old emphasis sticks.
            ClearCrackPreviews();
            boardPresenter.ClearAbilityTargetVisuals();

            if (pendingSelection == null)
            {
                return;
            }

            foreach (string targetId in primaryTargets)
            {
                ConfigureVisual(targetId, primarySelectedColor, true);
            }

            foreach (string targetId in secondaryTargets)
            {
                ConfigureVisual(targetId, secondarySelectedColor, true);
            }

            AbilityTargetRequirementDto current = FindCurrentRequirement();

            if (current?.validTargetIds != null)
            {
                foreach (string targetId in current.validTargetIds)
                {
                    if (!IsAlreadySelected(targetId))
                    {
                        ConfigureVisual(
                            targetId,
                            validTargetColor,
                            targetId == hoveredTargetId
                        );
                    }
                }
            }

            RefreshCrackPreviews();
            RefreshLinks();
        }

        private void ConfigureVisual(string instanceId, Color color, bool emphasized)
        {
            if (boardPresenter.TryGetFaceUpVisual(instanceId, out NetworkCardVisual view))
            {
                view.SetAbilityTargetState(
                    color,
                    HandleTargetClicked,
                    HandleTargetEntered,
                    HandleTargetExited
                );
                view.SetAbilityTargetEmphasis(
                    emphasized,
                    targetScaleBoost,
                    targetLift
                );
            }
        }

        private void RefreshTargetEmphasis(string instanceId)
        {
            if (boardPresenter.TryGetFaceUpVisual(instanceId, out NetworkCardVisual view))
            {
                view.SetAbilityTargetEmphasis(
                    IsAlreadySelected(instanceId) || instanceId == hoveredTargetId,
                    targetScaleBoost,
                    targetLift
                );
            }
        }

        private void CreateTargetLinks()
        {
            Canvas selectionCanvas = boardPresenter?.AbilitySelectionSourceRoot == null
                ? GetComponentInParent<Canvas>()
                : boardPresenter.AbilitySelectionSourceRoot.GetComponentInParent<Canvas>();
            Canvas rootCanvas = selectionCanvas?.rootCanvas;

            if (rootCanvas == null || targetLinkInstance != null)
            {
                return;
            }

            if (targetLinkPrefab != null)
            {
                targetLinkInstance = Instantiate(targetLinkPrefab, rootCanvas.transform, false);
                targetLinks = targetLinkInstance.GetComponentInChildren<AbilityTargetLinkGraphic>();

                if (targetLinks == null)
                {
                    Debug.LogError("Target Link Prefab needs AbilityTargetLinkGraphic on a child.");
                    Destroy(targetLinkInstance);
                    targetLinkInstance = null;
                }
            }

            if (targetLinkInstance == null)
            {
                targetLinkInstance = new GameObject(
                    "AbilityTargetLinks",
                    typeof(RectTransform),
                    typeof(Canvas)
                );
                targetLinkInstance.transform.SetParent(rootCanvas.transform, false);

                var beam = new GameObject(
                    "Beams",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(AbilityTargetLinkGraphic)
                );
                beam.transform.SetParent(targetLinkInstance.transform, false);
                targetLinks = beam.GetComponent<AbilityTargetLinkGraphic>();
            }

            targetLinkInstance.layer = rootCanvas.gameObject.layer;
            targetLinks.gameObject.layer = rootCanvas.gameObject.layer;

            var rootRect = (RectTransform)targetLinkInstance.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var rect = targetLinks.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // Links share the targeting band and sit below the selection controls.
            Canvas linkCanvas = targetLinkInstance.GetComponent<Canvas>();
            if (linkCanvas == null)
            {
                linkCanvas = targetLinkInstance.AddComponent<Canvas>();
            }
            MatchSorting.Apply(linkCanvas, MatchSorting.Targeting);
            targetLinks.raycastTarget = false;
            targetLinks.maskable = false;
        }

        private void RefreshLinks()
        {
            if (pendingSelection != null && targetLinks == null)
            {
                CreateTargetLinks();
            }

            if (
                targetLinks == null
                || pendingSelection == null
                || boardPresenter == null
                || linksSuspendedForResolution
            )
            {
                targetLinks?.SetLinks(null, null, null);
                return;
            }

            RectTransform sourceRect = null;

            if (boardPresenter.TryGetFaceUpVisual(
                pendingSelection.sourceCardInstanceId,
                out NetworkCardVisual source
            ))
            {
                sourceRect = source.CardRect;
            }

            sourceRect ??= boardPresenter.AbilitySelectionSourceRoot;

            if (sourceRect == null)
            {
                targetLinks.SetLinks(null, null, null);
                return;
            }

            var selected = new List<RectTransform>();

            foreach (string id in primaryTargets)
            {
                AddLinkTarget(id, selected);
            }

            foreach (string id in secondaryTargets)
            {
                AddLinkTarget(id, selected);
            }

            RectTransform hovered = null;

            if (
                !string.IsNullOrEmpty(hoveredTargetId)
                && boardPresenter.TryGetFaceUpVisual(
                    hoveredTargetId,
                    out NetworkCardVisual hoveredView
                )
            )
            {
                hovered = hoveredView.CardRect;
            }

            targetLinks.SetLinks(sourceRect, selected, hovered);
        }

        private void AddLinkTarget(string instanceId, List<RectTransform> targets)
        {
            if (boardPresenter.TryGetFaceUpVisual(instanceId, out NetworkCardVisual view))
            {
                targets.Add(view.CardRect);
            }
        }

        private void RefreshCrackPreviews()
        {
            var selectedLethalIds = new HashSet<string>();

            foreach (string id in primaryTargets)
            {
                if (IsLethalSelection(id))
                {
                    selectedLethalIds.Add(id);
                }
            }

            foreach (string id in secondaryTargets)
            {
                if (IsLethalSelection(id))
                {
                    selectedLethalIds.Add(id);
                }
            }

            var removed = new List<string>();

            foreach (KeyValuePair<string, CardDeathFeedback> pair in previewCracks)
            {
                if (selectedLethalIds.Contains(pair.Key) && pair.Value != null)
                {
                    continue;
                }

                if (pair.Value != null)
                {
                    pair.Value.ClearTargetPreviewCrack();
                }
                removed.Add(pair.Key);
            }

            foreach (string id in removed)
            {
                previewCracks.Remove(id);
            }

            foreach (string id in selectedLethalIds)
            {
                if (
                    previewCracks.ContainsKey(id)
                    || !boardPresenter.TryGetFaceUpVisual(id, out NetworkCardVisual view)
                )
                {
                    continue;
                }

                CardDeathFeedback feedback = view.GetComponent<CardDeathFeedback>();

                if (feedback != null)
                {
                    feedback.ShowTargetPreviewCrack(id);
                    previewCracks.Add(id, feedback);
                }
            }
        }

        private bool IsLethalSelection(string instanceId)
        {
            if (pendingSelection?.requirements == null)
            {
                return false;
            }

            foreach (AbilityTargetRequirementDto requirement in pendingSelection.requirements)
            {
                if (Contains(requirement?.lethalTargetIds, instanceId))
                {
                    return true;
                }
            }

            return false;
        }

        private void ClearCrackPreviews()
        {
            foreach (CardDeathFeedback feedback in previewCracks.Values)
            {
                if (feedback != null)
                {
                    feedback.ClearTargetPreviewCrack();
                }
            }

            previewCracks.Clear();
        }

        private string BuildPrompt()
        {
            if (pendingSelection == null)
            {
                return string.Empty;
            }

            if (IsSelectionComplete())
            {
                return $"Kỹ năng {pendingSelection.abilityId}: " + "đã chọn đủ mục tiêu.";
            }

            AbilityTargetRequirementDto current = FindCurrentRequirement();

            if (current == null)
            {
                return $"Kỹ năng {pendingSelection.abilityId}: " + "hãy chọn mục tiêu.";
            }

            List<string> selected = GetTargets(current.slot);

            return $"Kỹ năng {pendingSelection.abilityId}: "
                + $"chọn {GetRelationText(current.relation)} "
                + $"làm {GetSlotText(current.slot)} "
                + $"({selected.Count}/{current.count}).";
        }

        #endregion

        #region Validation

        private void SanitizeSelections()
        {
            SanitizeSlot((int)AbilityTargetSlot.Primary, primaryTargets);

            SanitizeSlot((int)AbilityTargetSlot.Secondary, secondaryTargets);

            for (int i = secondaryTargets.Count - 1; i >= 0; i--)
            {
                if (primaryTargets.Contains(secondaryTargets[i]))
                {
                    secondaryTargets.RemoveAt(i);
                }
            }
        }

        private void SanitizeSlot(int slot, List<string> targets)
        {
            AbilityTargetRequirementDto requirement = FindRequirement(slot);

            if (requirement == null)
            {
                targets.Clear();
                return;
            }

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                if (!Contains(requirement.validTargetIds, targets[i]))
                {
                    targets.RemoveAt(i);
                }
            }

            while (targets.Count > requirement.count)
            {
                targets.RemoveAt(targets.Count - 1);
            }
        }

        private bool IsSelectionComplete()
        {
            if (pendingSelection?.requirements == null)
            {
                return false;
            }

            foreach (
                AbilityTargetRequirementDto requirement in pendingSelection.requirements
            )
            {
                if (
                    requirement == null
                    || GetTargets(requirement.slot).Count != requirement.count
                )
                {
                    return false;
                }
            }

            return pendingSelection.requirements.Length > 0;
        }

        private AbilityTargetRequirementDto FindCurrentRequirement()
        {
            if (pendingSelection?.requirements == null)
            {
                return null;
            }

            foreach (
                AbilityTargetRequirementDto requirement in pendingSelection.requirements
            )
            {
                if (
                    requirement != null
                    && GetTargets(requirement.slot).Count < requirement.count
                )
                {
                    return requirement;
                }
            }

            return null;
        }

        private AbilityTargetRequirementDto FindRequirement(int slot)
        {
            if (pendingSelection?.requirements == null)
            {
                return null;
            }

            foreach (
                AbilityTargetRequirementDto requirement in pendingSelection.requirements
            )
            {
                if (requirement != null && requirement.slot == slot)
                {
                    return requirement;
                }
            }

            return null;
        }

        private List<string> GetTargets(int slot)
        {
            return slot == (int)AbilityTargetSlot.Secondary
                ? secondaryTargets
                : primaryTargets;
        }

        private static bool Contains(string[] values, string expected)
        {
            if (values == null)
            {
                return false;
            }

            foreach (string value in values)
            {
                if (value == expected)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region UI Helpers

        private void CancelSelection()
        {
            if (
                pendingSelection == null
                || !pendingSelection.canCancel
                || actionControls == null
            )
            {
                return;
            }

            if (cancelButton != null)
            {
                cancelButton.interactable = false;
            }

            SetPrompt("Đang hủy chọn kỹ năng...");

            bool sent = actionControls.CancelPendingAbilitySelection(
                pendingSelection.requestId
            );

            if (!sent)
            {
                RefreshPresentation();
            }
        }

        private void SetCancelButtonVisible(bool visible)
        {
            if (cancelButton == null)
            {
                return;
            }

            cancelButton.gameObject.SetActive(visible);
            cancelButton.interactable = visible;
        }

        private void SetPrompt(string message)
        {
            if (promptLabel != null)
            {
                promptLabel.text = message ?? string.Empty;
            }
        }

        private static string GetSlotText(int slot)
        {
            return slot == (int)AbilityTargetSlot.Secondary
                ? "mục tiêu phụ"
                : "mục tiêu chính";
        }

        private static string GetRelationText(int relation)
        {
            switch ((TargetRelation)relation)
            {
                case TargetRelation.Ally:
                    return "một đồng minh";

                case TargetRelation.Enemy:
                    return "một kẻ địch";

                default:
                    return "một lá bài";
            }
        }

        #endregion
    }
}
