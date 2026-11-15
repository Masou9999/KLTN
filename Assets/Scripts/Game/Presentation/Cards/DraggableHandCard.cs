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
        private bool isPending;
        private CardVisualLocation pendingLocation = CardVisualLocation.Hand;

        #endregion

        #region Drag State

        private bool canDrag;
        private bool dragging;
        private bool dropAccepted;

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

            if (!homeCaptured)
            {
                CaptureHome();
            }

            dragging = true;
            dropAccepted = false;
            

if (homeLocation == CardVisualLocation.Hand)
{
    SummonZoneHighlightController summonGlow =
        FindFirstObjectByType<SummonZoneHighlightController>();

    summonGlow?.ShowEffects();

    // Bật bóng khi bắt đầu kéo bài từ Hand.
    CardShadowVFX shadow = GetComponent<CardShadowVFX>();
    shadow?.UpdateDynamicShadow(0f);
}


else if (homeLocation == CardVisualLocation.Reserve)
{
    CombatZoneHighlightController combatGlow =
        FindFirstObjectByType<CombatZoneHighlightController>();

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

// Tắt bóng khi kết thúc kéo bài từ Reserve.

// Tắt bóng khi kết thúc kéo bài từ Hand hoặc Reserve.
if (homeLocation == CardVisualLocation.Hand ||
    homeLocation == CardVisualLocation.Reserve)
{
    CardShadowVFX shadow = GetComponent<CardShadowVFX>();
    shadow?.HideShadow();
}


            
if (homeLocation == CardVisualLocation.Hand)
{
    SummonZoneHighlightController summonGlow =
        FindFirstObjectByType<SummonZoneHighlightController>();

    summonGlow?.HideEffects();
}
else if (homeLocation == CardVisualLocation.Reserve)
{
    CombatZoneHighlightController combatGlow =
        FindFirstObjectByType<CombatZoneHighlightController>();

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
                RestoreToHome();
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
            dropAccepted = true;
            isPending = true;
            pendingParent = targetParent;
            pendingLocation = location;
            canDrag = CanDragNow();

            ApplyPendingPlacement(targetParent, location);

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

            ApplyPendingPlacement(pendingParent, pendingLocation);

            canDrag = CanDragNow();
            visual.SetPending(true);
            visual.SetInteractableVisual(canDrag);
        }

        private void ApplyPendingPlacement(
            RectTransform targetParent,
            CardVisualLocation location
        )
        {
            transform.SetParent(targetParent, false);

            visualLayout?.Apply(location);

            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);

            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            rectTransform.pivot = new Vector2(0.5f, 0.5f);

            rectTransform.anchoredPosition = Vector2.zero;

            rectTransform.localScale = Vector3.one;

            rectTransform.localRotation = Quaternion.identity;
        }

        public void RestoreToHome()
        {
            if (!homeCaptured || homeParent == null)
            {
                return;
            }

            // Prevent OnEndDrag from restoring the previous pending slot.
            dropAccepted = true;
            isPending = false;
            pendingParent = null;
            pendingLocation = homeLocation;
            transform.SetParent(homeParent, false);
            transform.SetSiblingIndex(homeSiblingIndex);
            visualLayout?.Apply(homeLocation);
            rectTransform.anchorMin = homeAnchorMin;
            rectTransform.anchorMax = homeAnchorMax;
            rectTransform.pivot = homePivot;
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
            canDrag = CanDragNow();
            visual.SetPending(false);
            visual.SetInteractableVisual(canDrag);

            if (homeParent is RectTransform homeRoot)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(homeRoot);
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
