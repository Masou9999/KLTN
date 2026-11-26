using System;
using System.Runtime.InteropServices;
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
#if UNITY_WEBGL && !UNITY_EDITOR
        private bool copyArmed;
#endif

        #endregion

#if UNITY_WEBGL && !UNITY_EDITOR
        #region Browser Clipboard

        // GUIUtility.systemCopyBuffer only fills Unity's internal buffer on WebGL.
        [DllImport("__Internal")]
        private static extern void KltnClipboardArm(string text, string target, string method);

        [DllImport("__Internal")]
        private static extern void KltnClipboardCopy(string text, string target, string method);

        #endregion
#endif

        #region Unity Lifecycle

        private void Awake()
        {
            roomCode.readOnly = true;
            joinModal.SetActive(false);
            roomCodeGroup.SetActive(false);
            copyIcon.sprite = copySprite;
            statusLabel.text = string.Empty;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Arm on pointer-down so the browser write runs inside its own pointerup
            // gesture; a Unity Button click arrives a frame later, outside the gesture.
            var trigger = copyButton.gameObject.AddComponent<EventTrigger>();
            var press = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            press.callback.AddListener(_ => ArmCopy());
            trigger.triggers.Add(press);
#endif
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

        private bool CanCopy => roomReady && !leaving && !joinModal.activeSelf
            && copyButton.interactable && !string.IsNullOrEmpty(roomCode.text);

        private void ArmCopy()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!CanCopy) return;
            copyArmed = true;
            KltnClipboardArm(roomCode.text, gameObject.name, nameof(OnClipboardResult));
#endif
        }

        /// <summary>Receives the browser clipboard result ("1" copied, "0" refused).</summary>
        public void OnClipboardResult(string result)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            copyArmed = false;
#endif
            bool copied = result == "1";
            copyIcon.sprite = copied ? checkedSprite : copySprite;
            if (!copied) ShowCopyFailure();
        }

        private void CopyRoomCode()
        {
            if (!roomReady || leaving || string.IsNullOrEmpty(roomCode.text)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Pointer clicks were already armed; keyboard activation copies directly.
            if (!copyArmed)
                KltnClipboardCopy(roomCode.text, gameObject.name, nameof(OnClipboardResult));
#else
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
#endif
        }

        private void ShowCopyFailure()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Selecting the code and pressing Ctrl+C also stays inside Unity on WebGL.
            statusLabel.text = "Trình duyệt chặn sao chép. Hãy gửi mã phòng hiển thị ở trên.";
#else
            statusLabel.text = "Chưa thể sao chép. Hãy bôi đen mã và nhấn Ctrl+C.";
#endif
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
