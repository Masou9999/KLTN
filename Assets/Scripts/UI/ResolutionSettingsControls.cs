using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>Adds mutually exclusive resolution checkboxes to settings.</summary>
    internal static class ResolutionSettingsControls
    {
        #region Construction

        public static Toggle[] Build(Transform window, TMP_Text template,
            UnityAction<int> select)
        {
            var windowRect = (RectTransform)window;
            windowRect.sizeDelta += new Vector2(0, 120);
            Move(window, "TitleText", 60);
            foreach (string prefix in new[] { "Music", "UiSound", "SFX" })
            {
                Move(window, prefix + "Label", 40);
                Move(window, prefix + "Slider", 40);
            }
            Move(window, "MusicMuteControl", -40);
            Move(window, "SoundMuteControl", -40);
            Move(window, "SaveAudioButton", -60);
            Move(window, "CloseButton", -60);
            Move(window, "SurrenderButton", -60);
            Move(window, "AudioSaveStatus", -60);

            var row = new GameObject("ResolutionRow", typeof(RectTransform),
                typeof(ToggleGroup));
            row.transform.SetParent(window, false);
            Place((RectTransform)row.transform, new Vector2(0, -65),
                new Vector2(720, 44));
            var group = row.GetComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            Label(template, row.transform, "Độ phân giải", -285, 150);
            var toggles = new Toggle[WindowResolutionSettings.Count];
            for (int i = 0; i < toggles.Length; i++)
            {
                int index = i;
                var option = new GameObject("Resolution" + i, typeof(RectTransform),
                    typeof(Image), typeof(Toggle));
                option.transform.SetParent(row.transform, false);
                Place((RectTransform)option.transform,
                    new Vector2(-95 + i * 170, 0), new Vector2(160, 44));
                option.GetComponent<Image>().color = Color.clear;
                var box = new GameObject("Box", typeof(RectTransform), typeof(Image));
                box.transform.SetParent(option.transform, false);
                Place((RectTransform)box.transform, new Vector2(-64, 0),
                    new Vector2(26, 26));
                box.GetComponent<Image>().color = new Color(0.85f, 0.68f, 0.4f);
                var tick = new GameObject("Checkmark", typeof(RectTransform),
                    typeof(ResolutionCheckmarkGraphic));
                tick.transform.SetParent(box.transform, false);
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

        private static TMP_Text Label(TMP_Text template, Transform parent,
            string value, float x, float width)
        {
            var text = Object.Instantiate(template, parent, false);
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

        private static void Move(Transform window, string name, float y)
        {
            if (window.Find(name) is RectTransform rect)
            {
                rect.anchoredPosition += new Vector2(0, y);
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
