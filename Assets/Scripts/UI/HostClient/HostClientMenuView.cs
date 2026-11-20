using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>Owns the waiting-room controls, join dialog and clipboard feedback.</summary>
    public sealed class HostClientMenuView : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private ListenSessionController controller;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button clientButton;
        [SerializeField] private Button backButton;
        [SerializeField] private GameObject joinModal;
        [SerializeField] private TMP_InputField joinInput;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button confirmButton;
        [SerializeField] private GameObject roomCodeGroup;
        [SerializeField] private TMP_InputField roomCode;
        [SerializeField] private Button copyButton;
        [SerializeField] private Image copyIcon;
        [SerializeField] private Sprite copySprite;
        [SerializeField] private Sprite checkedSprite;
        [SerializeField] private TMP_Text statusLabel;

        #endregion

        #region Runtime State

        private bool busy;
        private bool roomReady;
        private bool leaving;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            roomCode.readOnly = true;
            joinModal.SetActive(false);
            roomCodeGroup.SetActive(false);
            copyIcon.sprite = copySprite;
            statusLabel.text = string.Empty;
        }

        private void OnEnable()
        {
            hostButton.onClick.AddListener(controller.Host);
            clientButton.onClick.AddListener(OpenJoin);
            backButton.onClick.AddListener(controller.Leave);
            cancelButton.onClick.AddListener(CloseJoin);
            confirmButton.onClick.AddListener(ConfirmJoin);
            copyButton.onClick.AddListener(CopyRoomCode);
            joinInput.onSubmit.AddListener(SubmitJoin);
            RefreshControls();
        }

        private void OnDisable()
        {
            hostButton.onClick.RemoveListener(controller.Host);
            clientButton.onClick.RemoveListener(OpenJoin);
            backButton.onClick.RemoveListener(controller.Leave);
            cancelButton.onClick.RemoveListener(CloseJoin);
            confirmButton.onClick.RemoveListener(ConfirmJoin);
            copyButton.onClick.RemoveListener(CopyRoomCode);
            joinInput.onSubmit.RemoveListener(SubmitJoin);
        }

        #endregion

        #region Public API

        public void SetBusy(bool value)
        {
            busy = value;
            RefreshControls();
        }

        public void SetLeaving(bool value)
        {
            leaving = value;
            RefreshControls();
        }

        public void ShowRoomCode(string code)
        {
            roomReady = true;
            roomCode.SetTextWithoutNotify(code);
            copyIcon.sprite = copySprite;
            roomCodeGroup.SetActive(true);
            RefreshControls();
        }

        public void HideRoomCode()
        {
            roomReady = false;
            roomCode.SetTextWithoutNotify(string.Empty);
            copyIcon.sprite = copySprite;
            roomCodeGroup.SetActive(false);
            RefreshControls();
        }

        #endregion

        #region Dialog And Clipboard

        private void OpenJoin()
        {
            if (busy || leaving || roomReady) return;
            joinModal.SetActive(true);
            RefreshControls();
            EventSystem.current?.SetSelectedGameObject(joinInput.gameObject);
            joinInput.ActivateInputField();
        }

        private void CloseJoin()
        {
            joinModal.SetActive(false);
            RefreshControls();
            EventSystem.current?.SetSelectedGameObject(clientButton.gameObject);
        }

        private void ConfirmJoin()
        {
            if (!joinModal.activeSelf || busy || leaving) return;
            joinInput.SetTextWithoutNotify(joinInput.text.Trim());
            CloseJoin();
            controller.Join();
        }

        private void SubmitJoin(string value)
        {
            ConfirmJoin();
        }

        private void CopyRoomCode()
        {
            if (!roomReady || leaving || string.IsNullOrEmpty(roomCode.text)) return;
            try
            {
                GUIUtility.systemCopyBuffer = roomCode.text;
                bool copied = string.Equals(
                    GUIUtility.systemCopyBuffer, roomCode.text, StringComparison.Ordinal);
                copyIcon.sprite = copied ? checkedSprite : copySprite;
                if (!copied) ShowCopyFailure();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[HostClient] Clipboard unavailable: {exception.Message}");
                copyIcon.sprite = copySprite;
                ShowCopyFailure();
            }
        }

        private void ShowCopyFailure()
        {
            statusLabel.text = "Chưa thể sao chép. Hãy bôi đen mã và nhấn Ctrl+C.";
        }

        private void RefreshControls()
        {
            bool modalOpen = joinModal.activeSelf;
            hostButton.interactable = !busy && !leaving && !roomReady && !modalOpen;
            clientButton.interactable = !busy && !leaving && !roomReady && !modalOpen;
            backButton.interactable = !leaving && !modalOpen;
            copyButton.interactable = roomReady && !leaving && !modalOpen;
            roomCode.interactable = roomReady && !leaving && !modalOpen;
        }

        #endregion
    }
}
