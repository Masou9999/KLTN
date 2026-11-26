using UnityEngine;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>Shared geometry for the audio settings modal in every scene.</summary>
    public static class SettingsPanelLayout
    {
        public const float StatusY = -162f;
        public const float ActionY = -236f;

        private static readonly (string Name, float Y)[] Rows =
            { ("Music", 110f), ("UiSound", 30f), ("SFX", -50f) };

        /// <summary>Uses one frame size for the surface and all four stretched edges.</summary>
        public static void ApplyLayout(Transform window)
        {
            var panel = (RectTransform)window.parent;
            panel.sizeDelta = new Vector2(900, 640);
            var surface = (RectTransform)window;
            surface.anchorMin = Vector2.zero;
            surface.anchorMax = Vector2.one;
            surface.offsetMin = surface.offsetMax = Vector2.zero;
            var background = panel.GetComponent<Image>();
            if (background != null) background.color = Color.clear;
            foreach (string name in new[]
                { "TopBorder", "BottomBorder", "LeftBorder", "RightBorder" })
            {
                if (!(panel.Find(name) is RectTransform edge)) continue;
                bool horizontal = name == "TopBorder" || name == "BottomBorder";
                float side = name == "TopBorder" || name == "RightBorder" ? 1f : 0f;
                edge.anchorMin = horizontal ? new Vector2(0, side) : new Vector2(side, 0);
                edge.anchorMax = horizontal ? new Vector2(1, side) : new Vector2(side, 1);
                edge.anchoredPosition = Vector2.zero;
                edge.sizeDelta = horizontal ? new Vector2(0, 3) : new Vector2(3, 0);
                edge.GetComponent<Image>().raycastTarget = false;
            }
            if (window.Find("TitleText") is RectTransform title)
                Place(title, new Vector2(0, 255), new Vector2(720, 65));
            foreach (var row in Rows)
            {
                // Mute icon | long slider; the icon is the row's only label.
                Position(window, row.Name + "MuteControl", -300, row.Y);
                Position(window, row.Name + "Slider", 50, row.Y);
                if (window.Find(row.Name + "Slider") is RectTransform slider)
                    slider.sizeDelta = new Vector2(540, slider.sizeDelta.y);
            }
        }

        private static void Position(Transform window, string name, float x, float y)
        {
            if (window.Find(name) is RectTransform rect)
            {
                rect.anchoredPosition = new Vector2(x, y);
            }
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
