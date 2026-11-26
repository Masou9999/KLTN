using System;
using KLTN.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>
    /// Scene-assigned sprites shared by the MainMenu profile and rename modals.
    /// </summary>
    [Serializable]
    public sealed class ModalTheme
    {
        [Tooltip("Rounded fill, drawn Sliced (ImproveLogin rounded-fill).")]
        public Sprite fill;
        [Tooltip("Frame outline, drawn Sliced (ImproveLogin panel-outline).")]
        public Sprite border;
        [Tooltip("Thin rounded outline for boxes and buttons (ImproveLogin rounded-outline).")]
        public Sprite outline;
        [Tooltip("Top-left corner ornament; mirrored for the other corners.")]
        public Sprite corner;
        public Sprite lotus;
        public Sprite titleLine;
        public Sprite divider;
        public Sprite close;
        [Tooltip("Colours monochrome icons from the Image colour (ImproveLogin AlphaTint).")]
        public Material tint;
        public TMP_FontAsset titleFont;
    }

    /// <summary>
    /// Builds the LoginScene-style modal frame: rounded panel, coral outline, mirrored
    /// corners, title divider and a close button centred on the top-right corner.
    /// </summary>
    internal static class ModalChrome
    {
        #region Palette

        public static readonly Color Cream = new Color32(239, 225, 205, 255);
        public static readonly Color Coral = new Color32(217, 104, 98, 255);
        public static readonly Color Red = new Color32(166, 44, 50, 255);
        public static readonly Color PanelColor = new Color(.12f, .09f, .105f, .97f);
        public static readonly Color BoxColor = new Color(.09f, .07f, .085f, .85f);
        public static readonly Color PrimaryFill = new Color32(142, 52, 58, 255);
        public static readonly Color SecondaryFill = new Color32(38, 29, 35, 255);
        public static readonly Color MutedOutline = new Color(.94f, .88f, .80f, .45f);

        #endregion

        #region Frame

        /// <summary>
        /// Skins an existing panel Image and inserts the outline and corners beneath
        /// its existing children so authored content keeps drawing on top.
        /// </summary>
        public static void Frame(Image panel, ModalTheme theme, float cornerSize)
        {
            panel.sprite = theme.fill;
            panel.type = Image.Type.Sliced;
            panel.color = PanelColor;
            panel.raycastTarget = true;
            // rect.size also covers stretched surfaces (sizeDelta is zero for those).
            Vector2 size = panel.rectTransform.rect.size;
            var border = Picture("Border", panel.transform, theme.border, 0, 0, 0, 0, Coral);
            border.type = Image.Type.Sliced;
            Stretch(border.rectTransform);
            border.transform.SetAsFirstSibling();
            float x = size.x * .5f - cornerSize * .5f - cornerSize * .17f;
            float y = size.y * .5f - cornerSize * .5f - cornerSize * .17f;
            for (int i = 3; i >= 0; i--)
            {
                bool right = i % 2 == 1;
                bool bottom = i >= 2;
                var corner = Picture("Corner" + i, panel.transform, theme.corner,
                    right ? x : -x, bottom ? -y : y, cornerSize, cornerSize * .94f,
                    Color.white);
                corner.preserveAspect = true;
                // The supplied ornament is drawn for the top-left corner.
                corner.transform.localScale =
                    new Vector3(right ? -1 : 1, bottom ? -1 : 1, 1);
                corner.transform.SetSiblingIndex(1);
            }
        }

        /// <summary>Adds the heading with the ornamental divider underneath it.</summary>
        public static TMP_Text Title(Transform parent, ModalTheme theme, TMP_FontAsset font,
            string text, float y, float width, float size)
        {
            var title = Label("Title", parent, theme.titleFont != null ? theme.titleFont : font,
                text, 0, y, width, size * 1.5f, size, Cream, true);
            Picture("TitleDivider", parent, theme.divider, 0, y - size * .95f,
                size * 7.2f, size * 1.05f, Color.white).preserveAspect = true;
            return title;
        }

        /// <summary>
        /// Creates the close button centred on the panel's top-right corner, half inside
        /// and half outside the frame. Call last so it draws above the corner ornament.
        /// </summary>
        public static Button Close(Transform panel, ModalTheme theme, float size,
            Action action)
        {
            var image = Picture("Close", panel, theme.close, 0, 0, size, size, Color.white);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one;
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1, .87f, .8f);
            colors.pressedColor = new Color(.75f, .6f, .55f);
            // A child modal (card detail, rename) makes the parent non-interactable;
            // the close button keeps its look instead of fading to the disabled tint.
            colors.disabledColor = Color.white;
            button.colors = colors;
            var sound = image.gameObject.AddComponent<UIButtonSound>();
            button.onClick.AddListener(sound.PlayClick);
            if (action != null) button.onClick.AddListener(() => action());
            return button;
        }

        /// <summary>Thin coral rule with a centred diamond, as between profile rows.</summary>
        public static void Rule(Transform parent, string name, float y, float width)
        {
            var root = Rect(name, parent, 0, y, width, 14);
            Color line = new Color(Coral.r, Coral.g, Coral.b, .7f);
            float half = width * .5f - 22;
            Picture("Left", root, null, -(half * .5f + 22), 0, half, 2, line);
            Picture("Right", root, null, half * .5f + 22, 0, half, 2, line);
            var diamond = Picture("Diamond", root, null, 0, 0, 9, 9, Coral);
            diamond.transform.localRotation = Quaternion.Euler(0, 0, 45);
        }

        #endregion

        #region Boxes and Buttons

        /// <summary>Gives an Image the rounded fill plus a thin outline child.</summary>
        public static Image Box(Image image, ModalTheme theme, Color fill, Color outline)
        {
            image.sprite = theme.fill;
            image.type = Image.Type.Sliced;
            image.color = fill;
            var rect = image.rectTransform;
            var border = Picture("Outline", rect, theme.outline, 0, 0, 0, 0, outline);
            border.type = Image.Type.Sliced;
            Stretch(border.rectTransform);
            border.transform.SetAsFirstSibling();
            return border;
        }

        /// <summary>Restyles a button: coral when primary, dark with a muted outline otherwise.</summary>
        public static void StyleButton(Button button, ModalTheme theme, bool primary)
        {
            var image = (Image)button.targetGraphic;
            var existing = image.transform.Find("Outline");
            var outline = existing != null ? existing.GetComponent<Image>()
                : Box(image, theme, Color.white, Color.white);
            image.color = primary ? PrimaryFill : SecondaryFill;
            outline.color = primary ? Coral : MutedOutline;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.2f, 1.2f);
            colors.selectedColor = new Color(1.15f, 1.1f, 1.1f);
            colors.pressedColor = new Color(.8f, .75f, .75f);
            // Buttons styled here can be genuinely unavailable (save, rename): fade them.
            colors.disabledColor = ButtonDisabledStyle.UnavailableColor;
            colors.colorMultiplier = 1.25f;
            button.colors = colors;
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                label.color = Cream;
        }

        #endregion

        #region Primitives

        public static RectTransform Rect(string name, Transform parent,
            float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        public static Image Picture(string name, Transform parent, Sprite sprite,
            float x, float y, float w, float h, Color color)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Monochrome icon coloured through the AlphaTint material.</summary>
        public static Image Icon(string name, Transform parent, ModalTheme theme,
            Sprite sprite, float x, float y, float size, Color color)
        {
            var image = Picture(name, parent, sprite, x, y, size, size, color);
            image.material = theme.tint;
            image.preserveAspect = true;
            return image;
        }

        public static TMP_Text Label(string name, Transform parent, TMP_FontAsset font,
            string text, float x, float y, float w, float h, float size, Color color,
            bool center = false)
        {
            var label = Rect(name, parent, x, y, w, h).gameObject
                .AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.raycastTarget = false;
            label.richText = false;
            label.alignment = center
                ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        #endregion
    }
}
