#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using KLTN.Game.Content;
using KLTN.Game.Presentation;
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
    /// Generates the Shop UI so it does not have to be assembled by hand:
    ///   - Assets/Prefabs/UI/ShopCardItem.prefab
    ///   - Assets/Scenes/ShopScene.unity (+ Build Settings entry)
    ///   - a "CỬA HÀNG" button on MainMenu wired to MainMenuController.OpenShop
    /// Safe to run again; it overwrites the generated prefab/scene and skips an existing menu button.
    /// </summary>
    public static class ShopSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/ShopScene.unity";
        private const string MainMenuPath = "Assets/Scenes/MainMenu.unity";
        private const string ItemPrefabPath = "Assets/Prefabs/UI/ShopCardItem.prefab";
        private const string CardPrefabPath = "Assets/Prefabs/Networking/NetworkCardView.prefab";
        private const string PresentationCatalogPath = "Assets/CardData/Presentation/CardPresentationCatalog.asset";
        private const string FontPath = "Assets/Fonts/XTypewriter-Bold SDF.asset";

        private static readonly Color Background = new Color(0.07f, 0.06f, 0.08f, 1f);
        private static readonly Color Panel = new Color(0.12f, 0.1f, 0.12f, 0.95f);
        private static readonly Color Overlay = new Color(0.03f, 0.02f, 0.03f, 0.85f);
        private static readonly Color Accent = new Color(0.78f, 0.62f, 0.25f, 1f);
        // Matches the red card frame; yellow label for contrast on the dark overlay.
        private static readonly Color BuyRed = new Color(0.55f, 0.13f, 0.11f, 1f);
        private static readonly Color BuyYellow = new Color(0.97f, 0.80f, 0.30f, 1f);
        private static readonly Color MutedButton = new Color(0.32f, 0.30f, 0.33f, 1f);

        private const float CellWidth = 240f;
        private const float CellHeight = 410f;

        private static TMP_FontAsset font;

        [MenuItem("Tools/KLTN/Build Shop Scene + Main Menu Button")]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<ShopCatalog>("Assets/Resources/ShopCatalog.asset") == null)
            {
                ShopCatalogInstaller.Install();
            }

            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            ShopCardItemView itemPrefab = BuildItemPrefab();
            BuildShopScene(itemPrefab);
            AddToBuildSettings(ScenePath);
            AddMainMenuButton();

            Debug.Log("[ShopBuilder] Done. Open MainMenu and press Play to test the shop.");
        }

        #region Item Prefab

        private static ShopCardItemView BuildItemPrefab()
        {
            System.IO.Directory.CreateDirectory("Assets/Prefabs/UI");

            var root = new GameObject("ShopCardItem", typeof(RectTransform), typeof(Image));
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(CellWidth, CellHeight);

            // Transparent image so the whole tile receives hover events.
            Image hitArea = root.GetComponent<Image>();
            hitArea.color = new Color(0f, 0f, 0f, 0f);

            RectTransform mount = Child("CardMount", root.transform);
            Stretch(mount, new Vector2(0f, 0.12f), Vector2.one);

            GameObject owned = OverlayOver(mount, "OwnedOverlay");
            TextMeshProUGUI ownedLabel = Text("Label", owned.transform, "ĐÃ SỞ HỮU", 30, Accent);
            Stretch(ownedLabel.rectTransform, Vector2.zero, Vector2.one);

            GameObject hover = OverlayOver(mount, "HoverOverlay");
            Button buy = ButtonWithLabel("BuyButton", hover.transform, "MUA", new Vector2(150f, 60f), BuyRed, BuyYellow);
            buy.gameObject.AddComponent<HoverScale>();

            TextMeshProUGUI price = Text("Price", root.transform, "000 xu", 30, Color.white);
            Stretch(price.rectTransform, Vector2.zero, new Vector2(1f, 0.11f));
            price.alignment = TextAlignmentOptions.Top;

            ShopCardItemView view = root.AddComponent<ShopCardItemView>();
            view.EditorWire(mount, hover, buy, owned, price);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ItemPrefabPath);
            Object.DestroyImmediate(root);
            return saved.GetComponent<ShopCardItemView>();
        }

        private static GameObject OverlayOver(RectTransform mount, string name)
        {
            var overlay = new GameObject(name, typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(mount.parent, false);
            var rect = (RectTransform)overlay.transform;
            rect.anchorMin = mount.anchorMin;
            rect.anchorMax = mount.anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = overlay.GetComponent<Image>();
            image.color = Overlay;
            image.raycastTarget = false;
            return overlay;
        }

        #endregion

        #region Scene

        private static void BuildShopScene(ShopCardItemView itemPrefab)
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

            // Top bar: back button | title | coins + silver.
            RectTransform topBar = Child("TopBar", canvas);
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = Vector2.one;
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.sizeDelta = new Vector2(0f, 110f);
            topBar.gameObject.AddComponent<Image>().color = Panel;

            Button back = ButtonWithLabel("BackButton", topBar, "< MENU", new Vector2(200f, 64f));
            Place((RectTransform)back.transform, new Vector2(0f, 0.5f), new Vector2(140f, 0f));

            TextMeshProUGUI title = Text("Title", topBar, "CỬA HÀNG", 52, Accent);
            Place(title.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 100f));

            TextMeshProUGUI coins = Text("Coins", topBar, "— xu", 38, new Color(1f, 0.85f, 0.35f));
            Place(coins.rectTransform, new Vector2(1f, 0.5f), new Vector2(-460f, 0f), new Vector2(280f, 80f));
            coins.alignment = TextAlignmentOptions.MidlineRight;

            TextMeshProUGUI silver = Text("Silver", topBar, "— bạc", 38, new Color(0.82f, 0.86f, 0.92f));
            Place(silver.rectTransform, new Vector2(1f, 0.5f), new Vector2(-160f, 0f), new Vector2(260f, 80f));
            silver.alignment = TextAlignmentOptions.MidlineRight;

            ShopTopBarView topBarView = topBar.gameObject.AddComponent<ShopTopBarView>();
            topBarView.EditorWire(coins, silver);

            // Sort / filter row.
            TMP_Dropdown dropdown = Dropdown(canvas);
            Place((RectTransform)dropdown.transform, new Vector2(0f, 1f), new Vector2(240f, -160f), new Vector2(380f, 60f));

            TextMeshProUGUI status = Text("Status", canvas, string.Empty, 30, Color.white);
            Place(status.rectTransform, new Vector2(1f, 1f), new Vector2(-520f, -160f), new Vector2(900f, 60f));
            status.alignment = TextAlignmentOptions.MidlineRight;

            // Scrollable grid of cards.
            RectTransform content = ScrollGrid(canvas);

            ShopConfirmDialog dialog = BuildConfirmDialog(canvas);

            ShopController controller = canvasObject.AddComponent<ShopController>();
            controller.EditorWire(
                topBarView,
                content,
                itemPrefab,
                dropdown,
                status,
                back,
                dialog,
                LoadCardPrefab(),
                AssetDatabase.LoadAssetAtPath<CardPresentationCatalog>(PresentationCatalogPath)
            );

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static ShopConfirmDialog BuildConfirmDialog(Transform canvas)
        {
            // Full-screen blocker so the list cannot be clicked while the modal is open.
            RectTransform blocker = Child("ConfirmDialog", canvas);
            Stretch(blocker, Vector2.zero, Vector2.one);
            Image dim = blocker.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);

            RectTransform panel = Child("Panel", blocker);
            Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 340f));
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            panelImage.type = Image.Type.Sliced;
            panelImage.color = Panel;
            Outline border = panel.gameObject.AddComponent<Outline>();
            border.effectColor = BuyRed;
            border.effectDistance = new Vector2(4f, -4f);

            TextMeshProUGUI message = Text("Message", panel, "Bạn có đồng ý mua thẻ ... không?", 34, Color.white);
            Stretch(message.rectTransform, new Vector2(0f, 0.42f), Vector2.one);
            message.rectTransform.offsetMin = new Vector2(40f, 0f);
            message.rectTransform.offsetMax = new Vector2(-40f, -30f);
            message.textWrappingMode = TextWrappingModes.Normal;

            Button accept = ButtonWithLabel("AcceptButton", panel, "Chấp nhận", new Vector2(230f, 66f), BuyRed, BuyYellow);
            Place((RectTransform)accept.transform, new Vector2(0.5f, 0f), new Vector2(-140f, 75f));
            accept.gameObject.AddComponent<HoverScale>();

            Button cancel = ButtonWithLabel("CancelButton", panel, "Hủy", new Vector2(230f, 66f), MutedButton, Color.white);
            Place((RectTransform)cancel.transform, new Vector2(0.5f, 0f), new Vector2(140f, 75f));
            cancel.gameObject.AddComponent<HoverScale>();

            ShopConfirmDialog dialog = canvas.gameObject.AddComponent<ShopConfirmDialog>();
            dialog.EditorWire(blocker.gameObject, message, accept, cancel);
            blocker.gameObject.SetActive(false);
            blocker.SetAsLastSibling();
            return dialog;
        }

        private static RectTransform ScrollGrid(Transform canvas)
        {
            RectTransform scroll = Child("CardScroll", canvas);
            Stretch(scroll, Vector2.zero, Vector2.one);
            scroll.offsetMin = new Vector2(40f, 30f);
            scroll.offsetMax = new Vector2(-40f, -210f);
            scroll.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            // Wheel-only, clamped, no inertia: no press-drag and no bounce at the ends.
            ShopScrollRect scrollRect = scroll.gameObject.AddComponent<ShopScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = false;
            scrollRect.scrollSensitivity = 40f;

            RectTransform viewport = Child("Viewport", scroll);
            Stretch(viewport, Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Child("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;

            GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CellWidth, CellHeight);
            grid.spacing = new Vector2(40f, 72f);
            grid.padding = new RectOffset(40, 40, 36, 48);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport;
            scrollRect.content = content;
            return content;
        }

        private static NetworkCardVisual LoadCardPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CardPrefabPath);
            NetworkCardVisual visual = prefab != null ? prefab.GetComponent<NetworkCardVisual>() : null;

            if (visual == null)
            {
                Debug.LogWarning($"[ShopBuilder] Card prefab not found at {CardPrefabPath}. Assign ShopController.cardVisualPrefab manually.");
            }

            return visual;
        }

        #endregion

        #region Main Menu

        private static void AddMainMenuButton()
        {
            Scene scene = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
            Button[] buttons = Resources.FindObjectsOfTypeAll<Button>()
                .Where(b => b.gameObject.scene == scene)
                .ToArray();

            if (buttons.Any(b => HasListener(b, nameof(MainMenuController.OpenShop))))
            {
                Debug.Log("[ShopBuilder] MainMenu already has a Shop button.");
                return;
            }

            Button findMatch = buttons.FirstOrDefault(b => HasListener(b, nameof(MainMenuController.OpenFindMatch)));
            Button hostClient = buttons.FirstOrDefault(b => HasListener(b, nameof(MainMenuController.OpenHostClientMenu)));
            Button template = hostClient != null ? hostClient : findMatch;

            if (template == null)
            {
                Debug.LogError("[ShopBuilder] Could not find a MainMenu button to copy. Add the Shop button manually (OnClick -> MainMenuController.OpenShop).");
                return;
            }

            var controller = template.onClick.GetPersistentTarget(0) as MainMenuController;

            if (controller == null)
            {
                controller = Object.FindAnyObjectByType<MainMenuController>();
            }

            Button shop = Object.Instantiate(template, template.transform.parent);
            shop.name = "ShopButton";
            shop.transform.SetSiblingIndex(template.transform.GetSiblingIndex());

            for (int i = shop.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEventTools.RemovePersistentListener(shop.onClick, i);
            }

            UnityEventTools.AddPersistentListener(shop.onClick, new UnityAction(controller.OpenShop));

            TMP_Text label = shop.GetComponentInChildren<TMP_Text>(true);

            if (label != null)
            {
                label.text = "CỬA HÀNG";
            }

            // Place above the top-most existing button, using the existing vertical spacing.
            var templateRect = (RectTransform)template.transform;
            float spacing = 130f;

            if (findMatch != null && hostClient != null)
            {
                spacing = Mathf.Abs(((RectTransform)hostClient.transform).anchoredPosition.y
                    - ((RectTransform)findMatch.transform).anchoredPosition.y);
            }

            ((RectTransform)shop.transform).anchoredPosition = templateRect.anchoredPosition + new Vector2(0f, spacing);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ShopBuilder] Added ShopButton to MainMenu. Adjust its position in the scene if needed.");
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

        private static Button ButtonWithLabel(string name, Transform parent, string label, Vector2 size)
        {
            return ButtonWithLabel(name, parent, label, size, Accent, Color.black);
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

        private static TMP_Dropdown Dropdown(Transform parent)
        {
            var resources = new TMP_DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };

            GameObject go = TMP_DefaultControls.CreateDropdown(resources);
            go.name = "SortDropdown";
            go.transform.SetParent(parent, false);

            foreach (TMP_Text text in go.GetComponentsInChildren<TMP_Text>(true))
            {
                text.fontSize = 26;

                if (font != null)
                {
                    text.font = font;
                }
            }

            // Default template rows are 20 px high; make room for 26 pt text.
            if (go.transform.Find("Template") is RectTransform template)
            {
                template.sizeDelta = new Vector2(0f, 400f);
            }

            if (go.transform.Find("Template/Viewport/Content") is RectTransform list)
            {
                list.sizeDelta = new Vector2(0f, 54f);
            }

            if (go.transform.Find("Template/Viewport/Content/Item") is RectTransform row)
            {
                row.sizeDelta = new Vector2(0f, 50f);
            }

            return go.GetComponent<TMP_Dropdown>();
        }

        #endregion
    }
}
#endif
