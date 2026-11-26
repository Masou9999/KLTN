using System;
using KLTN.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>Builds the player-information layout in the LoginScene modal style.</summary>
    /// <remarks>
    /// The frame is wider than the personal page needs so the ranking table and the
    /// four-column owned-card grid fit without horizontal scrolling.
    /// </remarks>
    internal sealed class PlayerInformationPanel
    {
        #region Layout

        public const float Width = 1240;
        public const float Height = 980;
        public const float PageWidth = 1120;
        public const float PageHeight = 560;
        public const float HistoryWidth = 1080;
        public const float CardsWidth = 1100;
        public static readonly float[] HistoryColumns = { -405, -150, 125, 380 };
        public static readonly Vector2 RenameTrigger = new Vector2(450, 205);

        private static readonly Vector2 PageCentre = new Vector2(0, -140);
        private static readonly string[] Titles =
            { "Thông tin cá nhân", "Lịch sử xếp hạng", "Bài đã sở hữu" };

        #endregion

        #region Properties

        private readonly TMP_FontAsset font;
        private readonly ModalTheme theme;
        private readonly Image[] tabFills = new Image[3];
        private readonly Image[] tabOutlines = new Image[3];
        private readonly TMP_Text title;
        public GameObject Root { get; }
        public CanvasGroup Group { get; }
        public RectTransform[] Pages { get; } = new RectTransform[3];
        public Button[] Tabs { get; } = new Button[3];

        /// <summary>Username, email, rank, gold (coins) and silver, in that order.</summary>
        public TMP_Text[] Values { get; } = new TMP_Text[5];
        public TMP_Text RenameHint { get; }
        public TMP_Text HistoryStatus { get; }
        public TMP_Text CardsStatus { get; }
        public RectTransform HistoryContent { get; }
        public RectTransform CardsContent { get; }

        /// <summary>Scrolling near the end of this view requests the next season page.</summary>
        public ScrollRect HistoryScroll { get; }
        public Button Close { get; }

        #endregion

        #region Construction

        public PlayerInformationPanel(Transform canvas, TMP_FontAsset font, ModalTheme theme,
            Sprite[] tabIcons, Sprite coin, Sprite silver, Action<int> selectTab,
            Action close)
        {
            this.font = font;
            this.theme = theme;
            var overlay = Image("PlayerInformationModal", canvas, 0, 0, 0, 0,
                new Color(0, 0, 0, .78f));
            ModalChrome.Stretch(overlay.rectTransform);
            overlay.raycastTarget = true;
            Root = overlay.gameObject;
            Group = Root.AddComponent<CanvasGroup>();
            var panel = Image("Panel", overlay.transform, 0, 0, Width, Height, Color.white);
            ModalChrome.Frame(panel, theme, 92);

            // Header: lotus between two lines, then the title and its divider.
            var lotus = ModalChrome.Picture("Lotus", panel.transform, theme.lotus,
                0, Height * .5f - 52, 96, 62, ModalChrome.Coral);
            lotus.preserveAspect = true;
            // Same treatment as the LoginScene lotus: AlphaTint in the accent coral.
            lotus.material = theme.tint;
            lotus.color = new Color32(229, 123, 115, 255);
            ModalChrome.Picture("TitleLineLeft", panel.transform, theme.titleLine,
                -150, Height * .5f - 50, 135, 27, Color.white);
            ModalChrome.Picture("TitleLineRight", panel.transform, theme.titleLine,
                150, Height * .5f - 50, 135, 27, Color.white).transform.localScale =
                new Vector3(-1, 1, 1);
            title = ModalChrome.Title(panel.transform, theme, font, Titles[0],
                Height * .5f - 122, 900, 50);

            for (int i = 0; i < Tabs.Length; i++)
            {
                int index = i;
                Tabs[i] = Tab(panel.transform, i, tabIcons[i], () => selectTab(index));
            }

            var content = Image("Content", panel.transform, PageCentre.x, PageCentre.y,
                PageWidth + 40, PageHeight + 40, Color.white);
            ModalChrome.Box(content, theme, ModalChrome.BoxColor,
                new Color(ModalChrome.Cream.r, ModalChrome.Cream.g, ModalChrome.Cream.b, .7f));
            for (int i = 0; i < Pages.Length; i++)
                Pages[i] = Rect("Page" + i, content.transform, 0, 0, PageWidth, PageHeight);

            RenameHint = BuildPersonal(Pages[0], coin, silver);
            HistoryStatus = Text("Status", Pages[1], "", 0, 248, 1080, 40, 22, true);
            var header = Rect("Header", Pages[1], 0, 200, HistoryWidth, 52);
            ModalChrome.Rule(header, "HeaderRule", -30, HistoryWidth - 40);
            string[] titles = { "Mùa", "Điểm", "Bậc", "Tỉ lệ thắng" };
            for (int i = 0; i < titles.Length; i++)
                Text("Column" + i, header, titles[i], HistoryColumns[i], 0, 240, 46, 26, true)
                    .color = ModalChrome.Coral;
            HistoryContent = Scroll("History", Pages[1], 0, -52, HistoryWidth, 430);
            HistoryScroll = HistoryContent.GetComponentInParent<ScrollRect>();
            CardsStatus = Text("Status", Pages[2], "", 0, 250, 1100, 40, 22, true);
            CardsContent = Scroll("Cards", Pages[2], 0, -22, CardsWidth, 500);

            Close = ModalChrome.Close(panel.transform, theme, 84, close);
            Root.SetActive(false);
        }

        private Button Tab(Transform panel, int index, Sprite icon, Action action)
        {
            var fill = Image("Tab" + index, panel, (index - 1) * 375, 230, 355, 86,
                Color.white);
            fill.raycastTarget = true;
            tabOutlines[index] = ModalChrome.Box(fill, theme, ModalChrome.SecondaryFill,
                ModalChrome.MutedOutline);
            tabFills[index] = fill;
            ModalChrome.Icon("Icon", fill.transform, theme, icon, 0, 0, 52, ModalChrome.Cream);
            var button = fill.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.25f, 1.25f);
            colors.selectedColor = new Color(1.15f, 1.1f, 1.1f);
            colors.colorMultiplier = 1.3f;
            button.colors = colors;
            // Tabs are only disabled while a child modal (card detail, rename) is open.
            ButtonDisabledStyle.LockOnly(button);
            var sound = fill.gameObject.AddComponent<UIButtonSound>();
            button.onClick.AddListener(sound.PlayClick);
            button.onClick.AddListener(() => action());
            return button;
        }

        /// <summary>Builds the personal page and returns the rename-count hint.</summary>
        private TMP_Text BuildPersonal(RectTransform page, Sprite coin, Sprite silver)
        {
            const float labelX = -335;
            const float valueX = 175;
            Text("Field0", page, "Tên người chơi", labelX, 205, 330, 56, 28).fontStyle =
                FontStyles.Bold;
            var nameBox = Image("UsernameBox", page, valueX, 205, 640, 66, Color.white);
            ModalChrome.Box(nameBox, theme, new Color(.16f, .12f, .14f, .9f),
                ModalChrome.MutedOutline);
            Values[0] = Text("Value0", nameBox.transform, "Đang tải…", -30, 0, 540, 56, 28);
            var hint = Text("RenameHint", page, "", valueX - 10, 148, 620, 36, 21);
            hint.color = new Color(ModalChrome.Cream.r, ModalChrome.Cream.g,
                ModalChrome.Cream.b, .8f);
            ModalChrome.Rule(page, "Rule0", 100, 960);

            Text("Field1", page, "Email", labelX, 40, 330, 56, 28).fontStyle = FontStyles.Bold;
            Values[1] = Text("Value1", page, "Đang tải…", valueX, 40, 640, 56, 26);
            ModalChrome.Rule(page, "Rule1", -20, 960);

            Text("Field2", page, "Điểm xếp hạng", labelX, -80, 330, 56, 28).fontStyle =
                FontStyles.Bold;
            Values[2] = Text("Value2", page, "Đang tải…", valueX, -80, 640, 56, 28);
            ModalChrome.Rule(page, "Rule2", -140, 960);

            Values[4] = Currency(page, "Silver", silver, 70);
            Values[3] = Currency(page, "Gold", coin, 380);
            foreach (var value in Values)
            {
                value.richText = false;
                value.textWrappingMode = TextWrappingModes.NoWrap;
                value.overflowMode = TextOverflowModes.Ellipsis;
            }
            return hint;
        }

        /// <summary>Pill with the result-screen ingot overlapping its left end.</summary>
        private TMP_Text Currency(RectTransform page, string name, Sprite icon, float x)
        {
            var pill = Image(name, page, x, -222, 220, 54, Color.white);
            ModalChrome.Box(pill, theme, new Color(.10f, .08f, .09f, .9f), ModalChrome.Cream);
            var image = ModalChrome.Picture("Icon", pill.transform, icon, -100, 4, 104, 72,
                Color.white);
            image.preserveAspect = true;
            // Right padding matches the pill radius so digits never touch the outline.
            var value = Text("Value", pill.transform, "Đang tải…", 16, 0, 140, 48, 27);
            value.alignment = TextAlignmentOptions.MidlineRight;
            value.fontStyle = FontStyles.Bold;
            return value;
        }

        #endregion

        #region Commands

        public void Select(int index)
        {
            title.text = Titles[index];
            for (int i = 0; i < Pages.Length; i++)
            {
                bool active = i == index;
                Pages[i].gameObject.SetActive(active);
                tabFills[i].color = active ? ModalChrome.PrimaryFill : ModalChrome.SecondaryFill;
                tabOutlines[i].color = active ? ModalChrome.Coral : ModalChrome.MutedOutline;
            }
        }

        #endregion

        #region Builders

        public TMP_Text Text(string name, Transform parent, string text,
            float x, float y, float w, float h, float size, bool center = false)
        {
            return ModalChrome.Label(name, parent, font, text, x, y, w, h, size,
                ModalChrome.Cream, center);
        }

        public static RectTransform Rect(string name, Transform parent,
            float x, float y, float w, float h)
        {
            return ModalChrome.Rect(name, parent, x, y, w, h);
        }

        public static Image Image(string name, Transform parent,
            float x, float y, float w, float h, Color color)
        {
            return ModalChrome.Picture(name, parent, null, x, y, w, h, color);
        }

        private static RectTransform Scroll(string name, Transform parent,
            float x, float y, float w, float h)
        {
            var viewport = Image(name, parent, x, y, w, h, new Color(0, 0, 0, .08f));
            viewport.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport.transform, 0, 0, w, 0);
            content.anchorMin = content.anchorMax = new Vector2(.5f, 1);
            content.pivot = new Vector2(.5f, 1);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport.rectTransform;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            return content;
        }

        #endregion
    }
}
