using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Connection messages drawn above every scene (FR-NET-06/07): a banner that also blocks
    /// gameplay input while the match is paused, and a dialog for the local player's own
    /// lost connection. Built in code so no scene has to be edited; it lives on the
    /// DontDestroyOnLoad object that owns it, so it survives the reconnect scene reload.
    /// </summary>
    public sealed class MatchConnectionOverlay : MonoBehaviour
    {
        #region Constants

        private const int SortingOrder = 5000;
        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        private static readonly Color BannerShade = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color DialogShade = new Color(0f, 0f, 0f, 0.8f);
        private static readonly Color PanelColor = new Color(0.12f, 0.09f, 0.09f, 0.95f);
        private static readonly Color ButtonColor = new Color(0.62f, 0.12f, 0.12f, 1f);
        private static readonly Color ButtonTextColor = new Color(1f, 0.85f, 0.35f, 1f);

        #endregion

        #region Runtime State

        private GameObject bannerRoot;
        private TMP_Text bannerText;
        private GameObject dialogRoot;
        private TMP_Text dialogText;
        private Button primaryButton;
        private TMP_Text primaryLabel;
        private Button secondaryButton;
        private TMP_Text secondaryLabel;
        private Action primaryAction;
        private Action secondaryAction;

        #endregion

        #region Public API

        public bool IsDialogVisible => dialogRoot != null && dialogRoot.activeSelf;

        public void ShowBanner(string message)
        {
            Build();
            bannerText.text = message;
            bannerRoot.SetActive(true);
        }

        public void HideBanner()
        {
            if (bannerRoot != null)
            {
                bannerRoot.SetActive(false);
            }
        }

        /// <summary>Shows a blocking dialog; a button is hidden when its label is null.</summary>
        public void ShowDialog(
            string message,
            string primaryText = null,
            Action onPrimary = null,
            string secondaryText = null,
            Action onSecondary = null
        )
        {
            Build();
            dialogText.text = message;
            primaryAction = onPrimary;
            secondaryAction = onSecondary;
            ConfigureButton(primaryButton, primaryLabel, primaryText);
            ConfigureButton(secondaryButton, secondaryLabel, secondaryText);

            // A lone button sits in the middle.
            var primaryRect = (RectTransform)primaryButton.transform;
            primaryRect.anchoredPosition = new Vector2(
                string.IsNullOrEmpty(secondaryText) ? 0f : -220f,
                primaryRect.anchoredPosition.y
            );
            dialogRoot.SetActive(true);
        }

        /// <summary>Updates the dialog text without touching its buttons.</summary>
        public void SetDialogMessage(string message)
        {
            if (dialogText != null)
            {
                dialogText.text = message;
            }
        }

        public void HideDialog()
        {
            if (dialogRoot != null)
            {
                dialogRoot.SetActive(false);
            }

            primaryAction = null;
            secondaryAction = null;
        }

        public void HideAll()
        {
            HideBanner();
            HideDialog();
        }

        #endregion

        #region Construction

        private void Build()
        {
            if (bannerRoot != null)
            {
                return;
            }

            var canvasObject = new GameObject(
                "ConnectionOverlayCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster)
            );
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            // The shade catches every click, which locks gameplay input while it is shown.
            bannerRoot = CreateShade("PauseBanner", canvasObject.transform, BannerShade);
            RectTransform bannerPanel = CreatePanel(bannerRoot.transform, new Vector2(1100f, 140f));
            bannerPanel.anchorMin = bannerPanel.anchorMax = new Vector2(0.5f, 1f);
            bannerPanel.pivot = new Vector2(0.5f, 1f);
            bannerPanel.anchoredPosition = new Vector2(0f, -60f);
            bannerText = CreateText(bannerPanel, 40f);

            dialogRoot = CreateShade("ConnectionDialog", canvasObject.transform, DialogShade);
            RectTransform dialogPanel = CreatePanel(dialogRoot.transform, new Vector2(1100f, 420f));
            dialogText = CreateText(dialogPanel, 40f);
            dialogText.rectTransform.offsetMin = new Vector2(40f, 150f);
            dialogText.rectTransform.offsetMax = new Vector2(-40f, -30f);

            primaryButton = CreateButton(dialogPanel, new Vector2(-220f, 40f), out primaryLabel);
            primaryButton.onClick.AddListener(() => primaryAction?.Invoke());
            secondaryButton = CreateButton(dialogPanel, new Vector2(220f, 40f), out secondaryLabel);
            secondaryButton.onClick.AddListener(() => secondaryAction?.Invoke());

            bannerRoot.SetActive(false);
            dialogRoot.SetActive(false);
        }

        private static GameObject CreateShade(string name, Transform parent, Color color)
        {
            var shade = new GameObject(name, typeof(RectTransform), typeof(Image));
            shade.transform.SetParent(parent, false);
            Stretch(shade.GetComponent<RectTransform>());
            Image image = shade.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return shade;
        }

        private static RectTransform CreatePanel(Transform parent, Vector2 size)
        {
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            panel.GetComponent<Image>().color = PanelColor;
            return rect;
        }

        private static TMP_Text CreateText(Transform parent, float fontSize)
        {
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            Stretch(rect);
            rect.offsetMin = new Vector2(30f, 10f);
            rect.offsetMax = new Vector2(-30f, -10f);

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(Transform parent, Vector2 position, out TMP_Text label)
        {
            var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(380f, 90f);
            rect.anchoredPosition = position;
            buttonObject.GetComponent<Image>().color = ButtonColor;

            label = CreateText(buttonObject.transform, 34f);
            label.color = ButtonTextColor;
            return buttonObject.GetComponent<Button>();
        }

        private static void ConfigureButton(Button button, TMP_Text label, string text)
        {
            bool visible = !string.IsNullOrEmpty(text);
            button.gameObject.SetActive(visible);

            if (visible)
            {
                label.text = text;
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        #endregion
    }
}
