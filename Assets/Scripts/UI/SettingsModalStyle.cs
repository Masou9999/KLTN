using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>
    /// Restyles the authored settings and surrender-confirmation windows in the
    /// LoginScene modal style and localizes their text. Bindings and flow are unchanged.
    /// </summary>
    /// <remarks>
    /// MainMenu's settings window is a copy of GameScene's without the surrender
    /// action, so one routine serves both; each applies at most once per window.
    /// </remarks>
    internal static class SettingsModalStyle
    {
        #region Configuration

        private const float CornerSize = 62;
        private static readonly string[] Rows = { "Music", "UiSound", "SFX" };
        // Plain rules: title | audio rows (longer), audio rows | actions (shorter).
        private const float HeaderRuleY = 190;
        private const float HeaderRuleWidth = 660;
        private const float FooterRuleY = -120;
        private const float FooterRuleWidth = 480;
        // Rounded-fill corners are sized for panels; 2.5× gives buttons a small radius.
        private const float ButtonCornerScale = 2.5f;
        // LoginScene primary button surface (ImproveLoginSceneBuilder "Pastel" #D96862).
        private static readonly Color LoginPrimary = new Color32(217, 104, 98, 255);
        private static readonly Color LoginPrimaryOutline = new Color32(229, 123, 115, 255);
        private static readonly string[] Edges =
            { "TopBorder", "BottomBorder", "LeftBorder", "RightBorder" };

        #endregion

        #region Public Commands

        /// <summary>Applies the themed frame, separators, sliders and buttons.</summary>
        public static void ApplySettings(RectTransform window, ModalTheme theme)
        {
            if (!Prepare(window, theme)) return;
            TMP_FontAsset body = BodyFont(window);
            Heading(window, theme, "Cài đặt", 255, null);
            Line(window, "HeaderRule", HeaderRuleY, HeaderRuleWidth);
            Line(window, "FooterRule", FooterRuleY, FooterRuleWidth);

            // The icons identify each row, so there are no text labels.
            foreach (string row in Rows)
            {
                var icon = window.Find(row + "MuteControl")?.GetComponent<Image>();
                if (icon != null) icon.color = ModalChrome.Cream;
                var slider = window.Find(row + "Slider")?.GetComponent<Slider>();
                if (slider != null) StyleSlider(slider, theme);
            }

            foreach (var button in window.GetComponentsInChildren<Button>(true))
            {
                switch (button.name)
                {
                    case "SaveAudioButton": Action(button, theme, true, null, body); break;
                    case "CloseButton": Action(button, theme, false, "Đóng", body); break;
                    case "SurrenderButton": Action(button, theme, true, "Đầu hàng", body); break;
                }
            }
            if (window.Find("AudioSaveStatus")?.GetComponent<TMP_Text>() is TMP_Text status)
                status.color = new Color(ModalChrome.Cream.r, ModalChrome.Cream.g,
                    ModalChrome.Cream.b, .75f);
        }

        /// <summary>Applies the themed frame and Vietnamese copy to the surrender prompt.</summary>
        public static void ApplySurrender(RectTransform window, ModalTheme theme)
        {
            if (!Prepare(window, theme)) return;
            TMP_FontAsset body = BodyFont(window);
            Heading(window, theme, "Đầu hàng?", 115, 74);
            if (window.Find("MessageText")?.GetComponent<TMP_Text>() is TMP_Text message)
            {
                Place(message.rectTransform, 0, 0, 600, 70);
                message.text = "Bạn sẽ thua trận này. Bạn có chắc muốn đầu hàng?";
                message.font = body;
                message.fontSize = 24;
                message.color = ModalChrome.Cream;
                message.alignment = TextAlignmentOptions.Center;
            }
            foreach (var button in window.GetComponentsInChildren<Button>(true))
            {
                bool yes = button.name == "YesButton";
                if (!yes && button.name != "CancelButton") continue;
                Action(button, theme, yes, yes ? "Đầu hàng" : "Huỷ", body);
                Place((RectTransform)button.transform, yes ? -150 : 150, -112, 240, 58);
            }
        }

        #endregion

        #region Helpers

        /// <summary>Hides the flat 3 px edges and frames the surface once.</summary>
        private static bool Prepare(RectTransform window, ModalTheme theme)
        {
            if (window == null || theme == null || theme.fill == null
                || window.Find("Border") != null) return false;
            foreach (string edge in Edges)
            {
                // Edges sit on the window (confirm) or on its parent (settings).
                window.Find(edge)?.gameObject.SetActive(false);
                window.parent?.Find(edge)?.gameObject.SetActive(false);
            }
            var surface = window.GetComponent<Image>();
            if (surface == null) return false;
            ModalChrome.Frame(surface, theme, CornerSize);
            return true;
        }

        private static void Heading(RectTransform window, ModalTheme theme, string text,
            float y, float? dividerY)
        {
            if (!(window.Find("TitleText")?.GetComponent<TMP_Text>() is TMP_Text title)) return;
            title.text = text;
            if (theme.titleFont != null) title.font = theme.titleFont;
            title.fontSize = 42;
            title.fontStyle = FontStyles.Normal;
            title.color = ModalChrome.Cream;
            title.alignment = TextAlignmentOptions.Center;
            Place(title.rectTransform, 0, y, 600, 60);
            if (dividerY == null) return;
            var divider = ModalChrome.Picture("TitleDivider", window, theme.divider,
                0, dividerY.Value, 260, 38, Color.white);
            divider.preserveAspect = true;
            divider.transform.SetSiblingIndex(title.transform.GetSiblingIndex() + 1);
        }

        private static void Line(Transform window, string name, float y, float width)
        {
            ModalChrome.Picture(name, window, null, 0, y, width, 2,
                new Color(ModalChrome.Coral.r, ModalChrome.Coral.g, ModalChrome.Coral.b, .8f));
        }

        private static void StyleSlider(Slider slider, ModalTheme theme)
        {
            var track = slider.transform.Find("Background")?.GetComponent<Image>();
            if (track != null) Rounded(track, theme, new Color(.07f, .055f, .065f, 1));
            if (slider.fillRect != null && slider.fillRect.GetComponent<Image>() is Image fill)
                Rounded(fill, theme, ModalChrome.Coral);
            if (slider.handleRect != null && slider.handleRect.GetComponent<Image>() is Image handle)
                Rounded(handle, theme, ModalChrome.Cream);
        }

        private static void Rounded(Image image, ModalTheme theme, Color color)
        {
            image.sprite = theme.fill;
            image.type = Image.Type.Sliced;
            // Panel-sized corner radius scaled down to the 35-unit slider height.
            image.pixelsPerUnitMultiplier = 4f;
            image.color = color;
        }

        private static void Action(Button button, ModalTheme theme, bool primary,
            string text, TMP_FontAsset font)
        {
            var shadow = button.GetComponent<Outline>();
            if (shadow != null) Object.Destroy(shadow);
            ModalChrome.StyleButton(button, theme, primary);
            ((Image)button.targetGraphic).pixelsPerUnitMultiplier = ButtonCornerScale;
            var outline = button.transform.Find("Outline")?.GetComponent<Image>();
            if (outline != null) outline.pixelsPerUnitMultiplier = ButtonCornerScale;
            if (primary)
            {
                ((Image)button.targetGraphic).color = LoginPrimary;
                if (outline != null) outline.color = LoginPrimaryOutline;
            }
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text != null) label.text = text;
                label.font = font;
                label.fontStyle = FontStyles.Bold;
            }
        }

        private static TMP_FontAsset BodyFont(Transform window)
        {
            foreach (var text in window.GetComponentsInChildren<TMP_Text>(true))
                if (text.name != "TitleText" && text.font != null) return text.font;
            return null;
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
        }

        #endregion
    }
}
