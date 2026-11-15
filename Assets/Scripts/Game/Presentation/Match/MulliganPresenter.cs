using System.Collections;
using System.Collections.Generic;
using KLTN.Game.Content;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class MulliganPresenter : MonoBehaviour
{
    #region Serialized Fields

    [Header("UI References")]
    [SerializeField] private Transform cardsContainer;
    [SerializeField] private Transform deckSpawnPoint;
    [SerializeField] private Transform handContainer;
    [SerializeField] private float handCardScale = 0.45f;
    [SerializeField] private float viewAfterSwapDelay = 1.5f;
    [SerializeField] private GameObject networkCardPrefab;
    [SerializeField] private CardPresentationCatalog presentationCatalog;

    [Header("Mulligan UI Elements")]
    [SerializeField] private GameObject darkBackDrop;
    [SerializeField] private GameObject topicGroup;
    [SerializeField] private TextMeshProUGUI myTurnText;
    [SerializeField] private GameObject topBar;

    [Header("Action Button Override")]
    [SerializeField] private Button actionButton;
    [SerializeField] private TextMeshProUGUI actionButtonText;

    #endregion

    #region Runtime State

    private readonly List<ulong> selectedToReplace = new List<ulong>();
    private readonly List<ulong> keptCardIds = new List<ulong>();
    private readonly List<Vector3> replacedSlotPositions = new List<Vector3>();
    private readonly Dictionary<CanvasGroup, (float alpha, bool blocksRaycasts)> savedHandCardStates =
        new Dictionary<CanvasGroup, (float alpha, bool blocksRaycasts)>();

    private bool hasConfirmed;
    private bool bothPlayersConfirmed;
    private bool isAnimating;
    private bool isReturningToDeck;
    private bool hasSpawnedNewCards;
    private bool hasMovedToHand;
    private bool isTransitioningToMatch;
    private bool hideRealHandCards;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (actionButton != null)
        {
            actionButton.onClick.AddListener(OnConfirmClicked);
        }
    }

    private void OnDisable()
    {
        if (actionButton != null)
        {
            actionButton.onClick.RemoveListener(OnConfirmClicked);
        }

        DestroyMulliganCardObjects();
    }

    private void LateUpdate()
    {
        if (hideRealHandCards)
        {
            ApplyHandCardsAlpha(false);
        }
    }

    #endregion

    #region Mulligan Rendering

    public void UpdateMulliganState(MatchSnapshotDto snapshot)
    {
        if (snapshot == null) return;

        if (snapshot.opponent == null)
        {
            SetRealHandVisibility(true);
            SetTopBarVisibility(true);
            gameObject.SetActive(false);
            ClearViews();
            return;
        }

        if (snapshot.roundNumber == 0)
        {
            SetTopBarVisibility(false);
            UpdateTurnOrderText(snapshot);
        }

        if (
            !hasConfirmed
            && snapshot.roundNumber == 0
            && !string.IsNullOrEmpty(snapshot.matchId)
            && snapshot.turnClock != null
            && snapshot.turnClock.self == null
        )
        {
            hasConfirmed = true;
            hasSpawnedNewCards = true;
            isReturningToDeck = false;

            if (cardsContainer != null)
            {
                foreach (Transform child in cardsContainer)
                {
                    MulliganCardToggle toggle = child.GetComponent<MulliganCardToggle>();
                    if (toggle != null)
                    {
                        toggle.LockAndClearOverlay();
                        Destroy(toggle);
                    }
                }
            }

            if (actionButtonText != null) actionButtonText.text = "ĐANG CHỜ...";
            if (actionButton != null)
            {
                actionButton.onClick.RemoveListener(OnConfirmClicked);
                actionButton.interactable = false;
            }
            return;
        }

        if (snapshot.roundNumber == 0 && !hasConfirmed)
        {
            if (snapshot.self?.hand == null || snapshot.self.hand.Length == 0)
            {
                gameObject.SetActive(false);
                return;
            }

            bothPlayersConfirmed = false;
            hasMovedToHand = false;
            gameObject.SetActive(true);
            SetRealHandVisibility(false);
            SetTopBarVisibility(false);

            if (actionButtonText != null)
            {
                actionButtonText.text = "XÁC NHẬN";
            }

            if (actionButton != null && !isAnimating)
            {
                actionButton.interactable = true;
            }

            if (cardsContainer != null && cardsContainer.childCount == 0)
            {
                RenderOpeningHand(snapshot);
            }
            return;
        }

        if (hasConfirmed && !hasSpawnedNewCards && snapshot.self?.hand != null)
        {
            List<CardViewDto> newCardsData = ExtractNewCardsFromSnapshot(snapshot);

            if (newCardsData.Count > 0 && newCardsData.Count >= replacedSlotPositions.Count)
            {
                hasSpawnedNewCards = true;
                StartCoroutine(SpawnNewCardsFromDeckRoutine(newCardsData));
            }
        }

        if (snapshot.roundNumber > 0)
        {
            if (actionButton != null)
            {
                actionButton.onClick.RemoveListener(OnConfirmClicked);
                actionButton.interactable = true;
            }

            if (hasMovedToHand && !isTransitioningToMatch)
            {
                SetRealHandVisibility(true);
                SetTopBarVisibility(true);
                if (gameObject.activeInHierarchy) gameObject.SetActive(false);
                ClearViews();
            }
            else if (!isTransitioningToMatch && gameObject.activeInHierarchy)
            {
                StartCoroutine(WaitAndCloseMulliganRoutine(snapshot));
            }
            else if (!isTransitioningToMatch && !gameObject.activeInHierarchy)
            {
                SetRealHandVisibility(true);
                SetTopBarVisibility(true);
                ClearViews();
            }
        }
    }

    private void UpdateTurnOrderText(MatchSnapshotDto snapshot)
    {
        if (myTurnText == null || snapshot == null) return;

        int localSeat = snapshot.self != null ? snapshot.self.seat : snapshot.viewerSeat;
        bool isGoingFirst = snapshot.firstSeat == localSeat;

        myTurnText.text = isGoingFirst ? "Lượt của bạn" : "Lượt của đối thủ";
    }

    public void NotifyBothPlayersConfirmed(float swapWaitDuration)
    {
        if (bothPlayersConfirmed || !gameObject.activeInHierarchy) return;

        bothPlayersConfirmed = true;
        StartCoroutine(WaitBothConfirmedAndMoveToHandRoutine(swapWaitDuration));
    }

    private List<CardViewDto> ExtractNewCardsFromSnapshot(MatchSnapshotDto snapshot)
    {
        List<CardViewDto> newCards = new List<CardViewDto>();
        if (snapshot.self?.hand == null) return newCards;

        foreach (var cardDto in snapshot.self.hand)
        {
            if (ulong.TryParse(cardDto.instanceId, out ulong id))
            {
                if (!keptCardIds.Contains(id))
                {
                    newCards.Add(cardDto);
                }
            }
        }
        return newCards;
    }

    private void RenderOpeningHand(MatchSnapshotDto snapshot)
    {
        if (snapshot.self?.hand == null || networkCardPrefab == null) return;

        if (!gameObject.activeInHierarchy)
        {
            gameObject.SetActive(true);
        }

        List<GameObject> spawnedCards = new List<GameObject>();

        foreach (CardViewDto cardDto in snapshot.self.hand)
        {
            GameObject cardObject = CreateCardView(cardDto, true);
            cardObject.transform.localScale = Vector3.zero;
            spawnedCards.Add(cardObject);
        }

        LayoutGroup layoutGroup = cardsContainer.GetComponent<LayoutGroup>();
        if (layoutGroup != null)
        {
            layoutGroup.enabled = true;
        }

        StartCoroutine(AnimateMulliganRoutine(spawnedCards, layoutGroup));
    }

    private GameObject CreateCardView(CardViewDto cardDto, bool addToggleComponent)
    {
        GameObject cardObject = Instantiate(networkCardPrefab, cardsContainer);
        NetworkCardVisual visual = cardObject.GetComponent<NetworkCardVisual>();

        if (visual != null)
        {
            CardVisualLayout layout = cardObject.GetComponent<CardVisualLayout>();
            if (layout != null)
            {
                layout.Apply(CardVisualLocation.Mulligan);
            }

            CardArtworkView artworkPrefab = presentationCatalog != null
                ? presentationCatalog.Find(cardDto.definitionId)
                : null;

            visual.BindFaceUp(cardDto, artworkPrefab);

            if (artworkPrefab == null)
            {
                Debug.LogWarning($"[Mulligan] Không tìm thấy artwork prefab cho lá bài: {cardDto.definitionId}", this);
            }
        }

        DraggableHandCard dragScript = cardObject.GetComponent<DraggableHandCard>();
        if (dragScript != null)
        {
            Destroy(dragScript);
        }

        if (addToggleComponent)
        {
            MulliganCardToggle toggle = cardObject.GetComponent<MulliganCardToggle>();
            if (toggle == null)
            {
                toggle = cardObject.AddComponent<MulliganCardToggle>();
            }

            if (ulong.TryParse(cardDto.instanceId, out ulong id))
            {
                toggle.cardInstanceId = id;
            }

            Transform overlayTransform = cardObject.transform.Find("ReplaceOverlay")
                ?? cardObject.transform.Find("VisualRoot/ReplaceOverlay");

            if (overlayTransform != null)
            {
                toggle.replaceOverlay = overlayTransform.GetComponent<Image>();
            }
        }
        else
        {
            MulliganCardToggle existingToggle = cardObject.GetComponent<MulliganCardToggle>();
            if (existingToggle != null) Destroy(existingToggle);
        }

        return cardObject;
    }

    private IEnumerator AnimateMulliganRoutine(List<GameObject> spawnedCards, LayoutGroup layoutGroup)
    {
        isAnimating = true;

        if (darkBackDrop != null) darkBackDrop.SetActive(false);
        if (topicGroup != null) topicGroup.SetActive(false);
        if (actionButton != null) actionButton.interactable = false;

        yield return new WaitForEndOfFrame();

        if (layoutGroup != null)
        {
            layoutGroup.enabled = false;
        }

        float totalDuration = 0f;

        for (int i = 0; i < spawnedCards.Count; i++)
        {
            if (spawnedCards[i] == null) continue;

            RectTransform cardRect = spawnedCards[i].GetComponent<RectTransform>();
            Vector3 targetLocalPos = cardRect.localPosition;

            if (deckSpawnPoint != null)
            {
                cardRect.position = deckSpawnPoint.position;
            }

            cardRect.localEulerAngles = new Vector3(0, 90f, 0);

            float delay = i * 0.3f;
            float animTime = 0.8f;

            Sequence dealSeq = DOTween.Sequence();
            dealSeq.Insert(delay, cardRect.DOLocalMove(targetLocalPos, animTime).SetEase(Ease.OutCubic));
            dealSeq.Insert(delay, cardRect.DOScale(Vector3.one, animTime).SetEase(Ease.OutBack));
            dealSeq.Insert(delay, cardRect.DOLocalRotate(Vector3.zero, animTime).SetEase(Ease.OutBack));
            totalDuration = Mathf.Max(totalDuration, delay + animTime);
        }

        yield return new WaitForSeconds(totalDuration);

        if (darkBackDrop != null) darkBackDrop.SetActive(true);
        if (topicGroup != null) topicGroup.SetActive(true);
        if (actionButton != null) actionButton.interactable = true;

        isAnimating = false;
    }

    #endregion

    #region Confirmation & Transitions

    private void OnConfirmClicked()
    {
        if (hasConfirmed || isAnimating) return;

        NetworkMatchBridge networkBridge = FindAnyObjectByType<NetworkMatchBridge>();
        if (networkBridge == null)
        {
            Debug.LogError("Không tìm thấy NetworkMatchBridge để gửi lệnh!", this);
            return;
        }

        selectedToReplace.Clear();
        keptCardIds.Clear();
        replacedSlotPositions.Clear();

        LayoutGroup layoutGroup = cardsContainer.GetComponent<LayoutGroup>();
        if (layoutGroup != null) layoutGroup.enabled = false;

        int replaceCount = 0;
        float maxReturnTime = 0f;

        foreach (Transform child in cardsContainer)
        {
            MulliganCardToggle toggle = child.GetComponent<MulliganCardToggle>();
            if (toggle == null) continue;

            toggle.LockAndClearOverlay();

            if (toggle.isSelected)
            {
                selectedToReplace.Add(toggle.cardInstanceId);

                RectTransform rect = child.GetComponent<RectTransform>();
                replacedSlotPositions.Add(rect.localPosition);

                GameObject cardToDestroy = child.gameObject;
                float delay = replaceCount * 0.15f;
                float animTime = 0.4f;

                Sequence returnSeq = DOTween.Sequence();
                if (deckSpawnPoint != null)
                {
                    returnSeq.Insert(delay, rect.DOMove(deckSpawnPoint.position, animTime).SetEase(Ease.InCubic));
                }
                returnSeq.Insert(delay, rect.DOScale(Vector3.zero, animTime).SetEase(Ease.InBack));
                returnSeq.Insert(delay, rect.DOLocalRotate(new Vector3(0, 90f, 0), animTime).SetEase(Ease.InBack));

                returnSeq.OnComplete(() =>
                {
                    if (cardToDestroy != null) Destroy(cardToDestroy);
                });

                maxReturnTime = Mathf.Max(maxReturnTime, delay + animTime);
                replaceCount++;
            }
            else
            {
                keptCardIds.Add(toggle.cardInstanceId);
                Destroy(toggle);
            }
        }

        hasConfirmed = true;
        hasMovedToHand = false;

        if (replaceCount == 0)
        {
            hasSpawnedNewCards = true;
            isReturningToDeck = false;
        }
        else
        {
            hasSpawnedNewCards = false;
            isReturningToDeck = true;
            StartCoroutine(TrackReturnToDeckRoutine(maxReturnTime));
        }

        if (actionButtonText != null) actionButtonText.text = "ĐANG CHỜ...";
        if (actionButton != null) actionButton.interactable = false;

        if (!networkBridge.RequestMulligan(selectedToReplace.ToArray()))
        {
            Debug.LogWarning("Không thể gửi yêu cầu đổi bài.", this);
            hasConfirmed = false;
            isReturningToDeck = false;
            if (actionButton != null) actionButton.interactable = true;
            return;
        }
    }

    private IEnumerator TrackReturnToDeckRoutine(float duration)
    {
        isReturningToDeck = true;
        yield return new WaitForSeconds(duration + 0.05f);
        isReturningToDeck = false;
    }

    private IEnumerator SpawnNewCardsFromDeckRoutine(List<CardViewDto> newCardsData)
    {
        isAnimating = true;

        while (isReturningToDeck)
        {
            yield return null;
        }

        float maxFlyInTime = 0f;
        int count = Mathf.Min(replacedSlotPositions.Count, newCardsData.Count);

        for (int i = 0; i < count; i++)
        {
            CardViewDto newData = newCardsData[i];
            Vector3 targetLocalPos = replacedSlotPositions[i];

            GameObject newCardObj = CreateCardView(newData, false);
            RectTransform cardRect = newCardObj.GetComponent<RectTransform>();

            if (deckSpawnPoint != null)
            {
                cardRect.position = deckSpawnPoint.position;
            }
            cardRect.localScale = Vector3.zero;
            cardRect.localEulerAngles = new Vector3(0, 90f, 0);

            float delay = i * 0.2f;
            float animTime = 0.5f;

            Sequence flyInSeq = DOTween.Sequence();
            flyInSeq.Insert(delay, cardRect.DOLocalMove(targetLocalPos, animTime).SetEase(Ease.OutCubic));
            flyInSeq.Insert(delay, cardRect.DOScale(Vector3.one, animTime).SetEase(Ease.OutBack));
            flyInSeq.Insert(delay, cardRect.DOLocalRotate(Vector3.zero, animTime).SetEase(Ease.OutBack));

            maxFlyInTime = Mathf.Max(maxFlyInTime, delay + animTime);
        }

        yield return new WaitForSeconds(maxFlyInTime);
        isAnimating = false;
    }

    private IEnumerator WaitBothConfirmedAndMoveToHandRoutine(float swapWaitDuration)
    {
        if (hasMovedToHand || isTransitioningToMatch) yield break;
        isTransitioningToMatch = true;

        float confirmedAtTime = Time.time;

        while (!hasSpawnedNewCards || isReturningToDeck || isAnimating)
        {
            yield return null;
        }

        float elapsedSwapTime = Time.time - confirmedAtTime;
        float remainingSwapWait = Mathf.Max(0f, swapWaitDuration - elapsedSwapTime);

        yield return new WaitForSeconds(remainingSwapWait + viewAfterSwapDelay);

        yield return StartCoroutine(AnimateCardsToHandRoutine());

        SetRealHandVisibility(true);
        DestroyMulliganCardObjects();

        hasMovedToHand = true;
        isTransitioningToMatch = false;
        gameObject.SetActive(false);
    }

    private IEnumerator WaitAndCloseMulliganRoutine(MatchSnapshotDto snapshot)
    {
        if (hasConfirmed && !hasSpawnedNewCards)
        {
            List<CardViewDto> newCardsData = ExtractNewCardsFromSnapshot(snapshot);
            if (newCardsData.Count > 0)
            {
                hasSpawnedNewCards = true;
                yield return StartCoroutine(SpawnNewCardsFromDeckRoutine(newCardsData));
            }
        }

        while (isReturningToDeck || isAnimating || isTransitioningToMatch)
        {
            yield return null;
        }

        if (!hasMovedToHand)
        {
            yield return StartCoroutine(WaitBothConfirmedAndMoveToHandRoutine(0f));
        }

        SetRealHandVisibility(true);
        if (gameObject.activeInHierarchy) gameObject.SetActive(false);
        ClearViews();
    }

    private IEnumerator AnimateCardsToHandRoutine()
    {
        if (darkBackDrop != null) darkBackDrop.SetActive(false);
        if (topicGroup != null) topicGroup.SetActive(false);

        if (cardsContainer == null || cardsContainer.childCount == 0)
        {
            yield break;
        }

        yield return new WaitForEndOfFrame();

        List<RectTransform> mulliganCards = new List<RectTransform>();
        foreach (Transform child in cardsContainer)
        {
            if (child != null && child.gameObject.activeSelf)
            {
                mulliganCards.Add(child.GetComponent<RectTransform>());
            }
        }
        mulliganCards.Sort((a, b) => a.localPosition.x.CompareTo(b.localPosition.x));

        List<Transform> handCardSlots = new List<Transform>();
        if (handContainer != null)
        {
            foreach (Transform child in handContainer)
            {
                if (child != null && child.gameObject.activeSelf && child.GetComponent<NetworkCardVisual>() != null)
                {
                    handCardSlots.Add(child);
                }
            }
        }

        float maxMoveTime = 0f;

        for (int i = 0; i < mulliganCards.Count; i++)
        {
            RectTransform cardRect = mulliganCards[i];
            if (cardRect == null) continue;

            Vector3 targetWorldPos = cardRect.position;
            if (i < handCardSlots.Count)
            {
                targetWorldPos = handCardSlots[i].position;
            }
            else if (handContainer != null)
            {
                targetWorldPos = handContainer.position;
            }

            float delay = i * 0.08f;
            float moveDuration = 0.4f;

            Sequence toHandSeq = DOTween.Sequence();
            toHandSeq.Insert(delay, cardRect.DOMove(targetWorldPos, moveDuration).SetEase(Ease.InOutCubic));
            toHandSeq.Insert(delay, cardRect.DOScale(Vector3.one * handCardScale, moveDuration).SetEase(Ease.InOutQuad));

            maxMoveTime = Mathf.Max(maxMoveTime, delay + moveDuration);
        }

        yield return new WaitForSeconds(maxMoveTime);
    }

    private void SetRealHandVisibility(bool visible)
    {
        hideRealHandCards = !visible;
        ApplyHandCardsAlpha(visible);
    }

    private void SetTopBarVisibility(bool visible)
    {
        if (topBar != null && topBar.activeSelf != visible)
        {
            topBar.SetActive(visible);
        }
    }

    private void ApplyHandCardsAlpha(bool visible)
    {
        if (handContainer == null) return;

        CanvasGroup containerGroup = handContainer.GetComponent<CanvasGroup>();
        if (containerGroup != null)
        {
            containerGroup.alpha = 1f;
            containerGroup.blocksRaycasts = true;
        }

        foreach (Transform child in handContainer)
        {
            if (child == null || child.GetComponent<NetworkCardVisual>() == null) continue;

            CanvasGroup cardGroup = child.GetComponent<CanvasGroup>();
            if (cardGroup == null)
            {
                cardGroup = child.gameObject.AddComponent<CanvasGroup>();
            }

            if (!visible)
            {
                if (!savedHandCardStates.ContainsKey(cardGroup))
                {
                    savedHandCardStates[cardGroup] = (cardGroup.alpha, cardGroup.blocksRaycasts);
                }

                cardGroup.alpha = 0f;
                cardGroup.blocksRaycasts = false;
            }
            else
            {
                if (savedHandCardStates.TryGetValue(cardGroup, out var savedState))
                {
                    cardGroup.alpha = savedState.alpha;
                    cardGroup.blocksRaycasts = savedState.blocksRaycasts;
                }
            }
        }

        if (visible)
        {
            savedHandCardStates.Clear();
        }
    }

    #endregion

    #region View Cleanup

    private void DestroyMulliganCardObjects()
    {
        if (cardsContainer != null)
        {
            foreach (Transform child in cardsContainer)
            {
                child.DOKill();
                Destroy(child.gameObject);
            }
        }
    }

    public void ClearViews()
    {
        DestroyMulliganCardObjects();

        hasConfirmed = false;
        bothPlayersConfirmed = false;
        isAnimating = false;
        isReturningToDeck = false;
        hasSpawnedNewCards = false;
        hasMovedToHand = false;
        isTransitioningToMatch = false;
        hideRealHandCards = false;
        selectedToReplace.Clear();
        keptCardIds.Clear();
        replacedSlotPositions.Clear();
        savedHandCardStates.Clear();
        if (myTurnText != null)
        {
            myTurnText.text = string.Empty;
        }
    }

    #endregion
}