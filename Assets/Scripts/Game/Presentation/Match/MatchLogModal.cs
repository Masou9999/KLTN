using System.Collections.Generic;
using KLTN.Game.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    public sealed class MatchLogModal : MonoBehaviour
    {
        [SerializeField] private Button openButton;
        [SerializeField] private GameObject modalRoot;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button backdropButton;
        [SerializeField] private Button[] filters;
        [SerializeField] private Button latestButton;
        [SerializeField] private MatchLogScrollRect scroll;
        [SerializeField] private TMP_Text emptyLabel;
        [SerializeField] private TMP_FontAsset font;
        private readonly List<TextMeshProUGUI> rows = new List<TextMeshProUGUI>();
        private MatchJournalClient journal;
        private int filter;
        private bool dirty = true;
        private bool followLatest = true;
        private Vector2 previousViewportSize;
        private GameObject previousSelection;
        private long latestVisibleSequence;

        public bool IsOpen => modalRoot != null && modalRoot.activeSelf;

        private void Awake()
        {
            openButton.onClick.AddListener(Show);
            closeButton.onClick.AddListener(Hide);
            backdropButton.onClick.AddListener(Hide);
            latestButton.onClick.AddListener(JumpToLatest);
            for (int i = 0; i < filters.Length; i++)
            {
                int index = i;
                filters[i].onClick.AddListener(() => SetFilter(index));
            }
            scroll.onValueChanged.AddListener(_ =>
            {
                if (dirty) return;
                followLatest = !scroll.vertical || scroll.verticalNormalizedPosition <= .001f;
                if (followLatest) latestButton.gameObject.SetActive(false);
            });
            // Explicit navigation keeps keyboard/gamepad selection inside the open modal.
            var controls = new Selectable[] { closeButton, filters[0], filters[1], filters[2], scroll.verticalScrollbar, latestButton };
            for (int i = 0; i < controls.Length; i++)
                controls[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnLeft = controls[(i + controls.Length - 1) % controls.Length],
                    selectOnRight = controls[(i + 1) % controls.Length],
                    selectOnUp = closeButton, selectOnDown = filters[0] };
            modalRoot.SetActive(false);
        }

        private void OnEnable()
        {
            journal = MatchUpdateInboxRegistry.Current.Journal;
            journal.Changed += OnLogsChanged;
            dirty = true;
        }

        private void OnDisable()
        {
            if (journal != null) journal.Changed -= OnLogsChanged;
        }

        private void Update()
        {
            if (IsOpen && UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame) Hide();
        }

        private void LateUpdate()
        {
            if (!IsOpen) return;
            if (previousViewportSize != scroll.viewport.rect.size) dirty = true;
            if (dirty) Render();
        }

        public void Show()
        {
            previousSelection = EventSystem.current?.currentSelectedGameObject;
            modalRoot.SetActive(true);
            filter = 0;
            followLatest = true;
            dirty = true;
            Canvas.ForceUpdateCanvases();
            Render();
            EventSystem.current?.SetSelectedGameObject(closeButton.gameObject);
        }

        public void Hide()
        {
            modalRoot.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(previousSelection != null ? previousSelection : openButton.gameObject);
        }

        private void SetFilter(int value)
        {
            filter = value;
            followLatest = true;
            dirty = true;
            latestButton.gameObject.SetActive(false);
        }

        private void OnLogsChanged()
        {
            // Preserve the pixel offset when the reader is looking at older entries.
            if (!dirty) followLatest = !scroll.vertical || scroll.verticalNormalizedPosition <= .001f;
            long newest = 0;
            foreach (var entry in journal.Entries)
                if (filter == 0 || entry.category == filter - 1) newest = entry.sequence;
            if (newest < latestVisibleSequence) followLatest = true;
            dirty = true;
            if (IsOpen && !followLatest && newest > latestVisibleSequence)
                latestButton.gameObject.SetActive(true);
        }

        public void JumpToLatest()
        {
            followLatest = true;
            if (dirty) Render();
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 0;
            latestButton.gameObject.SetActive(false);
        }

        private void Render()
        {
            if (journal == null) return;
            float oldOffset = scroll.content.anchoredPosition.y;
            float width = Mathf.Max(80, scroll.viewport.rect.width - 24);
            float y = 12;
            int count = 0;
            latestVisibleSequence = 0;
            foreach (var log in journal.Entries)
            {
                if (filter != 0 && log.category != filter - 1) continue;
                if (count == rows.Count)
                {
                    var row = Label("LogEntry", scroll.content, font, 25, TextAlignmentOptions.TopLeft);
                    row.rectTransform.anchorMin = new Vector2(0, 1);
                    row.rectTransform.anchorMax = new Vector2(1, 1);
                    row.rectTransform.pivot = new Vector2(.5f, 1);
                    row.richText = true;
                    rows.Add(row);
                }
                latestVisibleSequence = log.sequence;
                var text = rows[count++];
                text.gameObject.SetActive(true);
                string tag = log.category == 0 ? "<color=#83D9A0>[Kỹ năng]</color>" : "<color=#F3B477>[Giao tranh]</color>";
                string body = log.text.Replace("<", "‹").Replace(">", "›");
                text.text = $"<size=80%><color=#A69DA4>Vòng {log.round}</color></size>  {tag}  {body}";
                float height = Mathf.Ceil(text.GetPreferredValues(text.text, width, Mathf.Infinity).y) + 12;
                text.rectTransform.sizeDelta = new Vector2(-24, height);
                text.rectTransform.anchoredPosition = new Vector2(0, -y);
                y += height + 16;
            }
            for (int i = count; i < rows.Count; i++) rows[i].gameObject.SetActive(false);
            emptyLabel.gameObject.SetActive(count == 0);
            emptyLabel.text = filter == 0 ? "Chưa có diễn biến được ghi lại." :
                filter == 1 ? "Chưa có log kỹ năng." : "Chưa có log giao tranh.";
            float viewportHeight = scroll.viewport.rect.height;
            float contentHeight = Mathf.Max(viewportHeight, y);
            scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
            scroll.StopMovement();
            scroll.vertical = contentHeight > viewportHeight + .5f;
            scroll.verticalScrollbar.gameObject.SetActive(scroll.vertical);
            if (followLatest) scroll.verticalNormalizedPosition = 0;
            else scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(oldOffset, 0, contentHeight - viewportHeight));
            scroll.Rebuild(CanvasUpdate.PostLayout);
            if (followLatest || !scroll.vertical) latestButton.gameObject.SetActive(false);
            for (int i = 0; i < filters.Length; i++)
            {
                filters[i].GetComponent<Image>().color = i == filter ? new Color(.32f, .19f, .23f) : new Color(.13f, .12f, .15f);
                filters[i].GetComponentInChildren<TMP_Text>().color = i == filter ? new Color(1, .91f, .79f) : new Color(.72f, .69f, .72f);
            }
            previousViewportSize = scroll.viewport.rect.size;
            dirty = false;
        }

        /// <summary>Called by the editor installer; all permanent controls are serialized into GameScene.</summary>
        public static MatchLogModal Build(Button settingsButton, TMP_FontAsset fontAsset)
        {
            var root = Rect("MatchLogUI", settingsButton.GetComponentInParent<Canvas>().rootCanvas.transform);
            Stretch(root);
            var modal = root.gameObject.AddComponent<MatchLogModal>();
            modal.font = fontAsset;
            var settingsRect = (RectTransform)settingsButton.transform;
            var launch = Rect("LogButton", settingsButton.transform.parent);
            launch.anchorMin = settingsRect.anchorMin; launch.anchorMax = settingsRect.anchorMax;
            launch.pivot = settingsRect.pivot; launch.sizeDelta = settingsRect.sizeDelta;
            launch.anchoredPosition = settingsRect.anchoredPosition - new Vector2(settingsRect.sizeDelta.x + 18, 0);
            launch.localScale = settingsRect.localScale;
            var icon = launch.gameObject.AddComponent<MatchLogIcon>();
            icon.color = new Color(.81f, .77f, .77f);
            modal.openButton = launch.gameObject.AddComponent<Button>();
            modal.openButton.targetGraphic = icon;
            modal.openButton.colors = settingsButton.colors;
            modal.openButton.navigation = new Navigation { mode = Navigation.Mode.None };

            var overlay = Rect("LogModal", root);
            Stretch(overlay);
            var canvas = overlay.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = 200;
            overlay.gameObject.AddComponent<GraphicRaycaster>();
            modal.modalRoot = overlay.gameObject;
            var backdrop = Rect("Backdrop", overlay); Stretch(backdrop);
            backdrop.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .72f);
            modal.backdropButton = backdrop.gameObject.AddComponent<Button>();
            modal.backdropButton.transition = Selectable.Transition.None;
            modal.backdropButton.navigation = new Navigation { mode = Navigation.Mode.None };
            var panel = Rect("Panel", overlay);
            panel.anchorMin = new Vector2(.16f, .1f); panel.anchorMax = new Vector2(.84f, .9f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = new Color(.10f, .08f, .105f, .99f);
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.51f, .34f, .34f); outline.effectDistance = new Vector2(2, -2);
            var title = Label("Title", panel, fontAsset, 36, TextAlignmentOptions.MidlineLeft);
            Top(title.rectTransform, 28, 18, -100, 58); title.text = "NHẬT KÝ TRẬN ĐẤU";
            modal.closeButton = TextButton("Close", panel, "×", fontAsset);
            var closeRect = (RectTransform)modal.closeButton.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1, 1);
            closeRect.pivot = new Vector2(1, 1); closeRect.anchoredPosition = new Vector2(-18, -18); closeRect.sizeDelta = new Vector2(58, 58);

            modal.filters = new Button[3];
            string[] names = { "Tất cả", "Kỹ năng", "Giao tranh" };
            for (int i = 0; i < 3; i++)
            {
                var button = TextButton("Filter" + i, panel, names[i], fontAsset);
                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(i / 3f, 1); rect.anchorMax = new Vector2((i + 1) / 3f, 1);
                rect.pivot = new Vector2(.5f, 1); rect.sizeDelta = new Vector2(-24, 54); rect.anchoredPosition = new Vector2(0, -94);
                modal.filters[i] = button;
            }
            var list = Rect("LogScroll", panel); Stretch(list);
            list.offsetMin = new Vector2(24, 24); list.offsetMax = new Vector2(-24, -172);
            list.gameObject.AddComponent<Image>().color = new Color(.065f, .055f, .075f, .7f);
            modal.scroll = list.gameObject.AddComponent<MatchLogScrollRect>();
            modal.scroll.horizontal = false; modal.scroll.movementType = ScrollRect.MovementType.Clamped;
            modal.scroll.inertia = false; modal.scroll.scrollSensitivity = 45;
            var viewport = Rect("Viewport", list); Stretch(viewport);
            viewport.offsetMax = new Vector2(-22, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            modal.scroll.viewport = viewport; modal.scroll.content = content;
            var track = Rect("Scrollbar", list);
            track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one;
            track.pivot = new Vector2(1, .5f); track.sizeDelta = new Vector2(12, -8); track.anchoredPosition = new Vector2(-4, 0);
            track.gameObject.AddComponent<Image>().color = new Color(.18f, .15f, .19f);
            var handle = Rect("Handle", track); Stretch(handle);
            var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = new Color(.65f, .52f, .54f);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            modal.scroll.verticalScrollbar = scrollbar;
            modal.scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            modal.emptyLabel = Label("Empty", viewport, fontAsset, 26, TextAlignmentOptions.Center);
            Stretch(modal.emptyLabel.rectTransform);
            var latest = Rect("LatestLog", panel);
            latest.anchorMin = latest.anchorMax = new Vector2(1, 0);
            latest.anchoredPosition = new Vector2(-66, 66); latest.sizeDelta = new Vector2(66, 66);
            var arrow = latest.gameObject.AddComponent<MatchLogIcon>(); arrow.downArrow = true;
            arrow.color = new Color(.94f, .84f, .72f);
            modal.latestButton = latest.gameObject.AddComponent<Button>(); modal.latestButton.targetGraphic = arrow;
            latest.gameObject.SetActive(false);
            overlay.gameObject.SetActive(false);
            return modal;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer;
            return rect;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Top(RectTransform rect, float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(left, -top); rect.sizeDelta = new Vector2(right - left, height);
        }
        private static TextMeshProUGUI Label(string name, Transform parent, TMP_FontAsset font, float size, TextAlignmentOptions alignment)
        {
            var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.alignment = alignment;
            text.color = new Color(.94f, .92f, .90f); text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }
        private static Button TextButton(string name, Transform parent, string caption, TMP_FontAsset font)
        {
            var rect = Rect(name, parent); var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(.13f, .12f, .15f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var label = Label("Label", rect, font, 28, TextAlignmentOptions.Center);
            Stretch(label.rectTransform); label.text = caption;
            return button;
        }
    }
}
