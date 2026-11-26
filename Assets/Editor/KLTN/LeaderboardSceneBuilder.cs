#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KLTN.Game.Content;
using KLTN.Game.Domain.Ranking;
using KLTN.UI.Leaderboard;
using KLTN.UI.Shop;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Generates the leaderboard so it does not have to be assembled by hand:
    ///   - Resources/RankingSettings.asset and Resources/RankCatalog.asset (rank sprites)
    ///   - Assets/Scenes/LeaderboardScene.unity (+ Build Settings entry)
    ///   - a "Leaderboard" button on MainMenu wired to MainMenuController.OpenLeaderboard
    ///   - CloudCode/SeasonModule/deploy/season-tick.sched from RankingSettings
    /// Safe to run again; it overwrites the generated scene and skips an existing menu button.
    /// </summary>
    public static class LeaderboardSceneBuilder
    {
        #region Paths

        private const string ScenePath = "Assets/Scenes/LeaderboardScene.unity";
        private const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
        private const string SettingsPath = "Assets/Resources/RankingSettings.asset";
        private const string CatalogPath = "Assets/Resources/RankCatalog.asset";
        private const string FontPath = "Assets/Fonts/XTypewriter-Bold SDF.asset";
        private const string SchedulePath = "CloudCode/SeasonModule/deploy/season-tick.sched";

        // Bronze, Silver, Gold, Platinum | Diamond, Master, Challenger (left to right in each sheet).
        private static readonly string[] RankSheets =
        {
            "Assets/Art/UI/Ranks/Bronze to Plate.png",
            "Assets/Art/UI/Ranks/Diamond to Challenger.png",
        };

        #endregion

        #region Layout

        private const float RowHeight = 76f;
        private const float RowSpacing = 6f;

        // Column anchors (fraction of the table width): STT | Username | Rank | Điểm | Tỉ lệ thắng.
        private static readonly Vector2[] Columns =
        {
            new Vector2(0f, 0.10f),
            new Vector2(0.10f, 0.42f),
            new Vector2(0.42f, 0.68f),
            new Vector2(0.68f, 0.83f),
            new Vector2(0.83f, 1f),
        };

        private static readonly Color Background = new Color(0.07f, 0.06f, 0.08f, 1f);
        private static readonly Color Panel = new Color(0.12f, 0.1f, 0.12f, 0.95f);
        private static readonly Color HeaderRow = new Color(0.30f, 0.09f, 0.08f, 1f);
        private static readonly Color Accent = new Color(0.78f, 0.62f, 0.25f, 1f);
        private static readonly Color IdleButton = new Color(0.22f, 0.19f, 0.21f, 1f);

        private static TMP_FontAsset font;

        #endregion

        #region Menu

        [MenuItem("Tools/KLTN/Build Leaderboard Scene + Main Menu Button")]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            InstallRankingAssets();
            ExportSeasonSchedule();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            BuildScene();
            AddToBuildSettings(ScenePath);
            AddMainMenuButton();

            Debug.Log("[LeaderboardBuilder] Done. Open MainMenu and press Play to test the leaderboard.");
        }

        [MenuItem("Tools/KLTN/Install Ranking Assets")]
        public static void InstallRankingAssets()
        {
            Directory.CreateDirectory("Assets/Resources");

            if (AssetDatabase.LoadAssetAtPath<RankingSettings>(SettingsPath) == null)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<RankingSettings>(), SettingsPath);
            }

            RankCatalog catalog = AssetDatabase.LoadAssetAtPath<RankCatalog>(CatalogPath);

            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<RankCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            // Slice names are not contiguous (Diamond sheet has _0, _2, _3), so order by position.
            var icons = new List<Sprite>();

            foreach (string sheet in RankSheets)
            {
                icons.AddRange(
                    AssetDatabase.LoadAllAssetsAtPath(sheet)
                        .OfType<Sprite>()
                        .OrderBy(sprite => sprite.rect.x)
                );
            }

            if (icons.Count != RankPolicy.TierCount)
            {
                Debug.LogWarning(
                    $"[LeaderboardBuilder] Expected {RankPolicy.TierCount} rank sprites, found {icons.Count}. "
                        + "Check the sprite slicing in Assets/Art/UI/Ranks and the order in RankCatalog."
                );
            }

            Undo.RecordObject(catalog, "Install Rank Catalog");
            catalog.EditorReplace(icons);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LeaderboardBuilder] RankCatalog: {icons.Count} icons. Settings: {SettingsPath}.");
        }

        /// <summary>Writes the UGS Scheduler config whose payload carries the Inspector values.</summary>
        [MenuItem("Tools/KLTN/Export Season Schedule")]
        public static void ExportSeasonSchedule()
        {
            RankingSettings settings = AssetDatabase.LoadAssetAtPath<RankingSettings>(SettingsPath);

            if (settings == null)
            {
                InstallRankingAssets();
                settings = AssetDatabase.LoadAssetAtPath<RankingSettings>(SettingsPath);
            }

            string hours = settings.SeasonDurationHours.ToString("0.###", CultureInfo.InvariantCulture);
            string json = "{\n"
                + "  \"$schema\": \"https://ugs-config-schemas.unity3d.com/v1/schedules.schema.json\",\n"
                + "  \"Configs\": {\n"
                + "    \"season-tick\": {\n"
                + "      \"EventName\": \"season-tick\",\n"
                + "      \"Type\": \"recurring\",\n"
                + $"      \"Schedule\": \"{settings.ScheduleCron}\",\n"
                + "      \"PayloadVersion\": 1,\n"
                + $"      \"Payload\": \"{{\\\"durationHours\\\": {hours}, \\\"lockMinutes\\\": {settings.PointsLockMinutes}}}\"\n"
                + "    }\n"
                + "  }\n"
                + "}\n";

            string fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, SchedulePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, json);
            Debug.Log(
                $"[LeaderboardBuilder] {SchedulePath}: {hours} h/mùa, khóa điểm {settings.PointsLockMinutes} phút. "
                    + "Deploy it (UGS CLI or Dashboard) for the change to apply from the next season."
            );
        }

        #endregion

        #region Scene

        private static void BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            cameraObject.tag = "MainCamera";

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            Transform canvas = canvasObject.transform;

            Image background = Child("Background", canvas).gameObject.AddComponent<Image>();
            Stretch(background.rectTransform, Vector2.zero, Vector2.one);
            background.color = Background;

            // Left: back button + season list.
            RectTransform sidebar = Child("SeasonSidebar", canvas);
            sidebar.anchorMin = Vector2.zero;
            sidebar.anchorMax = new Vector2(0f, 1f);
            sidebar.pivot = new Vector2(0f, 0.5f);
            sidebar.sizeDelta = new Vector2(360f, 0f);
            sidebar.anchoredPosition = Vector2.zero;
            sidebar.gameObject.AddComponent<Image>().color = Panel;

            Button back = ButtonWithLabel("BackButton", sidebar, "< MENU", new Vector2(220f, 64f), Accent, Color.black);
            Place((RectTransform)back.transform, new Vector2(0.5f, 1f), new Vector2(0f, -70f));

            TextMeshProUGUI seasonsLabel = Text("SeasonsLabel", sidebar, "MÙA GIẢI", 36, Accent);
            Place(seasonsLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(320f, 60f));

            (RectTransform seasonContent, ScrollRect seasonScrollRect) = ScrollList("SeasonScroll", sidebar, 10f);
            var seasonScroll = (RectTransform)seasonScrollRect.transform;
            Stretch(seasonScroll, Vector2.zero, Vector2.one);
            seasonScroll.offsetMin = new Vector2(20f, 30f);
            seasonScroll.offsetMax = new Vector2(-20f, -200f);
            seasonContent.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, 4, 4);
            seasonContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Button seasonTemplate = ButtonWithLabel("SeasonButtonTemplate", seasonContent, "Mùa 1", new Vector2(300f, 64f), IdleButton, Color.white);
            seasonTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;

            // Right: title, countdown, status, header row, table.
            RectTransform main = Child("Main", canvas);
            Stretch(main, Vector2.zero, Vector2.one);
            main.offsetMin = new Vector2(400f, 30f);
            main.offsetMax = new Vector2(-40f, 0f);

            TextMeshProUGUI title = Text("Title", main, "BẢNG XẾP HẠNG", 54, Accent);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1200f, 80f));

            TextMeshProUGUI countdown = Text("Countdown", main, string.Empty, 32, Color.white);
            Place(countdown.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(1200f, 50f));

            TextMeshProUGUI status = Text("Status", main, string.Empty, 28, new Color(0.85f, 0.82f, 0.78f));
            Place(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -178f), new Vector2(1400f, 44f));

            RectTransform header = Child("HeaderRow", main);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = Vector2.one;
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 60f);
            header.anchoredPosition = new Vector2(0f, -215f);
            header.gameObject.AddComponent<Image>().color = HeaderRow;
            string[] headings = { "STT", "Username", "Rank", "Điểm", "Tỉ lệ thắng" };

            for (int column = 0; column < headings.Length; column++)
            {
                TextMeshProUGUI heading = Text("H_" + column, header, headings[column], 30, new Color(0.97f, 0.80f, 0.30f));
                Cell(heading.rectTransform, column);
                heading.fontStyle = FontStyles.Bold;
            }

            RectTransform frame = Child("TableFrame", main);
            Stretch(frame, Vector2.zero, Vector2.one);
            frame.offsetMin = Vector2.zero;
            frame.offsetMax = new Vector2(0f, -285f);
            frame.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);

            (RectTransform tableContent, ScrollRect tableScroll) = ScrollList("TableScroll", frame, RowSpacing);
            Stretch((RectTransform)tableScroll.transform, Vector2.zero, Vector2.one);

            LeaderboardRowView rowTemplate = BuildRow("RowTemplate", tableContent);
            rowTemplate.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;

            // Own row copy pinned to the bottom of the table frame (outside the scroll content).
            LeaderboardRowView pinned = BuildRow("PinnedSelfRow", frame);
            var pinnedRect = (RectTransform)pinned.transform;
            pinnedRect.anchorMin = Vector2.zero;
            pinnedRect.anchorMax = new Vector2(1f, 0f);
            pinnedRect.pivot = new Vector2(0.5f, 0f);
            pinnedRect.sizeDelta = new Vector2(0f, RowHeight);
            pinnedRect.anchoredPosition = Vector2.zero;
            Shadow shadow = pinned.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(0f, 8f);

            LeaderboardController controller = canvasObject.AddComponent<LeaderboardController>();
            controller.EditorWire(
                title,
                countdown,
                status,
                back,
                seasonContent,
                seasonTemplate,
                tableScroll,
                tableContent,
                rowTemplate,
                pinned,
                RowHeight,
                RowSpacing
            );

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static LeaderboardRowView BuildRow(string name, Transform parent)
        {
            RectTransform row = Child(name, parent);
            row.sizeDelta = new Vector2(0f, RowHeight);
            Image background = row.gameObject.AddComponent<Image>();
            background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            background.type = Image.Type.Sliced;
            Outline highlight = row.gameObject.AddComponent<Outline>();
            highlight.effectDistance = new Vector2(3f, -3f);
            highlight.enabled = false;

            RectTransform positionCell = Child("PositionCell", row);
            Cell(positionCell, 0);
            Image badge = Child("Badge", positionCell).gameObject.AddComponent<Image>();
            badge.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            badge.raycastTarget = false;
            Place(badge.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58f, 58f));
            TextMeshProUGUI position = Text("Position", positionCell, "1", 32, Color.white);
            Stretch(position.rectTransform, Vector2.zero, Vector2.one);

            TextMeshProUGUI username = Text("Username", row, "user000000", 30, Color.white);
            Cell(username.rectTransform, 1);
            // Centred; LeaderboardRowView also centres the rank icon + name group at runtime.
            username.alignment = TextAlignmentOptions.Center;
            username.textWrappingMode = TextWrappingModes.NoWrap;
            username.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform rankCell = Child("RankCell", row);
            Cell(rankCell, 2);
            Image icon = Child("RankIcon", rankCell).gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            icon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            icon.rectTransform.pivot = new Vector2(0f, 0.5f);
            icon.rectTransform.sizeDelta = new Vector2(64f, 64f);
            icon.rectTransform.anchoredPosition = new Vector2(16f, 0f);
            TextMeshProUGUI rankName = Text("RankName", rankCell, "Đồng", 30, Color.white);
            Stretch(rankName.rectTransform, Vector2.zero, Vector2.one);
            rankName.rectTransform.offsetMin = new Vector2(96f, 0f);
            rankName.alignment = TextAlignmentOptions.MidlineLeft;

            TextMeshProUGUI points = Text("Points", row, "0", 30, Color.white);
            Cell(points.rectTransform, 3);

            TextMeshProUGUI winRate = Text("WinRate", row, "—", 30, Color.white);
            Cell(winRate.rectTransform, 4);

            LeaderboardRowView view = row.gameObject.AddComponent<LeaderboardRowView>();
            view.EditorWire(background, highlight, badge, position, username, icon, rankName, points, winRate);
            return view;
        }

        /// <summary>Wheel-only vertical ScrollRect (same feel as the shop) with a VerticalLayoutGroup content.</summary>
        private static (RectTransform content, ScrollRect scroll) ScrollList(string name, Transform parent, float spacing)
        {
            RectTransform scrollRect = Child(name, parent);
            ShopScrollRect scroll = scrollRect.gameObject.AddComponent<ShopScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 40f;

            RectTransform viewport = Child("Viewport", scrollRect);
            Stretch(viewport, Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Child("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;

            scroll.viewport = viewport;
            scroll.content = content;
            return (content, scroll);
        }

        #endregion

        #region Main Menu

        private static void AddMainMenuButton()
        {
            Scene scene = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
            Button[] buttons = Resources.FindObjectsOfTypeAll<Button>()
                .Where(b => b.gameObject.scene == scene)
                .ToArray();

            if (buttons.Any(b => HasListener(b, nameof(MainMenuController.OpenLeaderboard))))
            {
                Debug.Log("[LeaderboardBuilder] MainMenu already has a Leaderboard button.");
                return;
            }

            Button shop = buttons.FirstOrDefault(b => HasListener(b, nameof(MainMenuController.OpenShop)));
            Button findMatch = buttons.FirstOrDefault(b => HasListener(b, nameof(MainMenuController.OpenFindMatch)));
            Button logout = buttons.FirstOrDefault(b => HasListener(b, nameof(MainMenuController.Logout)));
            Button template = shop != null ? shop : findMatch;

            if (template == null)
            {
                Debug.LogError(
                    "[LeaderboardBuilder] Could not find a MainMenu button to copy. Add the Leaderboard "
                        + "button manually (OnClick -> MainMenuController.OpenLeaderboard)."
                );
                return;
            }

            var controller = template.onClick.GetPersistentTarget(0) as MainMenuController;

            if (controller == null)
            {
                controller = Object.FindAnyObjectByType<MainMenuController>();
            }

            Button leaderboard = Object.Instantiate(template, template.transform.parent);
            leaderboard.name = "LeaderboardButton";
            leaderboard.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

            for (int i = leaderboard.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEventTools.RemovePersistentListener(leaderboard.onClick, i);
            }

            UnityEventTools.AddPersistentListener(leaderboard.onClick, new UnityAction(controller.OpenLeaderboard));

            TMP_Text label = leaderboard.GetComponentInChildren<TMP_Text>(true);

            if (label != null)
            {
                label.text = "Leaderboard";
            }

            // Vertical stack: take the slot below the template and push Logout one slot down.
            var templateRect = (RectTransform)template.transform;
            float spacing = 100f;

            if (shop != null && findMatch != null)
            {
                spacing = Mathf.Abs(((RectTransform)shop.transform).anchoredPosition.y
                    - ((RectTransform)findMatch.transform).anchoredPosition.y);
            }

            Vector2 slot = templateRect.anchoredPosition - new Vector2(0f, spacing);
            ((RectTransform)leaderboard.transform).anchoredPosition = slot;

            if (logout != null)
            {
                var logoutRect = (RectTransform)logout.transform;

                if (logoutRect.anchoredPosition.y >= slot.y - 1f)
                {
                    logoutRect.anchoredPosition = slot - new Vector2(0f, spacing);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[LeaderboardBuilder] Added LeaderboardButton to MainMenu. Adjust its position in the scene if needed.");
        }

        private static bool HasListener(Button button, string methodName)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) == methodName)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddToBuildSettings(string path)
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();

            if (scenes.Any(s => s.path == path))
            {
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        #endregion

        #region UI Helpers

        private static void Cell(RectTransform rect, int column)
        {
            rect.anchorMin = new Vector2(Columns[column].x, 0f);
            rect.anchorMax = new Vector2(Columns[column].y, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2? size = null)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;

            if (size.HasValue)
            {
                rect.sizeDelta = size.Value;
            }
        }

        private static TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color color)
        {
            RectTransform rect = Child(name, parent);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            if (font != null)
            {
                text.font = font;
            }

            return text;
        }

        private static Button ButtonWithLabel(
            string name,
            Transform parent,
            string label,
            Vector2 size,
            Color background,
            Color textColor
        )
        {
            RectTransform rect = Child(name, parent);
            Place(rect, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = background;
            Button button = rect.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
            button.colors = colors;

            TextMeshProUGUI text = Text("Label", rect, label, 30, textColor);
            Stretch(text.rectTransform, Vector2.zero, Vector2.one);
            return button;
        }

        #endregion
    }
}
#endif
