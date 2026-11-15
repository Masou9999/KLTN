using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using KLTN.Game.Networking;
using UnityEngine;

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

        [Tooltip("Sorting Order của lá bài lúc đang bay rút bài để nổi lên trên LocalActionStatus")]
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

        #endregion

        #region Runtime State

        private readonly List<GameObject> spawnedViews = new List<GameObject>();
        private MatchClientProjection projection;

        private readonly List<GameObject> opponentHandBacks = new List<GameObject>();

        private readonly Dictionary<string, NetworkCardVisual> faceUpViewsById =
            new Dictionary<string, NetworkCardVisual>();

        private readonly HashSet<string> knownHandCardIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> animatingDrawCardIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, (float alpha, bool blocksRaycasts)> savedDrawCardCanvasStates =
            new Dictionary<string, (float alpha, bool blocksRaycasts)>(StringComparer.Ordinal);
        private readonly List<GameObject> activeDrawAnimationObjects = new List<GameObject>();

        private int lastSeenRoundNumber = -1;

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
        }

        private void RevealRealHandCardAfterDraw(string instanceId)
        {
            if (TryGetFaceUpVisual(instanceId, out NetworkCardVisual realHandView) && realHandView != null)
            {
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
                tempCanvas.overrideSorting = true;
                tempCanvas.sortingOrder = drawSortingOrder + i;

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
            savedDrawCardCanvasStates.Clear();
            knownHandCardIds.Clear();
            lastSeenRoundNumber = -1;
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
                CreateFaceUpCard(dto, reserveRoot, canDrag, CardVisualLocation.Reserve, isOpponent);
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
            bool centerNewSource = false
        )
        {
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
                                : CardVisualLocation.Reserve
                        );
                    }

                    existing.SetPending(false);
                    existing.SetInteractableVisual(true);
                }

                return existing;
            }

            bool placeAtCenter = centerNewSource
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
                    : CardVisualLocation.Reserve;

            if (ownerSeat != viewerSeat && zone == (int)CardZone.Reserve)
            {
                RemoveOneOpponentHandBack();
            }

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
            }

            prepared.SetInteractableVisual(true);
            return prepared;
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

        private void RemoveOneOpponentHandBack()
        {
            for (int i = opponentHandBacks.Count - 1; i >= 0; i--)
            {
                GameObject cardBackObject = opponentHandBacks[i];
                opponentHandBacks.RemoveAt(i);

                if (cardBackObject == null)
                {
                    continue;
                }

                spawnedViews.Remove(cardBackObject);
                cardBackObject.SetActive(false);
                Destroy(cardBackObject);
                return;
            }
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
                    spawnedView.SetActive(false);
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