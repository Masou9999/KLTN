using System.Collections.Generic;
using KLTN.Game.Content;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MulliganPresenter : MonoBehaviour
{
    #region Serialized Fields

    [Header("UI References")]
    [SerializeField]
    private Transform cardsContainer;

    [SerializeField]
    private GameObject networkCardPrefab;

    [SerializeField]
    private CardPresentationCatalog presentationCatalog;

    [Header("Action Button Override")]
    [SerializeField]
    private Button actionButton;

    [SerializeField]
    private TextMeshProUGUI actionButtonText;

    #endregion

    #region Runtime State

    private readonly List<ulong> selectedToReplace = new List<ulong>();
    private bool hasConfirmed;

    #endregion

    #region Unity Lifecycle

    private void OnDisable()
    {
        if (actionButton != null)
        {
            actionButton.onClick.RemoveListener(OnConfirmClicked);
        }
    }

    #endregion

    #region Mulligan Rendering

    public void UpdateMulliganState(MatchSnapshotDto snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        // The server can keep this hand automatically when its own mulligan clock
        // expires, while the opponent still has time left. Do not offer a second commit.
        if (snapshot.roundNumber == 0 && !string.IsNullOrEmpty(snapshot.matchId)
            && snapshot.turnClock != null && snapshot.turnClock.self == null)
        {
            hasConfirmed = true;
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
            gameObject.SetActive(true);

            if (actionButtonText != null)
            {
                actionButtonText.text = "XÁC NHẬN";
            }

            if (actionButton != null)
            {
                actionButton.onClick.RemoveListener(OnConfirmClicked);
                actionButton.onClick.AddListener(OnConfirmClicked);
                actionButton.interactable = true;
            }

            if (cardsContainer != null && cardsContainer.childCount == 0)
            {
                RenderOpeningHand(snapshot);
            }
        }
        else if (snapshot.roundNumber > 0)
        {
            if (actionButton != null)
            {
                actionButton.onClick.RemoveListener(OnConfirmClicked);
                actionButton.interactable = true;
            }

            gameObject.SetActive(false);
        }
    }

    private void RenderOpeningHand(MatchSnapshotDto snapshot)
    {
        if (snapshot.self?.hand == null || networkCardPrefab == null)
        {
            return;
        }

        foreach (CardViewDto cardDto in snapshot.self.hand)
        {
            GameObject cardObject = Instantiate(networkCardPrefab, cardsContainer);
            NetworkCardVisual visual = cardObject.GetComponent<NetworkCardVisual>();

            if (visual != null)
            {
                CardVisualLayout layout = cardObject.GetComponent<CardVisualLayout>();

                if (layout != null)
                {
                    layout.Apply(CardVisualLocation.Hand);
                }

                CardArtworkView artworkPrefab =
                    presentationCatalog != null
                        ? presentationCatalog.Find(cardDto.definitionId)
                        : null;

                visual.BindFaceUp(cardDto, artworkPrefab);

                if (artworkPrefab == null)
                {
                    Debug.LogWarning(
                        $"[Mulligan] Không tìm thấy artwork prefab cho lá bài: {cardDto.definitionId}",
                        this
                    );
                }
            }

            DraggableHandCard dragScript = cardObject.GetComponent<DraggableHandCard>();

            if (dragScript != null)
            {
                Destroy(dragScript);
            }

            MulliganCardToggle toggle = cardObject.GetComponent<MulliganCardToggle>();

            if (toggle == null)
            {
                toggle = cardObject.AddComponent<MulliganCardToggle>();
            }
            if (ulong.TryParse(cardDto.instanceId, out ulong id))
            {
                toggle.cardInstanceId = id;
            }

            Transform overlayTransform =
                cardObject.transform.Find("ReplaceOverlay")
                ?? cardObject.transform.Find("VisualRoot/ReplaceOverlay");

            if (overlayTransform != null)
            {
                toggle.replaceOverlay = overlayTransform.GetComponent<Image>();
            }
        }
    }

    #endregion

    #region Confirmation

    private void OnConfirmClicked()
    {
        if (hasConfirmed)
        {
            return;
        }

        selectedToReplace.Clear();

        foreach (Transform child in cardsContainer)
        {
            MulliganCardToggle toggle = child.GetComponent<MulliganCardToggle>();

            if (toggle != null)
            {
                if (toggle.isSelected) { selectedToReplace.Add(toggle.cardInstanceId); }
                Destroy(toggle); 
            }
        }

        Debug.Log($"Số lượng bài muốn đổi: {selectedToReplace.Count}");

        NetworkMatchBridge networkBridge = FindAnyObjectByType<NetworkMatchBridge>();

        if (networkBridge == null)
        {
            Debug.LogError("Không tìm thấy NetworkMatchBridge để gửi lệnh!", this);
            return;
        }

        if (!networkBridge.RequestMulligan(selectedToReplace.ToArray()))
        {
            Debug.LogWarning("Không thể gửi yêu cầu đổi bài.", this);
            return;
        }

        hasConfirmed = true;

        if (actionButtonText != null)
        {
            actionButtonText.text = "ĐANG CHỜ...";
        }
        if (actionButton != null)
        {
            actionButton.interactable = false;
        }
    }

    #endregion
}

