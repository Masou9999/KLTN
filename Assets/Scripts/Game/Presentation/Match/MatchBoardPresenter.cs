using System;
using System.Collections.Generic;
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
        public MulliganPresenter mulliganPresenter;

        #endregion

        #region Runtime State

        private readonly List<GameObject> spawnedViews = new List<GameObject>();
        private MatchClientProjection projection;

        private readonly List<GameObject> opponentHandBacks = new List<GameObject>();

        private readonly Dictionary<string, NetworkCardVisual> faceUpViewsById =
            new Dictionary<string, NetworkCardVisual>();

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

            if (mulliganPresenter != null)
            {
                mulliganPresenter.UpdateMulliganState(snapshot);
            }

            if (snapshot.roundNumber > 0)
            {
                RenderSelfHand(snapshot);
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
                    !isSelectionSource
                    && snapshot.viewerCanAct
                    && hasActiveRosterSpace
                    && dto.energy <= snapshot.self.mana;

                NetworkCardVisual view = CreateFaceUpCard(dto, parent, canDrag, location);

                
if (isSelectionSource)
{
    CenterAbilitySelectionSource(view);

    CardShadowVFX shadow = view.GetComponent<CardShadowVFX>();
    shadow?.ShowFixedShadow();
}

            }
        }

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
                // Keep a pending Play source in the centre for its entire host-confirmed
                // ability sequence. The next snapshot moves it to Reserve afterward.
                
if (
    abilitySelectionSourceRoot != null
    && existing.transform.parent == abilitySelectionSourceRoot
)
{
    existing.SetPending(false);
    existing.SetInteractableVisual(true);

    CardShadowVFX shadow = existing.GetComponent<CardShadowVFX>();
    shadow?.ShowFixedShadow();

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

    CardShadowVFX shadow = prepared.GetComponent<CardShadowVFX>();
    shadow?.ShowFixedShadow();
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
