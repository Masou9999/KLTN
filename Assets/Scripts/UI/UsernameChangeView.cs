using System;
using KLTN.Game.Domain.Ranking;
using KLTN.Infrastructure.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>MainMenu rename button and confirmation modal; assets are replaceable in the Inspector.</summary>
    public sealed class UsernameChangeView : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private Button editButton;
        [SerializeField] private GameObject modal;
        [SerializeField] private TMP_InputField input;
        [SerializeField] private TMP_Text feedback;
        [SerializeField] private Button accept;
        [SerializeField] private Button cancel;
        [SerializeField] private TMP_Text acceptLabel;
        [SerializeField] private GameObject confirmationModal;
        [SerializeField] private TMP_Text confirmationText;
        [SerializeField] private Button confirm;
        [SerializeField] private Button back;

        #endregion

        #region Runtime State

        private PlayerProfileService profiles;
        private bool busy;
        private Color feedbackColor;
        private Color confirmationColor;
        private bool colorsCaptured;
        private bool renamedThisOpening;
        private string confirmedName;
        private string openedUid;
        private int generation;

        #endregion

        #region Construction

        /// <summary>Used by both the installer and runtime; authored controls are reused.</summary>
        public static void Attach(TMP_Text username)
        {
            var view = username.transform.parent.GetComponent<UsernameChangeView>();
            if (view == null)
                view = username.transform.parent.gameObject.AddComponent<UsernameChangeView>();
            view.Initialize(username);
        }

        private void Initialize(TMP_Text username)
        {
            if (editButton == null)
                editButton = transform.Find("ChangeUsername")?.GetComponent<Button>();
            if (editButton == null)
            {
                username.rectTransform.sizeDelta = new Vector2(302, 48);
                editButton = MakeButton(transform, "ChangeUsername", "", username.font,
                    new Vector2(608, -109), new Vector2(40, 40), out _);
                var rect = (RectTransform)editButton.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
                var icon = Box(editButton.transform, "Icon", new Color(1, 1, 1, 1),
                    Vector2.zero, new Vector2(30, 30));
                icon.sprite = Resources.Load<Sprite>("UI/ChangeUsername");
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                editButton.GetComponent<Image>().color = new Color(.94f, .88f, .72f);
            }
            if (modal == null)
            {
                Transform canvas = username.GetComponentInParent<Canvas>().rootCanvas.transform;
                var overlay = Box(canvas, "UserNameChangeModal", new Color(0, 0, 0, .8f),
                    Vector2.zero, Vector2.zero);
                var rect = overlay.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                modal = overlay.gameObject;
                var panel = Box(modal.transform, "Panel", new Color(.12f, .13f, .16f),
                    Vector2.zero, new Vector2(640, 360));
                Label(panel.transform, "Title", "Đổi tên người chơi", username.font,
                    new Vector2(0, 137), new Vector2(560, 48), 30);
                var field = Box(panel.transform, "UsernameInput", new Color(.22f, .23f, .27f),
                    new Vector2(0, 63), new Vector2(550, 54));
                var text = Label(field.transform, "Text", "", username.font,
                    Vector2.zero, new Vector2(520, 48), 27);
                text.alignment = TextAlignmentOptions.MidlineLeft;
                var placeholder = Label(field.transform, "Placeholder", "Nhập tên mới",
                    username.font, Vector2.zero, new Vector2(520, 48), 27);
                placeholder.alignment = TextAlignmentOptions.MidlineLeft;
                placeholder.color = Color.gray;
                input = field.gameObject.AddComponent<TMP_InputField>();
                input.textViewport = field.rectTransform;
                input.textComponent = text;
                input.placeholder = placeholder;
                input.characterLimit = 0;
                input.lineType = TMP_InputField.LineType.SingleLine;
                input.contentType = TMP_InputField.ContentType.Standard;
                feedback = Label(panel.transform, "Feedback", "Bạn còn 1 lần đổi tên", username.font,
                    new Vector2(0, -26), new Vector2(550, 105), 23);
                accept = MakeButton(panel.transform, "Accept", "Xác Nhận", username.font,
                    new Vector2(0, -130), new Vector2(250, 52), out acceptLabel);
                cancel = MakeButton(panel.transform, "Exit", "×", username.font,
                    new Vector2(285, 145), new Vector2(44, 44), out _);
                modal.SetActive(false);
            }
            if (confirmationModal == null)
            {
                var overlay = Box(modal.transform.parent, "UserNameConfirmationModal",
                    new Color(0, 0, 0, .8f), Vector2.zero, Vector2.zero);
                overlay.rectTransform.anchorMin = Vector2.zero;
                overlay.rectTransform.anchorMax = Vector2.one;
                confirmationModal = overlay.gameObject;
                var panel = Box(overlay.transform, "Panel", new Color(.12f, .13f, .16f),
                    Vector2.zero, new Vector2(640, 360));
                confirmationText = Label(panel.transform, "ConfirmationText", "",
                    username.font, new Vector2(0, 40), new Vector2(550, 180), 26);
                confirm = MakeButton(panel.transform, "Confirm", "Xác Nhận", username.font,
                    new Vector2(145, -110), new Vector2(250, 52), out _);
                confirm.GetComponent<Image>().color = accept.GetComponent<Image>().color;
                back = MakeButton(panel.transform, "Back", "Hủy", username.font,
                    new Vector2(-145, -110), new Vector2(250, 52), out _);
                confirmationModal.SetActive(false);
            }
            if (Application.isPlaying) BindControls();
        }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            profiles = PlayerProfileService.Instance;
            if (profiles != null) profiles.ProfileChanged += Render;
            BindControls();
        }

        private void OnDisable()
        {
            ++generation;
            if (profiles != null) profiles.ProfileChanged -= Render;
            if (modal != null) modal.SetActive(false);
            if (confirmationModal != null) confirmationModal.SetActive(false);
        }

        private void BindControls()
        {
            if (editButton == null || input == null || confirmationModal == null) return;
            if (!colorsCaptured)
            {
                feedbackColor = feedback.color;
                confirmationColor = confirmationText.color;
                colorsCaptured = true;
            }
            editButton.onClick.RemoveListener(Open);
            editButton.onClick.AddListener(Open);
            accept.onClick.RemoveListener(ShowConfirmation);
            accept.onClick.AddListener(ShowConfirmation);
            confirm.onClick.RemoveListener(Submit);
            confirm.onClick.AddListener(Submit);
            back.onClick.RemoveListener(Back);
            back.onClick.AddListener(Back);
            cancel.onClick.RemoveListener(Close);
            cancel.onClick.AddListener(Close);
            input.onValueChanged.RemoveListener(Edited);
            input.onValueChanged.AddListener(Edited);
            Render(profiles?.Current);
        }

        #endregion

        #region Commands

        private void Render(PlayerProfile profile)
        {
            if (editButton != null)
                editButton.interactable = !busy && profile != null;
            if (modal != null && (modal.activeSelf || confirmationModal.activeSelf)
                && profile?.UserId != openedUid)
            {
                ++generation;
                modal.SetActive(false);
                confirmationModal.SetActive(false);
                return;
            }
            if (input == null || profile == null) return;
            bool alreadyChanged = !profile.CanChangeUsername;
            input.readOnly = alreadyChanged;
            input.interactable = !busy;
            accept.interactable = !busy && !alreadyChanged
                && !string.IsNullOrWhiteSpace(input.text);
            confirm.interactable = !busy && !alreadyChanged && confirmedName != null;
            back.interactable = !busy;
            cancel.interactable = !busy;
            if (alreadyChanged)
            {
                if (confirmationModal.activeSelf && !busy)
                {
                    confirmationModal.SetActive(false);
                    modal.SetActive(true);
                }
                input.SetTextWithoutNotify(profile.Username);
                SetFeedback(renamedThisOpening
                    ? "Đã đổi tên thành công"
                    : "Bạn đã đổi tên rồi");
            }
        }

        private void Open()
        {
            if (profiles?.Current == null || busy) return;
            openedUid = profiles.Current.UserId;
            renamedThisOpening = false;
            confirmedName = null;
            input.SetTextWithoutNotify("");
            SetFeedback("Bạn còn 1 lần đổi tên");
            acceptLabel.text = "Xác Nhận";
            confirmationModal.SetActive(false);
            modal.transform.SetAsLastSibling();
            modal.SetActive(true);
            Render(profiles.Current);
            if (!input.readOnly) input.ActivateInputField();
        }

        private void Close()
        {
            if (!busy) modal.SetActive(false);
        }

        private void Edited(string value)
        {
            confirmedName = null;
            SetFeedback("Bạn còn 1 lần đổi tên");
            Render(profiles?.Current);
        }

        private async void ShowConfirmation()
        {
            if (busy || profiles?.Current?.UserId != openedUid
                || !profiles.Current.CanChangeUsername
                || string.IsNullOrWhiteSpace(input.text)) return;
            string name = UsernamePolicy.DisplayName(input.text);
            string error = UsernamePolicy.ValidateChoice(name);
            if (error != null)
            {
                SetFeedback(Message(error), true);
                return;
            }
            int request = generation;
            busy = true;
            Render(profiles.Current);
            SetFeedback("Đang kiểm tra tên…");
            try
            {
                error = await profiles.CheckUsernameAsync(name);
                if (this == null || request != generation ||
                    profiles.Current?.UserId != openedUid || !modal.activeSelf) return;
                if (error != null)
                {
                    SetFeedback(Message(error), true);
                    return;
                }
                if (!profiles.Current.CanChangeUsername) return;
                confirmedName = name;
                SetFeedback("Bạn còn 1 lần đổi tên");
                confirmationText.color = confirmationColor;
                confirmationText.text = $"Tên của bạn sẽ được đổi thành “{name}”.\n"
                    + "Sau khi xác nhận, bạn không thể đổi tên lần nữa.";
                modal.SetActive(false);
                confirmationModal.transform.SetAsLastSibling();
                confirmationModal.SetActive(true);
            }
            finally
            {
                if (this != null)
                {
                    busy = false;
                    Render(profiles?.Current);
                }
            }
        }

        private void Back()
        {
            if (busy) return;
            confirmationModal.SetActive(false);
            modal.SetActive(true);
            confirmedName = null;
            Render(profiles?.Current);
        }

        private async void Submit()
        {
            if (busy || profiles?.Current?.UserId != openedUid
                || !profiles.Current.CanChangeUsername || confirmedName == null
                || !confirmationModal.activeSelf) return;
            string name = confirmedName;
            int request = generation;
            busy = true;
            input.interactable = accept.interactable = cancel.interactable = false;
            Render(profiles.Current);
            confirmationText.color = confirmationColor;
            confirmationText.text = "Đang đổi tên…";
            try
            {
                var result = await profiles.ChangeUsernameAsync(name);
                if (this == null || request != generation || profiles.Current?.UserId != openedUid)
                    return;
                if (result.Success)
                {
                    renamedThisOpening = true;
                    confirmationModal.SetActive(false);
                    modal.SetActive(true);
                    Render(profiles.Current);
                    SetFeedback("Đã đổi tên thành công");
                }
                else if (result.Error == "network_unknown")
                {
                    confirmationText.color = Color.red;
                    confirmationText.text = Message(result.Error);
                }
                else
                {
                    confirmationModal.SetActive(false);
                    modal.SetActive(true);
                    SetFeedback(Message(result.Error), true);
                }
            }
            finally
            {
                if (this != null)
                {
                    busy = false;
                    input.interactable = accept.interactable = cancel.interactable = true;
                    Render(profiles?.Current);
                }
            }
        }

        private void SetFeedback(string message, bool error = false)
        {
            feedback.text = message;
            feedback.color = error ? Color.red : feedbackColor;
        }

        private static string Message(string error)
        {
            switch (error)
            {
                case "name_taken": return "Tên đã được sử dụng";
                case "already_changed": return "Bạn đã đổi tên rồi";
                case "reserved_name":
                case "invalid_name": return "Tên không hợp lệ";
                case "invalid_length": return "Tên quá ký tự cho phép";
                case "special_characters": return "Tên không thể chứa ký tự đặc biệt";
                case "availability_unavailable": return "Chưa kiểm tra được tên. Vui lòng thử lại.";
                case "request_id_conflict": return "Yêu cầu không khớp tên. Hãy đóng và thử lại.";
                case "network_unknown": return "Chưa nhận được kết quả. Bấm xác nhận lần nữa để kiểm tra cùng yêu cầu.";
                case "season_unavailable": return "Chưa tải được mùa giải. Vui lòng thử lại sau.";
                default: return "Không đổi được tên. Vui lòng tải lại hồ sơ và thử lại.";
            }
        }

        #endregion

        #region UI Helpers

        private static Image Box(Transform parent, string name, Color color,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax =
                image.rectTransform.pivot = new Vector2(.5f, .5f);
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            return image;
        }

        private static TextMeshProUGUI Label(Transform parent, string name, string value,
            TMP_FontAsset font, Vector2 position, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = new Color(.96f, .92f, .82f);
            text.alignment = TextAlignmentOptions.Center;
            text.richText = false;
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = size;
            text.rectTransform.anchoredPosition = position;
            return text;
        }

        private static Button MakeButton(Transform parent, string name, string value,
            TMP_FontAsset font, Vector2 position, Vector2 size, out TMP_Text label)
        {
            var image = Box(parent, name, new Color(.32f, .28f, .2f), position, size);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            label = Label(image.transform, "Label", value, font,
                Vector2.zero, size - new Vector2(12, 4), 24);
            return button;
        }

        #endregion
    }
}
