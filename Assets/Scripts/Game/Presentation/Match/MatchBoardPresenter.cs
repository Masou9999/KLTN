using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Rebuilds local card views from filtered snapshots and temporarily preserves combat
    /// participants so presentation can finish after the domain has changed zones.
    /// </summary>
    public sealed class MatchBoardPresenter : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField]
        private NetworkCardVisual cardPrefab;

        [SerializeField]
        private CardPresentationCatalog presentationCatalog;

        [SerializeField]
        private Sprite cardBack;

        [Header("Hands")]
        [SerializeField]
        private RectTransform selfHandRoot;

        [SerializeField]
        private RectTransform opponentHandRoot;

        [Header("Ability Selection")]
        [SerializeField]
        private RectTransform abilitySelectionSourceRoot;

        [Header("Reserves")]
        [SerializeField]
        private RectTransform selfReserveRoot;

        [SerializeField]
        private RectTransform opponentReserveRoot;

        [Header("Board slots: element 0 = slot 1")]
        [SerializeField]
        private RectTransform[] selfBoardSlots = new RectTransform[3];

        [SerializeField]
        private RectTransform[] opponentBoardSlots = new RectTransform[3];

        [Header("Drag")]
        [SerializeField]
        private RectTransform dragLayer;

        [Header("Mulligan")]
        [SerializeField]
        public MulliganPresenter mulliganPresenter;

        [Header("Card Draw Animation")]
        [Tooltip("Kéo Transform vị trí Deck (giống Deck Spawn Point của Mulligan) vào đây")]
        [SerializeField]
        private Transform selfDeckSpawnPoint;

        [Tooltip("Thứ tự 0–99 trong lớp GameCardMotion khi lá bài đang bay rút bài")]
        [SerializeField]
        private int drawSortingOrder = 50;

        [Tooltip("Vị trí hiển thị lá bài giữa màn hình khi rút (Nếu để trống sẽ tự lấy tâm màn hình)")]
        [SerializeField]
        private RectTransform drawShowcasePoint;

        [Tooltip("Độ trễ trước khi rút bài lúc chuyển sang vòng mới (để chữ Vòng X hiện xong trước)")]
        [SerializeField]
        private float drawStartDelayOnNewRound = 0.75f;

        [SerializeField]
        private float drawFlyToCenterDuration = 0.45f;

        [SerializeField]
        private float drawShowcaseDuration = 0.6f;

        [SerializeField]
        private float drawFlyToHandDuration = 0.4f;

        [SerializeField]
        private float drawShowcaseScale = 1.0f;

        [SerializeField]
        private float drawHandScale = 0.45f;

        [Tooltip("Khoảng cách dàn ngang giữa các lá bài khi rút từ 2 lá trở lên cùng lúc")]
        [SerializeField]
        private float drawShowcaseSpacing = 250f;

        [Header("Ability Selection Movement")]
        [SerializeField]
        private float abilityFlyToCenterDuration = 0.38f;

        [SerializeField]
        private float abilityReturnToReserveDuration = 0.32f;

        [SerializeField]
        private float abilityReturnToHandDuration = 0.32f;

        [Header("Combat Return Movement")]
        [SerializeField]
        private float combatReturnToReserveDuration = 0.35f;

        [SerializeField]
        private float combatReturnToReserveStagger = 0.06f;

        #endregion

        #region Runtime State

        private readonly List<GameObject> spawnedViews = new List<GameObject>();
        private MatchClientProjection projection;

        private readonly List<GameObject> opponentHandBacks = new List<GameObject>();

        private readonly Dictionary<string, NetworkCardVisual> faceUpViewsById =
            new Dictionary<string, NetworkCardVisual>();

        private readonly HashSet<string> knownHandCardIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> animatingDrawCardIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> pendingDrawSlotIgnoreIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> previousReserveVisualIds = new HashSet<string>(StringComparer.Ordinal);
        private bool hasPreviousReserveSnapshot;
        private readonly Dictionary<string, (float alpha, bool blocksRaycasts)> savedDrawCardCanvasStates =
            new Dictionary<string, (float alpha, bool blocksRaycasts)>(StringComparer.Ordinal);
        private readonly List<GameObject> activeDrawAnimationObjects = new List<GameObject>();

        private int lastSeenRoundNumber = -1;

        private string lastAnimatedSelectionRequestId;
        private Vector3 capturedSelectionStartWorldPos;
        private bool hasCapturedSelectionStartPos;
        private bool shouldAnimateCurrentSelectionSource;
        private NetworkCardVisual currentSelectionSourceView;
        private string activeSelectionSourceCardId;
        private CardViewDto pendingCancelledSelectionCard;
        private Vector3 cancelledSelectionStartWorldPos;
        private Vector3 cancelledSelectionStartScale = Vector3.one;

        #endregion

        #region Events

        public event Action FaceUpViewsRendered;

        public RectTransform AbilitySelectionSourceRoot => abilitySelectionSourceRoot;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            projection = MatchProjectionRegistry.Current;
            projection.SnapshotChanged += Render;

            if (projection.Current != null)
            {
                Render(projection.Current);
            }
        }

        private void OnDisable()
        {
            if (projection != null)
            {
                projection.SnapshotChanged -= Render;
            }

            ClearDrawAnimations();
            ClearViews();
        }

        #endregion

        #region Snapshot Rendering

        private void Render(MatchSnapshotDto snapshot)
        {
            CaptureSelectionSourceStartState(snapshot);
            CaptureCurrentReserveVisualIds(snapshot);
            ClearViews();

            if (
                snapshot == null
                || snapshot.self == null
                || snapshot.opponent == null
                || cardPrefab == null
            )
            {
                FaceUpViewsRendered?.Invoke();
                return;
            }

            List<CardViewDto> newlyDrawnCards = DetectNewlyDrawnCards(snapshot, out bool isNewRound);

            RenderSelfHand(snapshot);

            if (mulliganPresenter != null)
            {
                mulliganPresenter.UpdateMulliganState(snapshot);
            }

            if (newlyDrawnCards != null && newlyDrawnCards.Count > 0)
            {
                StartCoroutine(AnimateCardDrawBatchRoutine(newlyDrawnCards, isNewRound));
            }

            RenderOpponentHand(snapshot);
            RenderReserve(
                snapshot.self.reserve,
                selfReserveRoot,
                canDrag: snapshot.viewerCanDeclareAttack || snapshot.viewerCanDeclareBlock,
                isOpponent: false
            );
            TriggerPendingSelectionMoveToCenter();
            TriggerPendingSelectionReturnToHand();

            RenderReserve(snapshot.opponent.reserve, opponentReserveRoot, false, true);
            RenderBoard(snapshot.self.board, selfBoardSlots, false);
            RenderBoard(snapshot.opponent.board, opponentBoardSlots, true);
            FaceUpViewsRendered?.Invoke();
        }

        private void RenderSelfHand(MatchSnapshotDto snapshot)
        {
            if (snapshot.self.hand == null || selfHandRoot == null)
            {
                return;
            }

            bool hasActiveRosterSpace =
                snapshot.self.activeRosterCount < MatchState.MaximumActiveRosterSize;

            foreach (CardViewDto dto in snapshot.self.hand)
            {
                bool isSelectionSource = IsPendingAbilitySelectionSource(snapshot, dto);

                RectTransform parent =
                    isSelectionSource && abilitySelectionSourceRoot != null
                        ? abilitySelectionSourceRoot
                        : selfHandRoot;

                CardVisualLocation location = isSelectionSource
                    ? CardVisualLocation.AbilitySelection
                    : CardVisualLocation.Hand;

                bool canDrag =
                    snapshot.roundNumber > 0
                    && !isSelectionSource
                    && snapshot.viewerCanAct
                    && hasActiveRosterSpace
                    && dto.energy <= snapshot.self.mana;

                NetworkCardVisual view = CreateFaceUpCard(dto, parent, canDrag, location);

                if (isSelectionSource)
                {
                    CenterAbilitySelectionSource(view);
                    view.GetComponent<CardShadowVFX>()?.ShowFixedShadow();
                    currentSelectionSourceView = view;
                    activeSelectionSourceCardId = dto.instanceId;
                }
                else if (
                    !string.IsNullOrWhiteSpace(dto.instanceId)
                    && animatingDrawCardIds.Contains(dto.instanceId)
                )
                {
                    HideRealHandCardForDraw(dto.instanceId, view);
                }
            }
        }

        #endregion

        #region Card Draw Animation

        private List<CardViewDto> DetectNewlyDrawnCards(MatchSnapshotDto snapshot, out bool isNewRound)
        {
            isNewRound = snapshot.roundNumber > lastSeenRoundNumber;

            if (snapshot.roundNumber == 0)
            {
                lastSeenRoundNumber = 0;
                knownHandCardIds.Clear();
                animatingDrawCardIds.Clear();
                pendingDrawSlotIgnoreIds.Clear();
                savedDrawCardCanvasStates.Clear();

                if (snapshot.self?.hand != null)
                {
                    foreach (CardViewDto dto in snapshot.self.hand)
                    {
                        if (!string.IsNullOrWhiteSpace(dto.instanceId))
                        {
                            knownHandCardIds.Add(dto.instanceId);
                        }
                    }
                }
                return null;
            }

            if (lastSeenRoundNumber <= 0)
            {
                lastSeenRoundNumber = snapshot.roundNumber;
                if (snapshot.self?.hand != null)
                {
                    foreach (CardViewDto dto in snapshot.self.hand)
                    {
                        if (!string.IsNullOrWhiteSpace(dto.instanceId))
                        {
                            knownHandCardIds.Add(dto.instanceId);
                        }
                    }
                }
                return null;
            }

            lastSeenRoundNumber = snapshot.roundNumber;

            if (snapshot.self?.hand == null || snapshot.self.hand.Length == 0)
            {
                return null;
            }

            List<CardViewDto> drawnCards = null;

            foreach (CardViewDto dto in snapshot.self.hand)
            {
                if (string.IsNullOrWhiteSpace(dto.instanceId))
                {
                    continue;
                }

                if (knownHandCardIds.Add(dto.instanceId))
                {
                    animatingDrawCardIds.Add(dto.instanceId);
                    pendingDrawSlotIgnoreIds.Add(dto.instanceId);
                    if (drawnCards == null)
                    {
                        drawnCards = new List<CardViewDto>();
                    }
                    drawnCards.Add(dto);
                }
            }

            return drawnCards;
        }

        private void HideRealHandCardForDraw(string instanceId, NetworkCardVisual view)
        {
            if (view == null) return;

            CanvasGroup cardGroup = view.GetComponent<CanvasGroup>();
            if (cardGroup == null)
            {
                cardGroup = view.gameObject.AddComponent<CanvasGroup>();
            }

            savedDrawCardCanvasStates[instanceId] = (cardGroup.alpha, cardGroup.blocksRaycasts);
            cardGroup.alpha = 0f;
            cardGroup.blocksRaycasts = false;

            if (!string.IsNullOrWhiteSpace(instanceId) && pendingDrawSlotIgnoreIds.Contains(instanceId))
            {
                LayoutElement layoutElement = view.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.ignoreLayout = true;
                }
            }
        }

        private void RevealRealHandCardAfterDraw(string instanceId)
        {
            pendingDrawSlotIgnoreIds.Remove(instanceId);

            NetworkCardVisual realHandView;
            if (TryGetFaceUpVisual(instanceId, out realHandView) && realHandView != null)
            {
                LayoutElement layoutElement = realHandView.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.ignoreLayout = false;
                }

                CardVisualLayout visualLayout = realHandView.GetComponent<CardVisualLayout>();
                if (visualLayout != null)
                {
                    visualLayout.Apply(CardVisualLocation.Hand);
                }

                CanvasGroup cardGroup = realHandView.GetComponent<CanvasGroup>();
                if (cardGroup != null)
                {
                    if (savedDrawCardCanvasStates.TryGetValue(instanceId, out var savedState))
                    {
                        cardGroup.alpha = savedState.alpha;
                        cardGroup.blocksRaycasts = savedState.blocksRaycasts;
                    }
                    else
                    {
                        cardGroup.alpha = 1f;
                        cardGroup.blocksRaycasts = true;
                    }
                }
            }

            savedDrawCardCanvasStates.Remove(instanceId);
        }

        private IEnumerator AnimateCardDrawBatchRoutine(List<CardViewDto> drawnCards, bool isNewRound)
        {
            if (drawnCards == null || drawnCards.Count == 0)
            {
                yield break;
            }

            float initialDelay = isNewRound ? drawStartDelayOnNewRound : 0.25f;
            if (initialDelay > 0f)
            {
                yield return new WaitForSeconds(initialDelay);
            }

            RectTransform animationParent = dragLayer != null
                ? dragLayer
                : selfHandRoot != null
                    ? selfHandRoot.root as RectTransform
                    : transform as RectTransform;

            if (animationParent == null || cardPrefab == null)
            {
                foreach (CardViewDto dto in drawnCards)
                {
                    animatingDrawCardIds.Remove(dto.instanceId);
                    RevealRealHandCardAfterDraw(dto.instanceId);
                }
                yield break;
            }

            StartHandSlotExpansionForDraw(drawnCards);

            Vector3 startWorldPos = selfDeckSpawnPoint != null
                ? selfDeckSpawnPoint.position
                : selfHandRoot != null
                    ? selfHandRoot.position
                    : animationParent.position;

            Vector3 centerWorldPos = drawShowcasePoint != null
                ? drawShowcasePoint.position
                : abilitySelectionSourceRoot != null
                    ? abilitySelectionSourceRoot.position
                    : animationParent.position;

            Vector3 baseCenterLocalPos = animationParent.InverseTransformPoint(centerWorldPos);
            Vector3 startLocalPos = animationParent.InverseTransformPoint(startWorldPos);

            int count = drawnCards.Count;
            List<NetworkCardVisual> tempCards = new List<NetworkCardVisual>(count);
            float maxFlyToCenterTime = 0f;

            for (int i = 0; i < count; i++)
            {
                CardViewDto cardDto = drawnCards[i];
                NetworkCardVisual tempCard = Instantiate(cardPrefab, animationParent);
                activeDrawAnimationObjects.Add(tempCard.gameObject);
                tempCards.Add(tempCard);

                Canvas tempCanvas = tempCard.GetComponent<Canvas>();
                if (tempCanvas == null)
                {
                    tempCanvas = tempCard.gameObject.AddComponent<Canvas>();
                }
                MatchSorting.Apply(tempCanvas, MatchSorting.CardMotion, drawSortingOrder + i);

                CardVisualLayout layout = tempCard.GetComponent<CardVisualLayout>();
                if (layout != null)
                {
                    layout.Apply(CardVisualLocation.Mulligan);
                }

                tempCard.BindFaceUp(cardDto, FindArtwork(cardDto.definitionId));
                tempCard.SetOwnerPerspective(false);

                DraggableHandCard dragScript = tempCard.GetComponent<DraggableHandCard>();
                if (dragScript != null)
                {
                    Destroy(dragScript);
                }

                CanvasGroup tempGroup = tempCard.GetComponent<CanvasGroup>();
                if (tempGroup == null)
                {
                    tempGroup = tempCard.gameObject.AddComponent<CanvasGroup>();
                }
                tempGroup.alpha = 1f;
                tempGroup.blocksRaycasts = false;

                RectTransform tempRect = tempCard.GetComponent<RectTransform>();
                tempRect.localPosition = startLocalPos;
                tempRect.localScale = Vector3.zero;
                tempRect.localEulerAngles = new Vector3(0f, 90f, 0f);

                float offsetIndex = i - (count - 1) * 0.5f;
                Vector3 targetShowcaseLocalPos = baseCenterLocalPos + new Vector3(offsetIndex * drawShowcaseSpacing, 0f, 0f);

                float staggerDelay = i * 0.18f;
                Sequence toCenterSeq = DOTween.Sequence();
                toCenterSeq.Insert(staggerDelay, tempRect.DOLocalMove(targetShowcaseLocalPos, drawFlyToCenterDuration).SetEase(Ease.OutCubic));
                toCenterSeq.Insert(staggerDelay, tempRect.DOScale(Vector3.one * drawShowcaseScale, drawFlyToCenterDuration).SetEase(Ease.OutBack));
                toCenterSeq.Insert(staggerDelay, tempRect.DOLocalRotate(Vector3.zero, drawFlyToCenterDuration).SetEase(Ease.OutBack));

                maxFlyToCenterTime = Mathf.Max(maxFlyToCenterTime, staggerDelay + drawFlyToCenterDuration);
            }

            yield return new WaitForSeconds(maxFlyToCenterTime);

            yield return new WaitForSeconds(drawShowcaseDuration);

            yield return new WaitForEndOfFrame();

            float maxFlyToHandTime = 0f;

            for (int i = 0; i < count; i++)
            {
                NetworkCardVisual tempCard = tempCards[i];
                if (tempCard == null) continue;

                CardViewDto cardDto = drawnCards[i];
                RectTransform tempRect = tempCard.GetComponent<RectTransform>();

                Vector3 targetHandWorldPos = selfHandRoot != null ? selfHandRoot.position : centerWorldPos;
                if (TryGetFaceUpVisual(cardDto.instanceId, out NetworkCardVisual realHandView) && realHandView != null)
                {
                    targetHandWorldPos = realHandView.transform.position;
                }

                float handDelay = i * 0.08f;
                Sequence toHandSeq = DOTween.Sequence();
                toHandSeq.Insert(handDelay, tempRect.DOMove(targetHandWorldPos, drawFlyToHandDuration).SetEase(Ease.InOutCubic));
                toHandSeq.Insert(handDelay, tempRect.DOScale(Vector3.one * drawHandScale, drawFlyToHandDuration).SetEase(Ease.InOutQuad));

                maxFlyToHandTime = Mathf.Max(maxFlyToHandTime, handDelay + drawFlyToHandDuration);
            }

            yield return new WaitForSeconds(maxFlyToHandTime);

            for (int i = 0; i < count; i++)
            {
                CardViewDto cardDto = drawnCards[i];
                animatingDrawCardIds.Remove(cardDto.instanceId);
                RevealRealHandCardAfterDraw(cardDto.instanceId);

                NetworkCardVisual tempCard = tempCards[i];
                if (tempCard != null)
                {
                    tempCard.transform.DOKill();
                    activeDrawAnimationObjects.Remove(tempCard.gameObject);
                    Destroy(tempCard.gameObject);
                }
            }
        }

        private void ClearDrawAnimations()
        {
            foreach (GameObject animObj in activeDrawAnimationObjects)
            {
                if (animObj != null)
                {
                    animObj.transform.DOKill();
                    Destroy(animObj);
                }
            }

            activeDrawAnimationObjects.Clear();
            animatingDrawCardIds.Clear();
            pendingDrawSlotIgnoreIds.Clear();
            previousReserveVisualIds.Clear();
            hasPreviousReserveSnapshot = false;
            savedDrawCardCanvasStates.Clear();
            knownHandCardIds.Clear();
            lastSeenRoundNumber = -1;
            activeSelectionSourceCardId = null;
            pendingCancelledSelectionCard = null;
        }

        private void CaptureCurrentReserveVisualIds(MatchSnapshotDto snapshot)
        {
            previousReserveVisualIds.Clear();

            if (snapshot == null || snapshot.roundNumber <= 0 || lastSeenRoundNumber <= 0)
            {
                hasPreviousReserveSnapshot = false;
                return;
            }

            hasPreviousReserveSnapshot = true;

            foreach (KeyValuePair<string, NetworkCardVisual> pair in faceUpViewsById)
            {
                NetworkCardVisual view = pair.Value;
                if (view == null || string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                Transform parent = view.transform.parent;
                if (parent == selfReserveRoot || parent == opponentReserveRoot)
                {
                    previousReserveVisualIds.Add(pair.Key);
                }
            }
        }

        private void StartHandSlotExpansionForDraw(List<CardViewDto> drawnCards)
        {
            if (drawnCards == null || selfHandRoot == null)
            {
                return;
            }

            for (int i = 0; i < drawnCards.Count; i++)
            {
                CardViewDto dto = drawnCards[i];
                if (dto == null || string.IsNullOrWhiteSpace(dto.instanceId))
                {
                    continue;
                }

                pendingDrawSlotIgnoreIds.Remove(dto.instanceId);

                NetworkCardVisual realHandView;
                if (TryGetFaceUpVisual(dto.instanceId, out realHandView) && realHandView != null)
                {
                    StartSlotExpansionTween(realHandView, selfHandRoot, 0.35f);
                }
            }
        }

        private static Tween StartSlotExpansionTween(
            NetworkCardVisual cardView,
            RectTransform parentRoot,
            float duration
        )
        {
            if (cardView == null || parentRoot == null)
            {
                return null;
            }

            LayoutElement layoutElement = cardView.GetComponent<LayoutElement>();
            RectTransform cardRect = cardView.transform as RectTransform;
            if (layoutElement == null || cardRect == null)
            {
                return null;
            }

            layoutElement.ignoreLayout = false;

            float targetWidth = layoutElement.preferredWidth > 0f
                ? layoutElement.preferredWidth
                : cardRect.rect.width;
            if (targetWidth <= 0f)
            {
                targetWidth = 150f;
            }

            float startWidth = 0f;
            HorizontalLayoutGroup layoutGroup = parentRoot.GetComponent<HorizontalLayoutGroup>();
            if (layoutGroup != null && layoutGroup.spacing < 0f)
            {
                startWidth = -layoutGroup.spacing;
            }

            float currentWidth = startWidth;
            layoutElement.preferredWidth = startWidth;
            cardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, startWidth);
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRoot);

            return DOTween
                .To(
                    () => currentWidth,
                    w =>
                    {
                        currentWidth = w;
                        if (layoutElement == null || cardRect == null)
                        {
                            return;
                        }

                        layoutElement.preferredWidth = w;
                        cardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
                        if (parentRoot != null)
                        {
                            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRoot);
                        }
                    },
                    targetWidth,
                    Mathf.Max(0.05f, duration)
                )
                .SetEase(Ease.OutCubic)
                .SetTarget(cardRect)
                .SetLink(cardView.gameObject)
                .OnComplete(() =>
                {
                    if (layoutElement != null)
                    {
                        layoutElement.preferredWidth = targetWidth;
                    }
                    if (cardRect != null)
                    {
                        cardRect.SetSizeWithCurrentAnchors(
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

        public IEnumerator AnimateReserveSlotExpansionForCard(
            NetworkCardVisual revivedView,
            float duration = 0.26f
        )
        {
            if (revivedView == null)
            {
                yield break;
            }

            RectTransform parentRoot = revivedView.transform.parent as RectTransform;
            if (parentRoot == null || parentRoot.childCount <= 1)
            {
                yield break;
            }

            Tween expandTween = StartSlotExpansionTween(revivedView, parentRoot, duration);
            if (expandTween != null)
            {
                yield return expandTween.WaitForCompletion();
            }
        }

        #endregion

        #region Helpers & Board/Reserve Rendering

        private static bool IsPendingAbilitySelectionSource(
            MatchSnapshotDto snapshot,
            CardViewDto card
        )
        {
            return snapshot.viewerMustSelectAbilityTargets
                && snapshot.pendingAbilitySelection != null
                && card != null
                && string.Equals(
                    snapshot.pendingAbilitySelection.sourceCardInstanceId,
                    card.instanceId,
                    StringComparison.Ordinal
                );
        }

        private static void CenterAbilitySelectionSource(NetworkCardVisual view)
        {
            if (view == null || !(view.transform is RectTransform rect))
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity;

            view.SetPending(true);
            view.SetInteractableVisual(false);
        }

        private void RenderOpponentHand(MatchSnapshotDto snapshot)
        {
            if (opponentHandRoot == null)
            {
                return;
            }

            for (int i = 0; i < snapshot.opponent.handCount; i++)
            {
                NetworkCardVisual view = Instantiate(cardPrefab, opponentHandRoot);
                CardVisualLayout layout = view.GetComponent<CardVisualLayout>();

                if (layout != null)
                {
                    layout.Apply(CardVisualLocation.Hand);
                }

                view.BindBack(cardBack);
                DraggableHandCard draggable = view.GetComponent<DraggableHandCard>();

                if (draggable != null)
                {
                    draggable.Configure(false, dragLayer, CardVisualLocation.Hand);
                }

                spawnedViews.Add(view.gameObject);
                opponentHandBacks.Add(view.gameObject);
            }
        }

        private void RenderReserve(
            CardViewDto[] cards,
            RectTransform reserveRoot,
            bool canDrag,
            bool isOpponent
        )
        {
            if (cards == null || reserveRoot == null)
            {
                return;
            }

            foreach (CardViewDto dto in cards)
            {
                NetworkCardVisual view = CreateFaceUpCard(
                    dto,
                    reserveRoot,
                    canDrag,
                    CardVisualLocation.Reserve,
                    isOpponent
                );

                if (
                    hasPreviousReserveSnapshot
                    && dto != null
                    && !string.IsNullOrWhiteSpace(dto.instanceId)
                    && !previousReserveVisualIds.Contains(dto.instanceId)
                    && reserveRoot.childCount > 1
                )
                {
                    StartSlotExpansionTween(view, reserveRoot, 0.25f);
                }
            }
        }

        private void RenderBoard(CardViewDto[] cards, RectTransform[] slots, bool isOpponent)
        {
            if (cards == null || slots == null || slots.Length < 3)
            {
                return;
            }

            foreach (CardViewDto dto in cards)
            {
                if (
                    dto.boardSlotIndex < 0
                    || dto.boardSlotIndex >= slots.Length
                    || slots[dto.boardSlotIndex] == null
                )
                {
                    continue;
                }

                CreateFaceUpCard(
                    dto,
                    slots[dto.boardSlotIndex],
                    false,
                    CardVisualLocation.Board,
                    isOpponent
                );
            }
        }

        private NetworkCardVisual CreateFaceUpCard(
            CardViewDto dto,
            RectTransform parent,
            bool canDrag,
            CardVisualLocation location,
            bool isOpponent = false
        )
        {
            NetworkCardVisual view = Instantiate(cardPrefab, parent);
            CardVisualLayout layout = view.GetComponent<CardVisualLayout>();

            if (layout != null)
            {
                layout.Apply(location);
            }

            view.BindFaceUp(dto, FindArtwork(dto.definitionId));
            view.SetOwnerPerspective(isOpponent);
            DraggableHandCard draggable = view.GetComponent<DraggableHandCard>();

            if (draggable != null)
            {
                draggable.Configure(canDrag, dragLayer, location);
            }

            spawnedViews.Add(view.gameObject);

            if (!string.IsNullOrWhiteSpace(view.InstanceId))
            {
                faceUpViewsById[view.InstanceId] = view;
            }

            if (location == CardVisualLocation.Board)
            {
                view.SetInteractableVisual(true);
            }

            return view;
        }

        #endregion

        #region Combat Presentation

        /// <summary>
        /// Reconstructs any combat participant missing from the latest snapshot and places
        /// it in its resolved slot before the animation director starts.
        /// </summary>
        public void PrepareCombat(RoundResolutionDto resolution, int viewerSeat)
        {
            if (resolution?.steps == null)
            {
                return;
            }

            for (int i = 0; i < resolution.steps.Length; i++)
            {
                CombatStepDto step = resolution.steps[i];

                PrepareResolvedCard(step.hostCard, step.slotIndex, viewerSeat);

                PrepareResolvedCard(step.guestCard, step.slotIndex, viewerSeat);
            }
        }

        public bool TryGetFaceUpVisual(string instanceId, out NetworkCardVisual view)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                view = null;
                return false;
            }

            return faceUpViewsById.TryGetValue(instanceId, out view) && view != null;
        }

        /// <summary>
        /// Makes a freshly summoned source visible before the final snapshot replaces
        /// the previous hand view. Existing card instances keep their visual identity.
        /// </summary>
        public NetworkCardVisual PrepareAbilitySource(
            CardViewDto card,
            int ownerSeat,
            int viewerSeat,
            int zone,
            bool centerReserveSource = false
        )
        {
            return PrepareAbilitySource(
                card,
                ownerSeat,
                viewerSeat,
                zone,
                centerReserveSource,
                out _
            );
        }
        /// <summary>
        /// Stages an existing source at its visible pose before center movement.
        /// Newly revealed opponent sources depart from the consumed Draw hand back.
        /// Already centered sources are reused without restarting their movement.
        /// Targets use centerReserveSource=false and keep their zone parent.
        /// </summary>
        public NetworkCardVisual PrepareAbilitySource(
            CardViewDto card,
            int ownerSeat,
            int viewerSeat,
            int zone,
            bool centerReserveSource,
            out bool requiresCenterMove
        )
        {
            requiresCenterMove = false;

            if (card == null || string.IsNullOrWhiteSpace(card.instanceId))
            {
                return null;
            }

            if (TryGetFaceUpVisual(card.instanceId, out NetworkCardVisual existing))
            {
                if (
                    abilitySelectionSourceRoot != null
                    && existing.transform.parent == abilitySelectionSourceRoot
                )
                {
                    existing.SetPending(false);
                    existing.SetInteractableVisual(true);
                    existing.GetComponent<CardShadowVFX>()?.ShowFixedShadow();
                    return existing;
                }

                if (centerReserveSource && zone == (int)CardZone.Reserve
                    && abilitySelectionSourceRoot != null
                    && existing.transform is RectTransform existingRect)
                {
                    RectTransform previousReserveParent =
                        existingRect.parent == selfReserveRoot || existingRect.parent == opponentReserveRoot
                            ? existingRect.parent as RectTransform
                            : null;
                    int previousReserveSiblingIndex = previousReserveParent != null
                        ? existingRect.GetSiblingIndex()
                        : -1;
                    Vector2 previousReserveSize = existingRect.rect.size;

                    // Preserve the actual staged pose, including parent scale, before
                    // changing layout. The local card already occupies its Reserve slot.
                    existingRect.DOKill();
                    Vector3 center = existingRect.TransformPoint(existingRect.rect.center);
                    float width = existingRect.TransformVector(
                        Vector3.right * existingRect.rect.width).magnitude;
                    float height = existingRect.TransformVector(
                        Vector3.up * existingRect.rect.height).magnitude;
                    Quaternion rotation = existingRect.rotation;
                    existingRect.SetParent(abilitySelectionSourceRoot, true);

                    if (previousReserveParent != null && previousReserveParent.childCount > 0)
                    {
                        SpawnCollapsingReservePlaceholder(
                            previousReserveParent,
                            previousReserveSiblingIndex,
                            previousReserveSize
                        );
                    }
                    existing.GetComponent<CardVisualLayout>()?.Apply(
                        CardVisualLocation.AbilitySelection);
                    CenterAbilitySelectionSource(existing);
                    existingRect.rotation = rotation;
                    existingRect.localScale = Vector3.one;
                    float unitWidth = existingRect.TransformVector(Vector3.right).magnitude;
                    float unitHeight = existingRect.TransformVector(Vector3.up).magnitude;
                    existingRect.localScale = new Vector3(
                        width / Mathf.Max(0.001f, existingRect.rect.width * unitWidth),
                        height / Mathf.Max(0.001f, existingRect.rect.height * unitHeight),
                        1f);
                    existingRect.position = center;
                    existing.GetComponent<DraggableHandCard>()?.Configure(
                        false, dragLayer, CardVisualLocation.AbilitySelection);
                    existing.SetPending(false);
                    existing.SetInteractableVisual(true);
                    existing.GetComponent<CardShadowVFX>()?.ShowFixedShadow();
                    requiresCenterMove = true;
                    return existing;
                }

                RectTransform destination = FindAbilityCardParent(
                    card,
                    ownerSeat,
                    viewerSeat,
                    zone
                );

                if (destination != null && existing.transform.parent != destination)
                {
                    existing.transform.SetParent(destination, false);

                    CardVisualLayout layout = existing.GetComponent<CardVisualLayout>();

                    if (layout != null)
                    {
                        layout.Apply(
                            zone == (int)CardZone.Board
                                ? CardVisualLocation.Board
                                : zone == (int)CardZone.DrawHand
                                    ? CardVisualLocation.Hand
                                    : CardVisualLocation.Reserve
                        );
                    }

                    DraggableHandCard draggable = existing.GetComponent<DraggableHandCard>();

                    if (draggable != null)
                    {
                        draggable.Configure(
                            false,
                            dragLayer,
                            zone == (int)CardZone.Board
                                ? CardVisualLocation.Board
                                : zone == (int)CardZone.DrawHand
                                    ? CardVisualLocation.Hand
                                    : CardVisualLocation.Reserve
                        );
                    }

                    existing.SetPending(false);
                    existing.SetInteractableVisual(true);
                }

                return existing;
            }

            bool placeAtCenter = centerReserveSource
                && zone == (int)CardZone.Reserve
                && abilitySelectionSourceRoot != null;
            RectTransform parent = placeAtCenter
                ? abilitySelectionSourceRoot
                : FindAbilityCardParent(card, ownerSeat, viewerSeat, zone);

            if (parent == null)
            {
                return null;
            }

            CardVisualLocation location = placeAtCenter
                ? CardVisualLocation.AbilitySelection
                : zone == (int)CardZone.Board
                    ? CardVisualLocation.Board
                    : zone == (int)CardZone.DrawHand
                                    ? CardVisualLocation.Hand
                                    : CardVisualLocation.Reserve;

            bool revealOpponentHand = ownerSeat != viewerSeat
                && zone == (int)CardZone.Reserve;
            var handPose = revealOpponentHand ? RemoveOneOpponentHandBack() : null;

            NetworkCardVisual prepared = CreateFaceUpCard(
                card,
                parent,
                false,
                location,
                ownerSeat != viewerSeat
            );

            if (placeAtCenter)
            {
                CenterAbilitySelectionSource(prepared);
                prepared.SetPending(false);
                prepared.GetComponent<CardShadowVFX>()?.ShowFixedShadow();
                GetReserveLayoutMetrics(prepared,
                    out Vector2 reserveSize, out float reserveScale);
                prepared.CardRect.localScale = Vector3.one * reserveScale;
                if (revealOpponentHand)
                {
                    // sourceZone describes the resolved gameplay state. A newly
                    // revealed opponent source visually departs from Draw instead.
                    RectTransform rect = prepared.CardRect;
                    if (handPose.HasValue)
                    {
                        var pose = handPose.Value;
                        rect.rotation = pose.rotation;
                        rect.localScale = Vector3.one;
                        float unitWidth = rect.TransformVector(Vector3.right).magnitude;
                        float unitHeight = rect.TransformVector(Vector3.up).magnitude;
                        rect.localScale = new Vector3(
                            pose.size.x / Mathf.Max(0.001f, rect.rect.width * unitWidth),
                            pose.size.y / Mathf.Max(0.001f, rect.rect.height * unitHeight),
                            1f);
                        rect.position = pose.center;
                    }
                    else if (opponentHandRoot != null)
                    {
                        rect.position = opponentHandRoot.TransformPoint(
                            opponentHandRoot.rect.center);
                    }
                }
                else
                {
                    prepared.CardRect.position = CalculateReserveSlotWorldPosition(
                        selfReserveRoot, reserveSize);
                }
                requiresCenterMove = true;
            }

            prepared.SetInteractableVisual(true);
            return prepared;
        }

        private void CaptureSelectionSourceStartState(MatchSnapshotDto snapshot)
        {
            shouldAnimateCurrentSelectionSource = false;
            pendingCancelledSelectionCard = null;

            string nextSourceId = null;
            if (
                snapshot != null
                && snapshot.viewerMustSelectAbilityTargets
                && snapshot.pendingAbilitySelection != null
                && !string.IsNullOrWhiteSpace(snapshot.pendingAbilitySelection.sourceCardInstanceId)
            )
            {
                nextSourceId = snapshot.pendingAbilitySelection.sourceCardInstanceId;
            }

            CardViewDto cancelledCardDto = FindSelfHandCard(snapshot, activeSelectionSourceCardId);
            if (
                cancelledCardDto != null
                && !string.Equals(activeSelectionSourceCardId, nextSourceId, StringComparison.Ordinal)
                && currentSelectionSourceView != null
                && abilitySelectionSourceRoot != null
                && currentSelectionSourceView.transform.parent == abilitySelectionSourceRoot
            )
            {
                pendingCancelledSelectionCard = cancelledCardDto;
                cancelledSelectionStartWorldPos = currentSelectionSourceView.transform.position;
                cancelledSelectionStartScale = currentSelectionSourceView.transform.localScale;
                animatingDrawCardIds.Add(cancelledCardDto.instanceId);
            }

            currentSelectionSourceView = null;
            activeSelectionSourceCardId = null;

            if (string.IsNullOrWhiteSpace(nextSourceId))
            {
                lastAnimatedSelectionRequestId = null;
                hasCapturedSelectionStartPos = false;
                return;
            }

            string requestId = snapshot.pendingAbilitySelection.requestId;
            if (string.Equals(lastAnimatedSelectionRequestId, requestId, StringComparison.Ordinal))
            {
                return;
            }

            lastAnimatedSelectionRequestId = requestId;
            shouldAnimateCurrentSelectionSource = true;
            hasCapturedSelectionStartPos = false;

            NetworkCardVisual existingView;
            if (
                TryGetFaceUpVisual(nextSourceId, out existingView)
                && existingView != null
                && existingView.transform.parent != selfHandRoot
            )
            {
                capturedSelectionStartWorldPos = existingView.transform.position;
                hasCapturedSelectionStartPos = true;
            }
        }

        private static CardViewDto FindSelfHandCard(
            MatchSnapshotDto snapshot,
            string instanceId
        )
        {
            if (
                snapshot?.self?.hand == null
                || string.IsNullOrWhiteSpace(instanceId)
            )
            {
                return null;
            }

            foreach (CardViewDto card in snapshot.self.hand)
            {
                if (
                    card != null
                    && string.Equals(card.instanceId, instanceId, StringComparison.Ordinal)
                )
                {
                    return card;
                }
            }

            return null;
        }

        private void TriggerPendingSelectionReturnToHand()
        {
            if (pendingCancelledSelectionCard == null)
            {
                return;
            }

            CardViewDto cancelledCard = pendingCancelledSelectionCard;
            pendingCancelledSelectionCard = null;
            string instanceId = cancelledCard.instanceId;

            RectTransform animationParent = dragLayer != null
                ? dragLayer
                : selfHandRoot != null
                    ? selfHandRoot.root as RectTransform
                    : transform as RectTransform;

            if (animationParent == null || cardPrefab == null || selfHandRoot == null)
            {
                if (!string.IsNullOrWhiteSpace(instanceId))
                {
                    animatingDrawCardIds.Remove(instanceId);
                    RevealRealHandCardAfterDraw(instanceId);
                }
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(selfHandRoot);

            Vector3 targetHandWorldPos = selfHandRoot.position;
            NetworkCardVisual realHandView;
            if (
                !string.IsNullOrWhiteSpace(instanceId)
                && TryGetFaceUpVisual(instanceId, out realHandView)
                && realHandView != null
            )
            {
                targetHandWorldPos = realHandView.transform.position;
                if (selfHandRoot.childCount > 1)
                {
                    StartSlotExpansionTween(
                        realHandView,
                        selfHandRoot,
                        abilityReturnToHandDuration * 0.85f
                    );
                }
            }

            NetworkCardVisual tempCard = Instantiate(cardPrefab, animationParent);
            activeDrawAnimationObjects.Add(tempCard.gameObject);

            Canvas tempCanvas = tempCard.GetComponent<Canvas>();
            if (tempCanvas == null)
            {
                tempCanvas = tempCard.gameObject.AddComponent<Canvas>();
            }
            MatchSorting.Apply(tempCanvas, MatchSorting.CardMotion, drawSortingOrder);

            CardVisualLayout layout = tempCard.GetComponent<CardVisualLayout>();
            float handScaleRatio = 0.75f;
            if (layout != null)
            {
                handScaleRatio = GetHandScaleRatio(tempCard, layout);
                layout.Apply(CardVisualLocation.AbilitySelection);
            }

            tempCard.BindFaceUp(cancelledCard, FindArtwork(cancelledCard.definitionId));
            tempCard.SetOwnerPerspective(false);
            tempCard.SetPending(false);
            tempCard.SetInteractableVisual(true);

            DraggableHandCard dragScript = tempCard.GetComponent<DraggableHandCard>();
            if (dragScript != null)
            {
                Destroy(dragScript);
            }

            CanvasGroup tempGroup = tempCard.GetComponent<CanvasGroup>();
            if (tempGroup == null)
            {
                tempGroup = tempCard.gameObject.AddComponent<CanvasGroup>();
            }
            tempGroup.alpha = 1f;
            tempGroup.blocksRaycasts = false;

            RectTransform tempRect = tempCard.GetComponent<RectTransform>();
            tempRect.anchorMin = new Vector2(0.5f, 0.5f);
            tempRect.anchorMax = new Vector2(0.5f, 0.5f);
            tempRect.pivot = new Vector2(0.5f, 0.5f);
            tempRect.position = cancelledSelectionStartWorldPos;
            tempRect.localScale = cancelledSelectionStartScale;
            tempRect.localRotation = Quaternion.identity;

            Sequence returnSeq = DOTween.Sequence();
            returnSeq.SetTarget(tempRect).SetLink(tempCard.gameObject);
            returnSeq.Join(
                tempRect.DOMove(targetHandWorldPos, abilityReturnToHandDuration).SetEase(Ease.InOutCubic)
            );
            returnSeq.Join(
                tempRect.DOScale(Vector3.one * handScaleRatio, abilityReturnToHandDuration).SetEase(Ease.InOutQuad)
            );

            returnSeq.OnComplete(() =>
            {
                if (!string.IsNullOrWhiteSpace(instanceId))
                {
                    animatingDrawCardIds.Remove(instanceId);
                    RevealRealHandCardAfterDraw(instanceId);
                }

                if (tempCard != null)
                {
                    tempCard.transform.DOKill();
                    activeDrawAnimationObjects.Remove(tempCard.gameObject);
                    Destroy(tempCard.gameObject);
                }
            });
        }

        private static float GetHandScaleRatio(NetworkCardVisual view, CardVisualLayout layout)
        {
            if (view == null || layout == null || !(view.transform is RectTransform rect))
            {
                return 0.75f;
            }

            layout.Apply(CardVisualLocation.Hand);
            Vector2 handSize = rect.rect.size;

            layout.Apply(CardVisualLocation.AbilitySelection);
            Vector2 selectionSize = rect.rect.size;

            return selectionSize.x > 0f ? handSize.x / selectionSize.x : 0.75f;
        }

        private void TriggerPendingSelectionMoveToCenter()
        {
            if (!shouldAnimateCurrentSelectionSource || currentSelectionSourceView == null)
            {
                return;
            }

            shouldAnimateCurrentSelectionSource = false;
            GetReserveLayoutMetrics(
                currentSelectionSourceView,
                out Vector2 reserveSize,
                out float reserveScaleRatio
            );

            Vector3 startWorldPos = hasCapturedSelectionStartPos
                ? capturedSelectionStartWorldPos
                : CalculateReserveSlotWorldPosition(selfReserveRoot, reserveSize);

            PlayMoveToCenterSequence(
                currentSelectionSourceView,
                startWorldPos,
                reserveScaleRatio,
                () => FaceUpViewsRendered?.Invoke()
            );
        }

        public IEnumerator AnimateAbilitySourceMoveToCenter(
            NetworkCardVisual sourceView,
            int ownerSeat,
            int viewerSeat
        )
        {
            if (sourceView == null || abilitySelectionSourceRoot == null
                || sourceView.transform.parent != abilitySelectionSourceRoot)
            {
                yield break;
            }

            // Preparation has already preserved the existing source's exact pose or
            // positioned a newly revealed opponent source at its Draw pose.
            RectTransform rect = sourceView.CardRect;
            rect.DOKill();
            Sequence seq = DOTween.Sequence();
            seq.Join(rect.DOLocalMove(Vector3.zero, abilityFlyToCenterDuration)
                .SetEase(Ease.OutCubic));
            seq.Join(rect.DOScale(Vector3.one, abilityFlyToCenterDuration)
                .SetEase(Ease.OutBack, 1.08f));
            seq.Join(rect.DOLocalRotate(Vector3.zero, abilityFlyToCenterDuration)
                .SetEase(Ease.OutCubic));
            yield return seq.WaitForCompletion();
        }

        public void IgnoreCardInReserveLayout(NetworkCardVisual targetView)
        {
            CreateCollapseCardInReserveTween(targetView, 0.22f);
        }

        public IEnumerator AnimateCollapseCardInReserveLayout(
            NetworkCardVisual targetView,
            float duration = 0.22f
        )
        {
            Tween collapseTween = CreateCollapseCardInReserveTween(targetView, duration);
            if (collapseTween != null)
            {
                yield return collapseTween.WaitForCompletion();
            }
        }

        private Tween CreateCollapseCardInReserveTween(
            NetworkCardVisual targetView,
            float duration
        )
        {
            if (targetView == null)
            {
                return null;
            }

            Transform parent = targetView.transform.parent;
            if (parent != selfReserveRoot && parent != opponentReserveRoot)
            {
                return null;
            }

            RectTransform reserveRect = parent as RectTransform;
            RectTransform targetRect = targetView.transform as RectTransform;
            LayoutElement element = targetView.GetComponent<LayoutElement>();

            if (reserveRect == null || targetRect == null)
            {
                if (element != null)
                {
                    element.ignoreLayout = true;
                }
                return null;
            }

            if (element != null && element.ignoreLayout)
            {
                return null;
            }

            CanvasGroup cardGroup = targetView.GetComponent<CanvasGroup>();
            if (cardGroup == null)
            {
                cardGroup = targetView.gameObject.AddComponent<CanvasGroup>();
            }
            cardGroup.alpha = 0f;
            cardGroup.blocksRaycasts = false;

            float startWidth = targetRect.rect.width;
            if (startWidth <= 0f && element != null && element.preferredWidth > 0f)
            {
                startWidth = element.preferredWidth;
            }

            if (startWidth <= 0.5f || duration <= 0.01f)
            {
                if (element != null)
                {
                    element.ignoreLayout = true;
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRect);
                return null;
            }

            float currentWidth = startWidth;
            targetRect.DOKill();

            return DOTween
                .To(
                    () => currentWidth,
                    w =>
                    {
                        currentWidth = w;
                        if (targetRect == null)
                        {
                            return;
                        }

                        if (element != null)
                        {
                            element.preferredWidth = w;
                        }
                        targetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
                        if (reserveRect != null)
                        {
                            LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRect);
                        }
                    },
                    0f,
                    duration
                )
                .SetEase(Ease.InOutQuad)
                .SetTarget(targetRect)
                .SetLink(targetView.gameObject)
                .OnComplete(() =>
                {
                    if (element != null)
                    {
                        element.ignoreLayout = true;
                    }
                    if (reserveRect != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRect);
                    }
                });
        }

        private void SpawnCollapsingReservePlaceholder(
            RectTransform reserveRoot,
            int siblingIndex,
            Vector2 cardSize,
            float duration = 0.22f
        )
        {
            if (reserveRoot == null)
            {
                return;
            }

            if (cardSize.x <= 0f || cardSize.y <= 0f)
            {
                cardSize = new Vector2(150f, 210f);
            }

            RectTransform placeholderRect = CreateReserveSlotPlaceholder(reserveRoot, cardSize);
            spawnedViews.Add(placeholderRect.gameObject);

            if (siblingIndex >= 0 && siblingIndex < reserveRoot.childCount)
            {
                placeholderRect.SetSiblingIndex(siblingIndex);
            }

            LayoutElement element = placeholderRect.GetComponent<LayoutElement>();
            float currentWidth = cardSize.x;

            DOTween
                .To(
                    () => currentWidth,
                    w =>
                    {
                        currentWidth = w;
                        if (placeholderRect == null)
                        {
                            return;
                        }

                        if (element != null)
                        {
                            element.preferredWidth = w;
                        }
                        placeholderRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
                        if (reserveRoot != null)
                        {
                            LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRoot);
                        }
                    },
                    0f,
                    Mathf.Max(0.05f, duration)
                )
                .SetEase(Ease.InOutQuad)
                .SetTarget(placeholderRect)
                .SetLink(placeholderRect.gameObject)
                .OnComplete(() =>
                {
                    if (placeholderRect != null)
                    {
                        spawnedViews.Remove(placeholderRect.gameObject);
                        placeholderRect.SetParent(null, false);
                        placeholderRect.gameObject.SetActive(false);
                        Destroy(placeholderRect.gameObject);
                    }
                    if (reserveRoot != null)
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRoot);
                    }
                });
        }

        public IEnumerator AnimateAbilitySourceReturnToReserve(
            CardViewDto sourceCard,
            int ownerSeat,
            int viewerSeat,
            CardViewDto[] authoritativeReserve = null
        )
        {
            if (
                sourceCard == null
                || string.IsNullOrWhiteSpace(sourceCard.instanceId)
                || !TryGetFaceUpVisual(sourceCard.instanceId, out NetworkCardVisual sourceView)
                || sourceView == null
            )
            {
                yield break;
            }

            if (
                abilitySelectionSourceRoot == null
                || sourceView.transform.parent != abilitySelectionSourceRoot
            )
            {
                yield break;
            }

            RectTransform reserveRoot = ownerSeat == viewerSeat
                ? selfReserveRoot
                : opponentReserveRoot;

            if (reserveRoot == null || !(sourceView.transform is RectTransform sourceRect))
            {
                yield break;
            }

            GetReserveLayoutMetrics(
                sourceView,
                out Vector2 reserveSize,
                out float reserveScaleRatio
            );

            int targetSiblingIndex = ResolveReserveInsertionSiblingIndex(
                reserveRoot,
                sourceCard.instanceId,
                authoritativeReserve
            );

            RectTransform placeholderRect = CreateReserveSlotPlaceholder(reserveRoot, reserveSize);
            spawnedViews.Add(placeholderRect.gameObject);
            if (targetSiblingIndex >= 0 && targetSiblingIndex < reserveRoot.childCount)
            {
                placeholderRect.SetSiblingIndex(targetSiblingIndex);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRoot);
            Vector3 targetWorldPos = placeholderRect.TransformPoint(placeholderRect.rect.center);

            sourceRect.DOKill();
            Sequence returnSeq = DOTween.Sequence();
            returnSeq.Join(
                sourceRect.DOMove(targetWorldPos, abilityReturnToReserveDuration).SetEase(Ease.InOutCubic)
            );
            returnSeq.Join(
                sourceRect.DOScale(Vector3.one * reserveScaleRatio, abilityReturnToReserveDuration).SetEase(Ease.InOutQuad)
            );

            if (reserveRoot.childCount > 1 && placeholderRect != null)
            {
                LayoutElement placeholderElement = placeholderRect.GetComponent<LayoutElement>();
                float currentWidth = 0f;
                if (placeholderElement != null)
                {
                    placeholderElement.preferredWidth = 0f;
                }
                placeholderRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0f);
                LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRoot);

                returnSeq.Join(
                    DOTween
                        .To(
                            () => currentWidth,
                            w =>
                            {
                                currentWidth = w;
                                if (placeholderRect == null)
                                {
                                    return;
                                }

                                if (placeholderElement != null)
                                {
                                    placeholderElement.preferredWidth = w;
                                }
                                placeholderRect.SetSizeWithCurrentAnchors(
                                    RectTransform.Axis.Horizontal,
                                    w
                                );
                                if (reserveRoot != null)
                                {
                                    LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRoot);
                                }
                            },
                            reserveSize.x,
                            abilityReturnToReserveDuration * 0.85f
                        )
                        .SetEase(Ease.OutCubic)
                        .SetLink(placeholderRect.gameObject)
                );
            }

            yield return returnSeq.WaitForCompletion();

            int dockSiblingIndex = -1;
            if (placeholderRect != null)
            {
                dockSiblingIndex = placeholderRect.GetSiblingIndex();
                spawnedViews.Remove(placeholderRect.gameObject);
                placeholderRect.SetParent(null, false);
                placeholderRect.gameObject.SetActive(false);
                Destroy(placeholderRect.gameObject);
            }

            if (sourceView != null && reserveRoot != null)
            {
                DockSourceCardIntoReserve(sourceView, reserveRoot, dockSiblingIndex);
            }
        }

        private struct CombatSurvivorReturnPlan
        {
            public NetworkCardVisual view;
            public RectTransform cardRect;
            public RectTransform reserveRoot;
            public RectTransform placeholderRect;
            public Vector3 targetWorldPos;
            public float reserveWidth;
            public float scaleRatio;
        }

        public IEnumerator AnimateCombatSurvivorsReturnToReserve(
            IReadOnlyList<ResolvedCardDto> survivors,
            int viewerSeat,
            MatchSnapshotDto postCombatSnapshot,
            RectTransform motionLayer = null
        )
        {
            if (survivors == null || survivors.Count == 0)
            {
                yield break;
            }

            var plans = new List<CombatSurvivorReturnPlan>(survivors.Count);
            bool hasSelfSurvivor = false;
            bool hasOpponentSurvivor = false;

            for (int i = 0; i < survivors.Count; i++)
            {
                ResolvedCardDto survivor = survivors[i];
                if (
                    survivor?.cardBefore == null
                    || string.IsNullOrWhiteSpace(survivor.cardBefore.instanceId)
                    || survivor.died
                    || survivor.diedFromEphemeral
                    || survivor.healthAfter <= 0
                )
                {
                    continue;
                }

                string definitionId = survivor.cardBefore.definitionId;
                if (
                    string.Equals(definitionId, "MaDa", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(definitionId, "MaLai", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                NetworkCardVisual cardView;
                if (
                    !TryGetFaceUpVisual(survivor.cardBefore.instanceId, out cardView)
                    || cardView == null
                )
                {
                    continue;
                }

                RectTransform cardRect = cardView.transform as RectTransform;
                if (cardRect == null)
                {
                    continue;
                }

                bool isSelf = survivor.seat == viewerSeat;
                RectTransform reserveRoot = isSelf ? selfReserveRoot : opponentReserveRoot;
                if (reserveRoot == null || cardView.transform.parent == reserveRoot)
                {
                    continue;
                }

                Vector2 reserveSize;
                float scaleRatio;
                GetBoardToReserveLayoutMetrics(cardView, out reserveSize, out scaleRatio);

                CardViewDto[] authoritativeReserve = isSelf
                    ? postCombatSnapshot?.self?.reserve
                    : postCombatSnapshot?.opponent?.reserve;

                int targetSiblingIndex = ResolveReserveInsertionSiblingIndex(
                    reserveRoot,
                    survivor.cardBefore.instanceId,
                    authoritativeReserve
                );

                RectTransform placeholderRect = CreateReserveSlotPlaceholder(reserveRoot, reserveSize);
                spawnedViews.Add(placeholderRect.gameObject);

                if (targetSiblingIndex >= 0 && targetSiblingIndex < reserveRoot.childCount)
                {
                    placeholderRect.SetSiblingIndex(targetSiblingIndex);
                }

                if (isSelf)
                {
                    hasSelfSurvivor = true;
                }
                else
                {
                    hasOpponentSurvivor = true;
                }

                plans.Add(
                    new CombatSurvivorReturnPlan
                    {
                        view = cardView,
                        cardRect = cardRect,
                        reserveRoot = reserveRoot,
                        placeholderRect = placeholderRect,
                        targetWorldPos = Vector3.zero,
                        reserveWidth = reserveSize.x,
                        scaleRatio = scaleRatio,
                    }
                );
            }

            if (plans.Count == 0)
            {
                yield break;
            }

            Canvas.ForceUpdateCanvases();
            if (hasSelfSurvivor && selfReserveRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(selfReserveRoot);
            }
            if (hasOpponentSurvivor && opponentReserveRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(opponentReserveRoot);
            }

            for (int i = 0; i < plans.Count; i++)
            {
                CombatSurvivorReturnPlan plan = plans[i];
                if (plan.placeholderRect != null)
                {
                    plan.targetWorldPos = plan.placeholderRect.TransformPoint(
                        plan.placeholderRect.rect.center
                    );
                }
                plans[i] = plan;
            }

            RectTransform effectiveMotionLayer = motionLayer != null ? motionLayer : dragLayer;
            Sequence masterSeq = DOTween.Sequence();

            for (int i = 0; i < plans.Count; i++)
            {
                CombatSurvivorReturnPlan plan = plans[i];
                if (plan.cardRect == null || plan.placeholderRect == null)
                {
                    continue;
                }

                Vector3 targetWorldPos = plan.targetWorldPos;
                float delay = i * Mathf.Max(0f, combatReturnToReserveStagger);

                LayoutElement placeholderLayout = plan.placeholderRect.GetComponent<LayoutElement>();
                if (
                    placeholderLayout != null
                    && plan.reserveRoot != null
                    && plan.reserveRoot.childCount > 1
                )
                {
                    float targetWidth = plan.reserveWidth > 0f ? plan.reserveWidth : 150f;
                    float currentWidth = 0f;
                    placeholderLayout.preferredWidth = 0f;
                    plan.placeholderRect.SetSizeWithCurrentAnchors(
                        RectTransform.Axis.Horizontal,
                        0f
                    );

                    RectTransform capturedReserveRoot = plan.reserveRoot;
                    RectTransform capturedPlaceholderRect = plan.placeholderRect;
                    masterSeq.Insert(
                        delay,
                        DOTween
                            .To(
                                () => currentWidth,
                                w =>
                                {
                                    currentWidth = w;
                                    if (
                                        capturedPlaceholderRect == null
                                        || placeholderLayout == null
                                    )
                                    {
                                        return;
                                    }

                                    placeholderLayout.preferredWidth = w;
                                    capturedPlaceholderRect.SetSizeWithCurrentAnchors(
                                        RectTransform.Axis.Horizontal,
                                        w
                                    );
                                    if (capturedReserveRoot != null)
                                    {
                                        LayoutRebuilder.ForceRebuildLayoutImmediate(
                                            capturedReserveRoot
                                        );
                                    }
                                },
                                targetWidth,
                                combatReturnToReserveDuration * 0.85f
                            )
                            .SetEase(Ease.OutCubic)
                            .SetLink(capturedPlaceholderRect.gameObject)
                    );
                }

                plan.cardRect.DOKill();

                if (effectiveMotionLayer != null)
                {
                    plan.cardRect.SetParent(effectiveMotionLayer, true);
                    plan.cardRect.SetAsLastSibling();
                }

                plan.view.GetComponent<CardShadowVFX>()?.ShowFixedShadow();

                masterSeq.Insert(
                    delay,
                    plan.cardRect.DOMove(targetWorldPos, combatReturnToReserveDuration)
                        .SetEase(Ease.InOutCubic)
                );
                masterSeq.Insert(
                    delay,
                    plan.cardRect.DOScale(
                        Vector3.one * plan.scaleRatio,
                        combatReturnToReserveDuration
                    ).SetEase(Ease.InOutQuad)
                );
            }

            if (hasSelfSurvivor && selfReserveRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(selfReserveRoot);
            }
            if (hasOpponentSurvivor && opponentReserveRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(opponentReserveRoot);
            }

            yield return masterSeq.WaitForCompletion();

            for (int i = 0; i < plans.Count; i++)
            {
                CombatSurvivorReturnPlan plan = plans[i];
                int dockSiblingIndex = -1;

                if (plan.placeholderRect != null)
                {
                    dockSiblingIndex = plan.placeholderRect.GetSiblingIndex();
                    spawnedViews.Remove(plan.placeholderRect.gameObject);
                    plan.placeholderRect.SetParent(null, false);
                    plan.placeholderRect.gameObject.SetActive(false);
                    Destroy(plan.placeholderRect.gameObject);
                }

                if (plan.view != null && plan.reserveRoot != null)
                {
                    plan.view.GetComponent<CardShadowVFX>()?.HideShadow();
                    DockSourceCardIntoReserve(plan.view, plan.reserveRoot, dockSiblingIndex);
                }
            }
        }

        private static void GetBoardToReserveLayoutMetrics(
            NetworkCardVisual view,
            out Vector2 reserveSize,
            out float boardToReserveScaleRatio
        )
        {
            reserveSize = new Vector2(150f, 210f);
            boardToReserveScaleRatio = 0.6f;

            if (view == null)
            {
                return;
            }

            RectTransform rect = view.transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            CardVisualLayout layout = view.GetComponent<CardVisualLayout>();
            if (layout == null)
            {
                return;
            }

            CardVisualLocation originalLocation = layout.CurrentLocation;

            layout.Apply(CardVisualLocation.Reserve);
            reserveSize = rect.rect.size;

            layout.Apply(CardVisualLocation.Board);
            Vector2 boardSize = rect.rect.size;

            if (boardSize.x > 0f)
            {
                boardToReserveScaleRatio = reserveSize.x / boardSize.x;
            }

            if (originalLocation != CardVisualLocation.Board)
            {
                layout.Apply(originalLocation);
            }
        }

        private int ResolveReserveInsertionSiblingIndex(
            RectTransform reserveRoot,
            string sourceInstanceId,
            CardViewDto[] authoritativeReserve
        )
        {
            if (
                reserveRoot == null
                || string.IsNullOrWhiteSpace(sourceInstanceId)
                || authoritativeReserve == null
            )
            {
                return -1;
            }

            int sourceIndex = -1;
            for (int i = 0; i < authoritativeReserve.Length; i++)
            {
                if (
                    authoritativeReserve[i] != null
                    && string.Equals(
                        authoritativeReserve[i].instanceId,
                        sourceInstanceId,
                        StringComparison.Ordinal
                    )
                )
                {
                    sourceIndex = i;
                    break;
                }
            }

            if (sourceIndex < 0)
            {
                return -1;
            }

            for (int i = sourceIndex + 1; i < authoritativeReserve.Length; i++)
            {
                string nextId = authoritativeReserve[i]?.instanceId;
                if (
                    !string.IsNullOrWhiteSpace(nextId)
                    && TryGetFaceUpVisual(nextId, out NetworkCardVisual nextView)
                    && nextView != null
                    && nextView.transform.parent == reserveRoot
                )
                {
                    LayoutElement element = nextView.GetComponent<LayoutElement>();
                    if (element == null || !element.ignoreLayout)
                    {
                        return nextView.transform.GetSiblingIndex();
                    }
                }
            }

            return -1;
        }

        private void DockSourceCardIntoReserve(
            NetworkCardVisual sourceView,
            RectTransform reserveRoot,
            int siblingIndex = -1
        )
        {
            sourceView.transform.SetParent(reserveRoot, false);

            if (siblingIndex >= 0 && siblingIndex < reserveRoot.childCount)
            {
                sourceView.transform.SetSiblingIndex(siblingIndex);
            }

            CardVisualLayout layout = sourceView.GetComponent<CardVisualLayout>();
            if (layout != null)
            {
                layout.Apply(CardVisualLocation.Reserve);
            }

            DraggableHandCard draggable = sourceView.GetComponent<DraggableHandCard>();
            if (draggable != null)
            {
                draggable.Configure(false, dragLayer, CardVisualLocation.Reserve);
            }

            sourceView.SetPending(false);
            sourceView.SetInteractableVisual(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRoot);
        }

        private static Vector3 CalculateReserveSlotWorldPosition(
            RectTransform reserveRoot,
            Vector2 reserveSize
        )
        {
            if (reserveRoot == null)
            {
                return Vector3.zero;
            }

            Canvas.ForceUpdateCanvases();
            RectTransform placeholderRect = CreateReserveSlotPlaceholder(reserveRoot, reserveSize);
            LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRoot);

            Vector3 slotWorldPos = placeholderRect.TransformPoint(placeholderRect.rect.center);

            placeholderRect.SetParent(null, false);
            placeholderRect.gameObject.SetActive(false);
            Destroy(placeholderRect.gameObject);
            LayoutRebuilder.ForceRebuildLayoutImmediate(reserveRoot);

            return slotWorldPos;
        }

        private Sequence PlayMoveToCenterSequence(
            NetworkCardVisual view,
            Vector3 startWorldPos,
            float startScale,
            Action onComplete = null
        )
        {
            if (view == null || !(view.transform is RectTransform rect))
            {
                onComplete?.Invoke();
                return null;
            }

            rect.DOKill();
            rect.position = startWorldPos;
            rect.localScale = Vector3.one * startScale;

            Sequence seq = DOTween.Sequence();
            seq.Join(rect.DOLocalMove(Vector3.zero, abilityFlyToCenterDuration).SetEase(Ease.OutCubic));
            seq.Join(rect.DOScale(Vector3.one, abilityFlyToCenterDuration).SetEase(Ease.OutBack, 1.08f));

            if (onComplete != null)
            {
                seq.OnComplete(() => onComplete.Invoke());
            }

            return seq;
        }

        private static void GetReserveLayoutMetrics(
            NetworkCardVisual view,
            out Vector2 reserveSize,
            out float reserveScaleRatio
        )
        {
            reserveSize = new Vector2(150f, 210f);
            reserveScaleRatio = 0.75f;

            if (view == null || !(view.transform is RectTransform rect))
            {
                return;
            }

            CardVisualLayout layout = view.GetComponent<CardVisualLayout>();
            if (layout == null)
            {
                return;
            }

            CardVisualLocation originalLocation = layout.CurrentLocation;

            layout.Apply(CardVisualLocation.Reserve);
            reserveSize = rect.rect.size;

            layout.Apply(CardVisualLocation.AbilitySelection);
            Vector2 selectionSize = rect.rect.size;

            if (selectionSize.x > 0f)
            {
                reserveScaleRatio = reserveSize.x / selectionSize.x;
            }

            if (originalLocation != CardVisualLocation.AbilitySelection)
            {
                layout.Apply(originalLocation);
            }
        }

        private static RectTransform CreateReserveSlotPlaceholder(
            RectTransform reserveRoot,
            Vector2 reserveSize
        )
        {
            var placeholderObj = new GameObject(
                "ReserveSlotPlaceholder",
                typeof(RectTransform),
                typeof(LayoutElement)
            );
            RectTransform placeholderRect = placeholderObj.GetComponent<RectTransform>();
            placeholderRect.SetParent(reserveRoot, false);
            placeholderRect.anchorMin = new Vector2(0.5f, 0.5f);
            placeholderRect.anchorMax = new Vector2(0.5f, 0.5f);
            placeholderRect.pivot = new Vector2(0.5f, 0.5f);
            placeholderRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, reserveSize.x);
            placeholderRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, reserveSize.y);

            LayoutElement element = placeholderObj.GetComponent<LayoutElement>();
            element.preferredWidth = reserveSize.x;
            element.preferredHeight = reserveSize.y;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;

            return placeholderRect;
        }

        /// <summary>
        /// Materializes a confirmed copy in its owner's hand. Hidden copies use only
        /// a card back; visible copies are excluded from the next snapshot's draw flight.
        /// Views share the usual snapshot/reset cleanup lifecycle.
        /// </summary>
        public NetworkCardVisual PrepareCopiedHandCard(
            CardViewDto card, int ownerSeat, int viewerSeat)
        {
            bool ownCard = ownerSeat == viewerSeat;
            RectTransform parent = ownCard ? selfHandRoot : opponentHandRoot;
            if (parent == null || cardPrefab == null || (ownCard && card == null))
            {
                return null;
            }

            NetworkCardVisual view;
            if (ownCard)
            {
                view = CreateFaceUpCard(card, parent, false, CardVisualLocation.Hand);
                knownHandCardIds.Add(card.instanceId);
            }
            else
            {
                view = Instantiate(cardPrefab, parent);
                view.GetComponent<CardVisualLayout>()?.Apply(CardVisualLocation.Hand);
                view.BindBack(cardBack);
                view.SetOwnerPerspective(true);
                view.GetComponent<DraggableHandCard>()?.Configure(
                    false, dragLayer, CardVisualLocation.Hand);
                spawnedViews.Add(view.gameObject);
                opponentHandBacks.Add(view.gameObject);
            }

            CanvasGroup group = view.GetComponent<CanvasGroup>();
            if (group == null) group = view.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            Canvas.ForceUpdateCanvases();
            return view;
        }

        /// <summary>
        /// Creates a new reserve view even when an earlier view of the same card has
        /// just shattered. The old view stays intact until this update finishes.
        /// </summary>
        public NetworkCardVisual PrepareRevivedCard(
            CardViewDto card,
            int ownerSeat,
            int viewerSeat,
            bool reserveCanDrag
        )
        {
            if (card == null || string.IsNullOrWhiteSpace(card.instanceId))
            {
                return null;
            }

            RectTransform parent = ownerSeat == viewerSeat
                ? selfReserveRoot
                : opponentReserveRoot;

            if (parent == null)
            {
                return null;
            }

            NetworkCardVisual revived = CreateFaceUpCard(
                card,
                parent,
                ownerSeat == viewerSeat && reserveCanDrag,
                CardVisualLocation.Reserve,
                ownerSeat != viewerSeat
            );
            return revived;
        }

        private RectTransform FindAbilityCardParent(
            CardViewDto card,
            int ownerSeat,
            int viewerSeat,
            int zone
        )
        {
            bool isSelf = ownerSeat == viewerSeat;

            if (zone == (int)CardZone.DrawHand)
                return isSelf ? selfHandRoot : null;

            if (zone == (int)CardZone.Reserve)
            {
                return isSelf ? selfReserveRoot : opponentReserveRoot;
            }

            if (zone != (int)CardZone.Board)
            {
                return null;
            }

            RectTransform[] slots = isSelf ? selfBoardSlots : opponentBoardSlots;

            return slots != null
                && card.boardSlotIndex >= 0
                && card.boardSlotIndex < slots.Length
                ? slots[card.boardSlotIndex]
                : null;
        }

        public void ClearAbilityTargetVisuals()
        {
            foreach (NetworkCardVisual view in faceUpViewsById.Values)
            {
                if (view != null)
                {
                    view.ClearAbilityTargetState();
                }
            }
        }

        private void PrepareResolvedCard(
            ResolvedCardDto resolvedCard,
            int slotIndex,
            int viewerSeat
        )
        {
            CardViewDto card = resolvedCard?.cardBefore;

            if (card == null || string.IsNullOrWhiteSpace(card.instanceId))
            {
                return;
            }

            bool isSelfCard = resolvedCard.seat == viewerSeat;
            RectTransform[] targetSlots = isSelfCard
                ? selfBoardSlots
                : opponentBoardSlots;

            if (
                targetSlots == null
                || slotIndex < 0
                || slotIndex >= targetSlots.Length
                || targetSlots[slotIndex] == null
            )
            {
                Debug.LogWarning(
                    $"Không thể chuẩn bị combat visual. "
                        + $"Seat={resolvedCard.seat}, Slot={slotIndex}."
                );

                return;
            }

            bool viewAlreadyExists = TryGetFaceUpVisual(
                card.instanceId,
                out NetworkCardVisual view
            );

            if (!viewAlreadyExists)
            {
                view = CreateFaceUpCard(
                    card,
                    targetSlots[slotIndex],
                    false,
                    CardVisualLocation.Board,
                    !isSelfCard
                );
            }
            else
            {
                PlaceExistingCardOnBoard(view, card, targetSlots[slotIndex]);
            }

            view.SetPending(false);
            view.SetInteractableVisual(true);
        }

        private void PlaceExistingCardOnBoard(
            NetworkCardVisual view,
            CardViewDto card,
            RectTransform targetSlot
        )
        {
            view.transform.SetParent(targetSlot, false);

            CardVisualLayout layout = view.GetComponent<CardVisualLayout>();

            if (layout != null)
            {
                layout.Apply(CardVisualLocation.Board);
            }

            RectTransform cardRect = view.transform as RectTransform;

            if (cardRect != null)
            {
                cardRect.anchorMin = new Vector2(0.5f, 0.5f);
                cardRect.anchorMax = new Vector2(0.5f, 0.5f);
                cardRect.pivot = new Vector2(0.5f, 0.5f);
                cardRect.anchoredPosition = Vector2.zero;
                cardRect.localScale = Vector3.one;
                cardRect.localRotation = Quaternion.identity;
            }

            view.BindFaceUp(card, FindArtwork(card.definitionId));

            DraggableHandCard draggable = view.GetComponent<DraggableHandCard>();

            if (draggable != null)
            {
                draggable.Configure(false, dragLayer, CardVisualLocation.Board);
            }

            view.SetInteractableVisual(true);
        }

        private (Vector3 center, Vector2 size, Quaternion rotation)?
            RemoveOneOpponentHandBack()
        {
            for (int i = opponentHandBacks.Count - 1; i >= 0; i--)
            {
                GameObject cardBackObject = opponentHandBacks[i];
                opponentHandBacks.RemoveAt(i);

                if (cardBackObject == null)
                {
                    continue;
                }

                // Capture the visible Draw pose before removing it from layout.
                RectTransform rect = (RectTransform)cardBackObject.transform;
                Vector3 center = rect.TransformPoint(rect.rect.center);
                Vector2 size = new Vector2(
                    rect.TransformVector(Vector3.right * rect.rect.width).magnitude,
                    rect.TransformVector(Vector3.up * rect.rect.height).magnitude);
                Quaternion rotation = rect.rotation;
                spawnedViews.Remove(cardBackObject);
                cardBackObject.SetActive(false);
                Destroy(cardBackObject);
                return (center, size, rotation);
            }

            return null;
        }

        #endregion

        #region Artwork Lookup

        public NetworkCardVisual CreateGraveyardCard(
            CardViewDto dto,
            RectTransform parent
        )
        {
            if (dto == null || parent == null || cardPrefab == null)
            {
                return null;
            }

            NetworkCardVisual view = Instantiate(cardPrefab, parent);

            view.BindFaceUp(dto, FindArtwork(dto.definitionId));
            view.SetPending(false);
            view.ClearAbilityTargetState();
            view.SetInteractableVisual(true);

            return view;
        }

        private CardArtworkView FindArtwork(string definitionId)
        {
            if (presentationCatalog == null)
            {
                return null;
            }

            return presentationCatalog.Find(definitionId);
        }

        #endregion

        #region View Cleanup

        private void ClearViews()
        {
            foreach (GameObject spawnedView in spawnedViews)
            {
                if (spawnedView != null)
                {
                    spawnedView.transform.DOKill();
                    spawnedView.SetActive(false);
                    spawnedView.transform.SetParent(null, false);
                    Destroy(spawnedView);
                }
            }

            spawnedViews.Clear();
            opponentHandBacks.Clear();
            faceUpViewsById.Clear();
        }

        #endregion
    }
}
