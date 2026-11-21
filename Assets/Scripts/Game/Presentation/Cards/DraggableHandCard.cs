using DG.Tweening;
using KLTN.Game.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    [RequireComponent(typeof(NetworkCardVisual))]
    public sealed class DraggableHandCard
        : MonoBehaviour,
            IBeginDragHandler,
            IDragHandler,
            IEndDragHandler
    {
        #region Component References

        private RectTransform rectTransform;
        private CanvasGroup canvasGroup;
        private NetworkCardVisual visual;
        private CardVisualLayout visualLayout;
        private RectTransform dragLayer;

        #endregion

        #region Hand Origin

        private Transform homeParent;
        private int homeSiblingIndex;
        private Vector2 homeAnchorMin;
        private Vector2 homeAnchorMax;
        private Vector2 homePivot;
        private bool homeCaptured;
        private CardVisualLocation homeLocation = CardVisualLocation.Hand;

        #endregion

        #region Pending Placement

        private RectTransform pendingParent;
        private int pendingSiblingIndex = -1;
        private bool isPending;
        private CardVisualLocation pendingLocation = CardVisualLocation.Hand;
        private RectTransform pendingPlaceholder;
        private LayoutElement pendingPlaceholderLayout;
        private Tween pendingPlaceholderWidthTween;

        #endregion

        #region Drag State

        private bool canDrag;
        private bool dragging;
        private bool dropAccepted;
        private RectTransform homePlaceholder;
        private LayoutElement homePlaceholderLayout;
        private Tween placeholderWidthTween;
        private Tween slotExpandTween;
        private Tween returnToHomeTween;

        private const float ReturnToHomeDuration = 0.18f;
        private const float SlotTransitionDuration = 0.22f;

        #endregion

        #region Properties

        public NetworkCardVisual Visual => visual;
        public bool IsPending => isPending;
        public CardVisualLocation HomeLocation => homeLocation;
        public RectTransform PendingParent => pendingParent;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            canvasGroup = GetComponent<CanvasGroup>();
            visual = GetComponent<NetworkCardVisual>();
            visualLayout = GetComponent<CardVisualLayout>();
        }

        private void OnDisable()
        {
            KillActiveTweens();
            DestroyHomePlaceholderImmediate();
            DestroyPendingPlaceholderImmediate();
        }

        private void OnDestroy()
        {
            KillActiveTweens();
            DestroyHomePlaceholderImmediate();
            DestroyPendingPlaceholderImmediate();
        }

        #endregion

        #region Configuration

        public void Configure(
            bool isInteractable,
            RectTransform targetDragLayer,
            CardVisualLocation originLocation
        )
        {
            canDrag = isInteractable;
            dragLayer = targetDragLayer;
            homeLocation = originLocation;

            if (!homeCaptured)
            {
                CaptureHome();
            }

            visual.SetInteractableVisual(isInteractable);
        }

        private void CaptureHome()
        {
            homeParent = transform.parent;
            homeSiblingIndex = transform.GetSiblingIndex();
            homeAnchorMin = rectTransform.anchorMin;
            homeAnchorMax = rectTransform.anchorMax;
            homePivot = rectTransform.pivot;
            homeCaptured = homeParent != null;
        }

        #endregion

        #region Drag Events

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (dragLayer == null || !CanDragNow())
            {
                canDrag = false;
                visual.SetInteractableVisual(false);
                return;
            }

            if (returnToHomeTween != null && returnToHomeTween.IsActive())
            {
                returnToHomeTween.Kill();
                returnToHomeTween = null;
                if (isPending)
                {
                    CompleteDockIntoPendingImmediate();
                }
                else
                {
                    CompleteDockIntoHomeImmediate(false);
                }
            }

            KillSlotExpandTween();

            if (!homeCaptured || (!isPending && transform.parent == homeParent))
            {
                CaptureHome();
            }

            if (!isPending && homeParent != null && transform.parent == homeParent)
            {
                EnsureHomePlaceholderExpanded();
            }
            else if (isPending && pendingParent != null && transform.parent == pendingParent)
            {
                pendingSiblingIndex = transform.GetSiblingIndex();
                if (pendingParent.GetComponent<HorizontalLayoutGroup>() != null)
                {
                    EnsurePendingPlaceholderExpanded();
                }
            }

            dragging = true;
            dropAccepted = false;

            if (homeLocation == CardVisualLocation.Hand)
            {
                // Bật bóng khi bắt đầu kéo bài từ Hand.
                CardShadowVFX shadow = GetComponent<CardShadowVFX>();
                shadow?.UpdateDynamicShadow(0f);
            }
            else if (homeLocation == CardVisualLocation.Reserve)
            {
                CombatZoneHighlightController combatGlow =
                    FindAnyObjectByType<CombatZoneHighlightController>();

                combatGlow?.ShowEffects();

                // Bật bóng khi kéo quái thú từ Reserve.
                CardShadowVFX shadow = GetComponent<CardShadowVFX>();
                shadow?.ShowFixedShadow();
            }

            transform.SetParent(dragLayer, false);
            transform.SetAsLastSibling();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            canvasGroup.blocksRaycasts = false;
            MoveToPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging)
            {
                return;
            }

            MoveToPointer(eventData);

            if (homeLocation == CardVisualLocation.Hand)
            {
                CardShadowVFX shadow = GetComponent<CardShadowVFX>();

                if (shadow != null)
                {
                    float progress = Mathf.Clamp01(
                        Vector2.Distance(
                            eventData.pressPosition,
                            eventData.position
                        ) / 500f
                    );

                    shadow.UpdateDynamicShadow(progress);
                }
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging)
            {
                return;
            }

            canvasGroup.blocksRaycasts = true;
            dragging = false;

            // Tắt bóng khi kết thúc kéo bài từ Hand hoặc Reserve.
            if (
                homeLocation == CardVisualLocation.Hand
                || homeLocation == CardVisualLocation.Reserve
            )
            {
                CardShadowVFX shadow = GetComponent<CardShadowVFX>();
                shadow?.HideShadow();
            }

            if (homeLocation == CardVisualLocation.Hand)
            {
                SummonZoneHighlightController summonGlow =
                    FindAnyObjectByType<SummonZoneHighlightController>();

                summonGlow?.HideEffects();
            }
            else if (homeLocation == CardVisualLocation.Reserve)
            {
                CombatZoneHighlightController combatGlow =
                    FindAnyObjectByType<CombatZoneHighlightController>();

                combatGlow?.HideEffects();
            }

            if (dropAccepted)
            {
                return;
            }

            if (isPending)
            {
                RestorePendingPlacement();
            }
            else
            {
                AnimateReturnToHome();
            }
        }

        #endregion

        #region Pending Placement

        public void PlacePending(RectTransform targetParent, CardVisualLocation location)
        {
            if (!dragging || targetParent == null)
            {
                return;
            }

            SetPendingPlacement(targetParent, location);
        }

        public void MovePending(RectTransform targetParent, CardVisualLocation location)
        {
            if (targetParent == null)
            {
                return;
            }

            SetPendingPlacement(targetParent, location);
        }

        private void SetPendingPlacement(
            RectTransform targetParent,
            CardVisualLocation location
        )
        {
            bool wasAlreadyInTarget = isPending && pendingParent == targetParent;

            KillReturnToHomeTween();
            KillSlotExpandTween();

            int targetSiblingIndex = -1;
            if (pendingPlaceholder != null)
            {
                if (wasAlreadyInTarget)
                {
                    targetSiblingIndex = pendingPlaceholder.GetSiblingIndex();
                    DestroyPendingPlaceholderImmediate();
                }
                else
                {
                    CollapseAndDestroyPendingPlaceholder();
                }
            }

            dropAccepted = true;
            isPending = true;
            pendingParent = targetParent;
            pendingLocation = location;
            canDrag = CanDragNow();

            CollapseHomePlaceholderAnimated();

            ApplyPendingPlacement(targetParent, location, targetSiblingIndex);

            if (
                !wasAlreadyInTarget
                && targetParent != null
                && targetParent.GetComponent<HorizontalLayoutGroup>() != null
                && targetParent.childCount > 1
            )
            {
                AnimateOwnSlotExpansion(targetParent);
            }

            visual.SetPending(true);
            visual.SetInteractableVisual(canDrag);
        }

        private void RestorePendingPlacement()
        {
            if (pendingParent == null)
            {
                RestoreToHome();
                return;
            }

            if (pendingPlaceholder != null)
            {
                AnimateReturnToPending();
                return;
            }

            ApplyPendingPlacement(pendingParent, pendingLocation, pendingSiblingIndex);

            canDrag = CanDragNow();
            visual.SetPending(true);
            visual.SetInteractableVisual(canDrag);
        }

        private void ApplyPendingPlacement(
            RectTransform targetParent,
            CardVisualLocation location,
            int targetSiblingIndex = -1
        )
        {
            transform.SetParent(targetParent, false);
            if (
                targetParent != null
                && targetSiblingIndex >= 0
                && targetSiblingIndex < targetParent.childCount
            )
            {
                transform.SetSiblingIndex(targetSiblingIndex);
            }
            pendingSiblingIndex = transform.GetSiblingIndex();

            visualLayout?.Apply(location);

            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);

            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            rectTransform.pivot = new Vector2(0.5f, 0.5f);

            rectTransform.anchoredPosition = Vector2.zero;

            rectTransform.localScale = Vector3.one;

            rectTransform.localRotation = Quaternion.identity;

            if (targetParent != null && targetParent.GetComponent<HorizontalLayoutGroup>() != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(targetParent);
            }
        }

        public void RestoreToHome()
        {
            if (!homeCaptured || homeParent == null)
            {
                return;
            }

            bool wasPending = isPending;
            RectTransform previousPendingParent = pendingParent;
            int previousPendingSiblingIndex =
                previousPendingParent != null && transform.parent == previousPendingParent
                    ? transform.GetSiblingIndex()
                    : pendingSiblingIndex;
            Vector2 pendingCardSize = rectTransform != null
                ? rectTransform.rect.size
                : new Vector2(150f, 210f);

            KillActiveTweens();

            if (pendingPlaceholder != null)
            {
                CollapseAndDestroyPendingPlaceholder();
            }
            else if (
                wasPending
                && previousPendingParent != null
                && previousPendingParent != homeParent
                && previousPendingParent.GetComponent<HorizontalLayoutGroup>() != null
            )
            {
                SpawnTemporaryCollapsingPlaceholder(
                    previousPendingParent,
                    previousPendingSiblingIndex,
                    pendingCardSize
                );
            }

            CompleteDockIntoHomeImmediate(wasPending);
        }

        private void AnimateReturnToPending()
        {
            if (pendingParent == null || pendingPlaceholder == null)
            {
                CompleteDockIntoPendingImmediate();
                return;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(pendingParent);

            Vector3 targetWorldPos = pendingPlaceholder.TransformPoint(
                pendingPlaceholder.rect.center
            );
            KillReturnToHomeTween();
            canvasGroup.blocksRaycasts = false;

            returnToHomeTween = rectTransform
                .DOMove(targetWorldPos, ReturnToHomeDuration)
                .SetEase(Ease.OutCubic)
                .SetTarget(rectTransform)
                .SetLink(gameObject)
                .OnComplete(CompleteDockIntoPendingImmediate);
        }

        private void CompleteDockIntoPendingImmediate()
        {
            returnToHomeTween = null;

            if (pendingParent == null)
            {
                DestroyPendingPlaceholderImmediate();
                return;
            }

            int targetIndex = pendingPlaceholder != null
                ? pendingPlaceholder.GetSiblingIndex()
                : pendingSiblingIndex;

            DestroyPendingPlaceholderImmediate();

            canvasGroup.blocksRaycasts = true;
            ApplyPendingPlacement(pendingParent, pendingLocation, targetIndex);

            canDrag = CanDragNow();
            visual.SetPending(true);
            visual.SetInteractableVisual(canDrag);
        }

        private void AnimateReturnToHome()
        {
            if (!homeCaptured || homeParent == null || homePlaceholder == null)
            {
                CompleteDockIntoHomeImmediate(false);
                return;
            }

            RectTransform homeRoot = homeParent as RectTransform;
            if (homeRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(homeRoot);
            }

            Vector3 targetWorldPos = homePlaceholder.TransformPoint(homePlaceholder.rect.center);
            KillReturnToHomeTween();
            canvasGroup.blocksRaycasts = false;

            returnToHomeTween = rectTransform
                .DOMove(targetWorldPos, ReturnToHomeDuration)
                .SetEase(Ease.OutCubic)
                .SetTarget(rectTransform)
                .SetLink(gameObject)
                .OnComplete(() => CompleteDockIntoHomeImmediate(false));
        }

        private void CompleteDockIntoHomeImmediate(bool animateSlotExpand)
        {
            returnToHomeTween = null;

            if (!homeCaptured || homeParent == null)
            {
                DestroyHomePlaceholderImmediate();
                return;
            }

            int targetIndex = homePlaceholder != null
                ? homePlaceholder.GetSiblingIndex()
                : homeSiblingIndex;

            DestroyHomePlaceholderImmediate();

            // Prevent OnEndDrag from restoring the previous pending slot.
            dropAccepted = true;
            isPending = false;
            pendingParent = null;
            pendingSiblingIndex = -1;
            pendingLocation = homeLocation;
            canvasGroup.blocksRaycasts = true;

            transform.SetParent(homeParent, false);
            int maxSibling = Mathf.Max(0, homeParent.childCount - 1);
            int clampedIndex = Mathf.Clamp(targetIndex, 0, maxSibling);
            transform.SetSiblingIndex(clampedIndex);
            homeSiblingIndex = clampedIndex;

            visualLayout?.Apply(homeLocation);
            rectTransform.anchorMin = homeAnchorMin;
            rectTransform.anchorMax = homeAnchorMax;
            rectTransform.pivot = homePivot;
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
            canDrag = CanDragNow();
            visual.SetPending(false);
            visual.SetInteractableVisual(canDrag);

            RectTransform homeRoot = homeParent as RectTransform;
            if (homeRoot != null)
            {
                if (
                    animateSlotExpand
                    && homeRoot.GetComponent<HorizontalLayoutGroup>() != null
                    && homeRoot.childCount > 1
                )
                {
                    AnimateOwnSlotExpansion(homeRoot);
                }
                else
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(homeRoot);
                }
            }
        }

        private void EnsureHomePlaceholderExpanded()
        {
            if (homeParent == null)
            {
                return;
            }

            Vector2 cardSize = rectTransform.rect.size;
            if (cardSize.x <= 0f || cardSize.y <= 0f)
            {
                cardSize = new Vector2(150f, 210f);
            }

            if (homePlaceholder == null)
            {
                var placeholderObj = new GameObject(
                    "HandSlotPlaceholder",
                    typeof(RectTransform),
                    typeof(LayoutElement)
                );
                homePlaceholder = placeholderObj.GetComponent<RectTransform>();
                homePlaceholderLayout = placeholderObj.GetComponent<LayoutElement>();
                homePlaceholder.SetParent(homeParent, false);
                homePlaceholder.SetSiblingIndex(homeSiblingIndex);
            }

            KillPlaceholderWidthTween();

            homePlaceholderLayout.ignoreLayout = false;
            homePlaceholderLayout.preferredWidth = cardSize.x;
            homePlaceholderLayout.preferredHeight = cardSize.y;
            homePlaceholderLayout.flexibleWidth = 0f;
            homePlaceholderLayout.flexibleHeight = 0f;

            homePlaceholder.anchorMin = new Vector2(0.5f, 0.5f);
            homePlaceholder.anchorMax = new Vector2(0.5f, 0.5f);
            homePlaceholder.pivot = new Vector2(0.5f, 0.5f);
            homePlaceholder.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, cardSize.x);
            homePlaceholder.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cardSize.y);
        }

        private void CollapseHomePlaceholderAnimated()
        {
            if (
                homePlaceholder == null
                || homePlaceholderLayout == null
                || homePlaceholderLayout.ignoreLayout
            )
            {
                return;
            }

            KillPlaceholderWidthTween();

            RectTransform homeRoot = homeParent as RectTransform;
            float startWidth = homePlaceholderLayout.preferredWidth;
            float targetWidth = GetCollapsedPlaceholderWidth(homeParent);

            if (startWidth <= targetWidth + 0.5f)
            {
                homePlaceholderLayout.ignoreLayout = true;
                if (homeRoot != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(homeRoot);
                }
                return;
            }

            float currentWidth = startWidth;
            placeholderWidthTween = DOTween
                .To(
                    () => currentWidth,
                    w =>
                    {
                        currentWidth = w;
                        if (homePlaceholder == null || homePlaceholderLayout == null)
                        {
                            return;
                        }

                        homePlaceholderLayout.preferredWidth = w;
                        homePlaceholder.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
                        if (homeRoot != null)
                        {
                            LayoutRebuilder.ForceRebuildLayoutImmediate(homeRoot);
                        }
                    },
                    targetWidth,
                    SlotTransitionDuration
                )
                .SetEase(Ease.InOutQuad)
                .SetLink(homePlaceholder.gameObject)
                .OnComplete(() =>
                {
                    placeholderWidthTween = null;
                    if (homePlaceholderLayout != null)
                    {
                        homePlaceholderLayout.ignoreLayout = true;
                    }
                    if (homeRoot != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(homeRoot);
                    }
                });
        }

        private void AnimateOwnSlotExpansion(RectTransform parentRoot)
        {
            if (parentRoot == null || rectTransform == null)
            {
                return;
            }

            LayoutElement ownLayout = GetComponent<LayoutElement>();
            if (ownLayout == null)
            {
                return;
            }

            KillSlotExpandTween();

            float targetWidth = ownLayout.preferredWidth > 0f
                ? ownLayout.preferredWidth
                : rectTransform.rect.width;
            if (targetWidth <= 0f)
            {
                targetWidth = 150f;
            }

            float startWidth = GetCollapsedPlaceholderWidth(parentRoot);
            float currentWidth = startWidth;

            ownLayout.preferredWidth = startWidth;
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, startWidth);
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRoot);

            slotExpandTween = DOTween
                .To(
                    () => currentWidth,
                    w =>
                    {
                        currentWidth = w;
                        if (ownLayout == null || rectTransform == null)
                        {
                            return;
                        }

                        ownLayout.preferredWidth = w;
                        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
                        if (parentRoot != null)
                        {
                            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRoot);
                        }
                    },
                    targetWidth,
                    SlotTransitionDuration
                )
                .SetEase(Ease.OutCubic)
                .SetTarget(rectTransform)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    slotExpandTween = null;
                    if (ownLayout != null)
                    {
                        ownLayout.preferredWidth = targetWidth;
                    }
                    if (rectTransform != null)
                    {
                        rectTransform.SetSizeWithCurrentAnchors(
                            RectTransform.Axis.Horizontal,
                            targetWidth
                        );
                    }
                    if (parentRoot != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(parentRoot);
                    }
                });
        }

        private static void SpawnTemporaryCollapsingPlaceholder(
            RectTransform parentRoot,
            int siblingIndex,
            Vector2 cardSize
        )
        {
            if (parentRoot == null)
            {
                return;
            }

            if (cardSize.x <= 0f || cardSize.y <= 0f)
            {
                cardSize = new Vector2(150f, 210f);
            }

            var placeholderObj = new GameObject(
                "TempCollapsePlaceholder",
                typeof(RectTransform),
                typeof(LayoutElement)
            );
            RectTransform placeholderRect = placeholderObj.GetComponent<RectTransform>();
            LayoutElement layoutElement = placeholderObj.GetComponent<LayoutElement>();

            placeholderRect.SetParent(parentRoot, false);
            if (siblingIndex >= 0 && siblingIndex < parentRoot.childCount)
            {
                placeholderRect.SetSiblingIndex(siblingIndex);
            }

            layoutElement.preferredWidth = cardSize.x;
            layoutElement.preferredHeight = cardSize.y;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            placeholderRect.anchorMin = new Vector2(0.5f, 0.5f);
            placeholderRect.anchorMax = new Vector2(0.5f, 0.5f);
            placeholderRect.pivot = new Vector2(0.5f, 0.5f);
            placeholderRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, cardSize.x);
            placeholderRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cardSize.y);

            float currentWidth = cardSize.x;
            float targetWidth = GetCollapsedPlaceholderWidth(parentRoot);

            DOTween
                .To(
                    () => currentWidth,
                    w =>
                    {
                        currentWidth = w;
                        if (placeholderRect == null || layoutElement == null)
                        {
                            return;
                        }

                        layoutElement.preferredWidth = w;
                        placeholderRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
                        if (parentRoot != null)
                        {
                            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRoot);
                        }
                    },
                    targetWidth,
                    SlotTransitionDuration
                )
                .SetEase(Ease.InOutQuad)
                .SetLink(placeholderObj)
                .OnComplete(() =>
                {
                    if (placeholderObj != null)
                    {
                        placeholderRect.SetParent(null, false);
                        placeholderObj.SetActive(false);
                        Destroy(placeholderObj);
                    }
                    if (parentRoot != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(parentRoot);
                    }
                });
        }

        private static float GetCollapsedPlaceholderWidth(Transform parentTransform)
        {
            if (parentTransform == null)
            {
                return 0f;
            }

            HorizontalLayoutGroup layoutGroup = parentTransform.GetComponent<HorizontalLayoutGroup>();
            if (layoutGroup != null && layoutGroup.spacing < 0f)
            {
                return -layoutGroup.spacing;
            }

            return 0f;
        }

        private void KillActiveTweens()
        {
            KillReturnToHomeTween();
            KillPlaceholderWidthTween();
            KillPendingPlaceholderWidthTween();
            KillSlotExpandTween();
        }

        private void KillReturnToHomeTween()
        {
            if (returnToHomeTween != null)
            {
                returnToHomeTween.Kill();
                returnToHomeTween = null;
            }
        }

        private void KillPlaceholderWidthTween()
        {
            if (placeholderWidthTween != null)
            {
                placeholderWidthTween.Kill();
                placeholderWidthTween = null;
            }
        }

        private void KillPendingPlaceholderWidthTween()
        {
            if (pendingPlaceholderWidthTween != null)
            {
                pendingPlaceholderWidthTween.Kill();
                pendingPlaceholderWidthTween = null;
            }
        }

        private void KillSlotExpandTween()
        {
            if (slotExpandTween != null)
            {
                slotExpandTween.Kill();
                slotExpandTween = null;
                visualLayout?.Apply(isPending ? pendingLocation : homeLocation);
            }
        }

        private void EnsurePendingPlaceholderExpanded()
        {
            if (pendingParent == null)
            {
                return;
            }

            Vector2 cardSize = rectTransform.rect.size;
            if (cardSize.x <= 0f || cardSize.y <= 0f)
            {
                cardSize = new Vector2(150f, 210f);
            }

            if (pendingPlaceholder == null)
            {
                var placeholderObj = new GameObject(
                    "PendingSlotPlaceholder",
                    typeof(RectTransform),
                    typeof(LayoutElement)
                );
                pendingPlaceholder = placeholderObj.GetComponent<RectTransform>();
                pendingPlaceholderLayout = placeholderObj.GetComponent<LayoutElement>();
                pendingPlaceholder.SetParent(pendingParent, false);
                if (
                    pendingSiblingIndex >= 0
                    && pendingSiblingIndex < pendingParent.childCount
                )
                {
                    pendingPlaceholder.SetSiblingIndex(pendingSiblingIndex);
                }
            }

            KillPendingPlaceholderWidthTween();

            pendingPlaceholderLayout.ignoreLayout = false;
            pendingPlaceholderLayout.preferredWidth = cardSize.x;
            pendingPlaceholderLayout.preferredHeight = cardSize.y;
            pendingPlaceholderLayout.flexibleWidth = 0f;
            pendingPlaceholderLayout.flexibleHeight = 0f;

            pendingPlaceholder.anchorMin = new Vector2(0.5f, 0.5f);
            pendingPlaceholder.anchorMax = new Vector2(0.5f, 0.5f);
            pendingPlaceholder.pivot = new Vector2(0.5f, 0.5f);
            pendingPlaceholder.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                cardSize.x
            );
            pendingPlaceholder.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                cardSize.y
            );
        }

        private void CollapseAndDestroyPendingPlaceholder()
        {
            if (pendingPlaceholder == null || pendingPlaceholderLayout == null)
            {
                DestroyPendingPlaceholderImmediate();
                return;
            }

            KillPendingPlaceholderWidthTween();

            RectTransform capturedPlaceholder = pendingPlaceholder;
            LayoutElement capturedLayout = pendingPlaceholderLayout;
            RectTransform capturedParent = capturedPlaceholder.parent as RectTransform;
            GameObject placeholderObj = capturedPlaceholder.gameObject;

            pendingPlaceholder = null;
            pendingPlaceholderLayout = null;

            float startWidth = capturedLayout.preferredWidth > 0f
                ? capturedLayout.preferredWidth
                : capturedPlaceholder.rect.width;
            float targetWidth = GetCollapsedPlaceholderWidth(capturedParent);

            if (startWidth <= targetWidth + 0.5f)
            {
                capturedPlaceholder.SetParent(null, false);
                placeholderObj.SetActive(false);
                Destroy(placeholderObj);
                if (capturedParent != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(capturedParent);
                }
                return;
            }

            float currentWidth = startWidth;
            DOTween
                .To(
                    () => currentWidth,
                    w =>
                    {
                        currentWidth = w;
                        if (capturedPlaceholder == null || capturedLayout == null)
                        {
                            return;
                        }

                        capturedLayout.preferredWidth = w;
                        capturedPlaceholder.SetSizeWithCurrentAnchors(
                            RectTransform.Axis.Horizontal,
                            w
                        );
                        if (capturedParent != null)
                        {
                            LayoutRebuilder.ForceRebuildLayoutImmediate(capturedParent);
                        }
                    },
                    targetWidth,
                    SlotTransitionDuration
                )
                .SetEase(Ease.InOutQuad)
                .SetLink(placeholderObj)
                .OnComplete(() =>
                {
                    if (placeholderObj != null)
                    {
                        capturedPlaceholder.SetParent(null, false);
                        placeholderObj.SetActive(false);
                        Destroy(placeholderObj);
                    }
                    if (capturedParent != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(capturedParent);
                    }
                });
        }

        private void DestroyPendingPlaceholderImmediate()
        {
            KillPendingPlaceholderWidthTween();

            if (pendingPlaceholder != null)
            {
                GameObject obj = pendingPlaceholder.gameObject;
                pendingPlaceholder.SetParent(null, false);
                obj.SetActive(false);
                Destroy(obj);
                pendingPlaceholder = null;
                pendingPlaceholderLayout = null;
            }
        }

        private void DestroyHomePlaceholderImmediate()
        {
            KillPlaceholderWidthTween();

            if (homePlaceholder != null)
            {
                GameObject obj = homePlaceholder.gameObject;
                homePlaceholder.SetParent(null, false);
                obj.SetActive(false);
                Destroy(obj);
                homePlaceholder = null;
                homePlaceholderLayout = null;
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Rechecks the latest viewer-filtered snapshot before dragging. A temporary
        /// animation view or a stale local draft must never grant gameplay authority.
        /// </summary>
        private bool CanDragNow()
        {
            if (!canDrag || visual == null || string.IsNullOrEmpty(visual.InstanceId))
            {
                return false;
            }

            MatchSnapshotDto snapshot = MatchProjectionRegistry.Current.Current;

            if (snapshot?.self == null)
            {
                return false;
            }

            CardViewDto[] cards;

            if (homeLocation == CardVisualLocation.Hand)
            {
                if (!snapshot.viewerCanAct)
                {
                    return false;
                }

                cards = snapshot.self.hand;
            }
            else if (homeLocation == CardVisualLocation.Reserve)
            {
                if (!snapshot.viewerCanDeclareAttack && !snapshot.viewerCanDeclareBlock)
                {
                    return false;
                }

                cards = snapshot.self.reserve;
            }
            else
            {
                return false;
            }

            if (cards == null)
            {
                return false;
            }

            foreach (CardViewDto card in cards)
            {
                if (card != null && card.instanceId == visual.InstanceId)
                {
                    return true;
                }
            }

            return false;
        }

        private void MoveToPointer(PointerEventData eventData)
        {
            if (
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    dragLayer,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 localPosition
                )
            )
            {
                rectTransform.anchoredPosition = localPosition;
            }
        }

        #endregion
    }
}
