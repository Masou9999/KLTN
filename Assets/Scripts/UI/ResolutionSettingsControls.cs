using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>Adds mutually exclusive resolution checkboxes to settings.</summary>
    public static class ResolutionSettingsControls
    {
        #region Construction

        public static Toggle[] Build(Transform window, TMP_Text template,
            UnityAction<int> select)
        {
            ApplyLayout(window);

            var row = Child(window, "ResolutionRow");
            if (row.GetComponent<ToggleGroup>() == null) row.AddComponent<ToggleGroup>();
            Place((RectTransform)row.transform, new Vector2(0, -116),
                new Vector2(680, 44));
            var group = row.GetComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            TMP_Text heading = Label(template, row.transform, "Độ phân giải", 0, 540);
            heading.alignment = TextAlignmentOptions.Center;
            heading.rectTransform.anchoredPosition = new Vector2(0, 48);
            var toggles = new Toggle[WindowResolutionSettings.Count];
            for (int i = 0; i < toggles.Length; i++)
            {
                int index = i;
                var option = Child(row.transform, "Resolution" + i);
                if (option.GetComponent<Image>() == null) option.AddComponent<Image>();
                if (option.GetComponent<Toggle>() == null) option.AddComponent<Toggle>();
                Place((RectTransform)option.transform,
                    new Vector2(-220 + i * 220, 0), new Vector2(180, 44));
                option.GetComponent<Image>().color = Color.clear;
                var box = Child(option.transform, "Box");
                if (box.GetComponent<Image>() == null) box.AddComponent<Image>();
                Place((RectTransform)box.transform, new Vector2(-64, 0),
                    new Vector2(26, 26));
                box.GetComponent<Image>().color = new Color(0.85f, 0.68f, 0.4f);
                var tick = Child(box.transform, "Checkmark");
                if (tick.GetComponent<ResolutionCheckmarkGraphic>() == null)
                    tick.AddComponent<ResolutionCheckmarkGraphic>();
                Place((RectTransform)tick.transform, Vector2.zero, new Vector2(26, 26));
                var mark = tick.GetComponent<ResolutionCheckmarkGraphic>();
                mark.raycastTarget = false;
                mark.color = new Color(0.04f, 0.07f, 0.09f);
                var size = WindowResolutionSettings.Size(i);
                Label(template, option.transform, $"{size.x}×{size.y}", 17, 118);
                var toggle = option.GetComponent<Toggle>();
                toggle.targetGraphic = box.GetComponent<Image>();
                toggle.graphic = mark;
                toggle.SetIsOnWithoutNotify(i == WindowResolutionSettings.SavedIndex);
                toggle.group = group;
                toggle.onValueChanged = new Toggle.ToggleEvent();
                toggle.onValueChanged.AddListener(on =>
                {
                    if (on) select(index);
                });
                toggles[i] = toggle;
            }
            return toggles;
        }

        #endregion

        #region Helpers

        /// <summary>Creates or rebinds the independent fullscreen checkbox.</summary>
        public static Toggle BuildFullscreen(Transform window, TMP_Text template,
            UnityAction<bool> select)
        {
            var option = Child(window, "FullscreenToggle");
            if (option.GetComponent<Image>() == null) option.AddComponent<Image>();
            if (option.GetComponent<Toggle>() == null) option.AddComponent<Toggle>();
            Place((RectTransform)option.transform,
                new Vector2(0, -174), new Vector2(340, 44));
            option.GetComponent<Image>().color = Color.clear;
            var box = Child(option.transform, "Box");
            if (box.GetComponent<Image>() == null) box.AddComponent<Image>();
            Place((RectTransform)box.transform, new Vector2(-145, 0),
                new Vector2(26, 26));
            box.GetComponent<Image>().color = new Color(0.85f, 0.68f, 0.4f);
            var tick = Child(box.transform, "Checkmark");
            if (tick.GetComponent<ResolutionCheckmarkGraphic>() == null)
                tick.AddComponent<ResolutionCheckmarkGraphic>();
            Place((RectTransform)tick.transform, Vector2.zero, new Vector2(26, 26));
            var mark = tick.GetComponent<ResolutionCheckmarkGraphic>();
            mark.raycastTarget = false;
            mark.color = new Color(0.04f, 0.07f, 0.09f);
            Label(template, option.transform, "Toàn màn hình (F11)", 20, 280);
            var toggle = option.GetComponent<Toggle>();
            toggle.group = null;
            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.graphic = mark;
            toggle.SetIsOnWithoutNotify(WindowResolutionSettings.SavedFullscreen);
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.onValueChanged.AddListener(select);
            return toggle;
        }

        private static TMP_Text Label(TMP_Text template, Transform parent,
            string value, float x, float width)
        {
            Transform existing = parent.Find("Label");
            var text = existing != null ? existing.GetComponent<TMP_Text>()
                : Object.Instantiate(template, parent, false);
            text.name = "Label";
            text.text = value;
            text.fontStyle = FontStyles.Normal;
            text.fontSize = 22;
            text.enableAutoSizing = true;
            text.fontSizeMin = 18;
            text.fontSizeMax = 22;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            Place(text.rectTransform, new Vector2(x, 0), new Vector2(width, 44));
            return text;
        }

        /// <summary>Uses one frame size for the surface and all four stretched edges.</summary>
        public static void ApplyLayout(Transform window)
        {
            var panel = (RectTransform)window.parent;
            panel.sizeDelta = new Vector2(900, 680);
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
                Place(title, new Vector2(0, 250), new Vector2(720, 65));
            foreach (var row in new[]
                { ("Music", 160f), ("UiSound", 88f), ("SFX", 16f) })
            {
                Position(window, row.Item1 + "MuteControl", -220, row.Item2);
                Position(window, row.Item1 + "Slider", 70, row.Item2);
            }
        }

        private static GameObject Child(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing.gameObject;
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
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

        #endregion
    }
}
