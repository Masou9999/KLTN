#if UNITY_EDITOR
using System;
using System.Linq;
using KLTN.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Builds the match result flow (loading overlay, banner Stage 1, rank Stage 2) under
    /// the GameScene result canvas and wires <see cref="MatchResultPresenter"/>.
    /// Re-running replaces the generated root; the previous result objects are disabled,
    /// not deleted, so their serialized data stays recoverable.
    /// </summary>
    public static class MatchResultUiInstaller
    {
        #region Configuration

        private const string ScenePath = "Assets/Scenes/GameScene.unity";
        private const string ArtFolder = "Assets/Art/Processed/UI/MatchResult/";
        private const string FontPath = "Assets/Ui/ImproveLogin/Fonts/BeVietnamPro-SemiBold SDF.asset";
        private const string SpinnerPath = "Assets/Ui/loading-spinner.png";
        private const string RootName = "MatchResultFlow";

        // Banner art is 806x960. Slot interiors are measured per banner in banner pixels
        // (top-left origin) so each artwork keeps its own layout.
        private static readonly Vector2 BannerSize = new Vector2(806f, 960f);
        private static readonly (string name, float[] x, float y)[] BannerSlots =
        {
            ("BannerVictory", new[] { 238f, 400f, 563f }, 791.5f),
            ("BannerDefeat", new[] { 238f, 400.5f, 563.5f }, 791.5f),
            ("BannerDraw", new[] { 238f, 400.5f, 563f }, 792.5f),
        };
        private static readonly Vector2 SlotSize = new Vector2(141f, 124f);
        private const float ValuePaddingRight = 4f; // screen px; reward digits read right-heavy
        private const float BannerScale = 0.84f;
        private const float BannerOffsetY = 46f;
        private const float BottomButtonY = -424f; // shared by Continue and Exit
        private const float Stage2Scale = 0.72f;

        private static readonly Color Cream = new Color32(247, 231, 180, 255);
        private static readonly Color SoftWhite = new Color32(235, 230, 220, 255);

        #endregion

        #region Public Commands

        [MenuItem("Tools/KLTN/Install Match Result UI")]
        public static void Install()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            ConfigureTextures();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var presenter = Object.FindAnyObjectByType<MatchResultPresenter>(FindObjectsInactive.Include);
            if (presenter == null) throw new InvalidOperationException("MatchResultPresenter is missing.");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) throw new InvalidOperationException("Be Vietnam Pro font is missing.");

            Transform canvas = presenter.transform;
            Transform old = canvas.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            foreach (Transform child in canvas)
            {
                if (child.gameObject.activeSelf)
                {
                    Debug.Log($"[MatchResultUi] Disabling legacy result object '{child.name}'.");
                    child.gameObject.SetActive(false);
                }
            }

            var root = CreateRect(RootName, canvas);
            Stretch(root);
            var dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.92f));
            Stretch(dim.rectTransform);
            dim.raycastTarget = true; // blocks the board behind the result

            var loading = BuildLoading(root, font);
            var banner = BuildBanner(root, font);
            var progress = BuildProgress(root, font);

            var serialized = new SerializedObject(presenter);
            serialized.FindProperty("dimBackground").objectReferenceValue = dim.gameObject;
            serialized.FindProperty("loadingView").objectReferenceValue = loading;
            serialized.FindProperty("bannerView").objectReferenceValue = banner;
            serialized.FindProperty("progressView").objectReferenceValue = progress;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            root.gameObject.SetActive(true);
            dim.gameObject.SetActive(false);
            loading.gameObject.SetActive(false);
            banner.gameObject.SetActive(false);
            progress.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MatchResultUi] Match result UI installed in GameScene.");
            Validate();
        }

        [MenuItem("Tools/KLTN/Validate Match Result UI")]
        public static void Validate()
        {
            var scene = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                MatchResultPresenter presenter = scene.GetRootGameObjects()
                    .Select(x => x.GetComponentInChildren<MatchResultPresenter>(true))
                    .FirstOrDefault(x => x != null);
                Require(presenter != null, "Missing MatchResultPresenter");
                RequireReferences(presenter, "dimBackground", "loadingView", "bannerView",
                    "progressView");
                RequireReferences(presenter.GetComponentInChildren<MatchResultLoadingView>(true),
                    "group", "spinner", "label", "skipButton");
                RequireReferences(presenter.GetComponentInChildren<MatchResultBannerView>(true),
                    "group", "content", "statusText", "continueButton");
                var bannerView = new SerializedObject(
                    presenter.GetComponentInChildren<MatchResultBannerView>(true));
                foreach (string layout in new[] { "victoryLayout", "defeatLayout", "drawLayout" })
                    foreach (string field in new[] { "root", "coinsValue", "silverValue", "pointsValue" })
                        Require(bannerView.FindProperty(layout + "." + field)
                                ?.objectReferenceValue != null,
                            $"MatchResultBannerView.{layout}.{field} is not assigned");
                RequireReferences(presenter.GetComponentInChildren<RankProgressView>(true),
                    "group", "body", "bodyGroup", "barGroup", "currentIcon", "nextIcon",
                    "pointsText", "barArea", "backdrop", "fill", "tipGlow", "maxGroup",
                    "maxIcon", "maxPoints", "promoIcon", "promoIconImage", "rays", "glow",
                    "backSparkles", "frontSparkles", "exitGroup", "exitButton");
                var fill = presenter.GetComponentInChildren<RankProgressView>(true)
                    .transform.Find("Body/BarLayout/Bar/BarArea/Fill").GetComponent<Image>();
                Require(fill.type == UnityEngine.UI.Image.Type.Filled
                    && fill.fillMethod == UnityEngine.UI.Image.FillMethod.Horizontal
                    && fill.fillOrigin == (int)UnityEngine.UI.Image.OriginHorizontal.Left,
                    "Rank fill must be a horizontal fill from the left");
                foreach (var text in presenter.GetComponentsInChildren<TMP_Text>(true)
                             .Where(x => x.transform.IsChildOf(presenter.transform.Find(RootName))))
                    Require(text.font != null && text.font.name.StartsWith("BeVietnamPro"),
                        $"{text.name} must use Be Vietnam Pro");
                Debug.Log("[MatchResultUi] Validation passed.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        #endregion

        #region Loading

        private static MatchResultLoadingView BuildLoading(RectTransform parent, TMP_FontAsset font)
        {
            var root = CreateRect("Loading", parent);
            Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            var view = root.gameObject.AddComponent<MatchResultLoadingView>();

            var spinnerSprite = AssetDatabase.LoadAllAssetsAtPath(SpinnerPath).OfType<Sprite>().First();
            var spinner = CreateImage("LoadingSpinner", root, spinnerSprite, Color.white);
            spinner.rectTransform.sizeDelta = new Vector2(96f, 96f);
            spinner.rectTransform.anchoredPosition = new Vector2(0f, 50f);
            spinner.preserveAspect = true;
            spinner.gameObject.AddComponent<LoadingSpinner>();

            var label = CreateText("LoadingLabel", root, font, MatchResultLoadingView.LoadingMessage, 34f,
                Color.white, new Vector2(1000f, 60f), new Vector2(0f, -40f));

            var skipText = CreateText("SkipButton", root, font,
                "<u>Bỏ qua thông tin kết quả trận đấu</u>", 26f, SoftWhite,
                new Vector2(700f, 50f), new Vector2(0f, -130f));
            skipText.richText = true;
            skipText.raycastTarget = true;
            var skip = skipText.gameObject.AddComponent<Button>();
            skip.targetGraphic = skipText;
            var colors = skip.colors;
            colors.highlightedColor = new Color(1f, 0.85f, 0.55f, 1f);
            colors.pressedColor = new Color(0.8f, 0.7f, 0.5f, 1f);
            skip.colors = colors;

            Assign(view, ("group", group), ("spinner", spinner.gameObject), ("label", label),
                ("skipButton", skip));
            return view;
        }

        #endregion

        #region Stage 1

        private static MatchResultBannerView BuildBanner(RectTransform parent, TMP_FontAsset font)
        {
            var root = CreateRect("Stage1", parent);
            Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            var view = root.gameObject.AddComponent<MatchResultBannerView>();

            var content = CreateRect("Content", root);
            content.sizeDelta = BannerSize;
            content.anchoredPosition = new Vector2(0f, BannerOffsetY);
            content.localScale = Vector3.one * BannerScale;

            var serializedView = new SerializedObject(view);
            foreach (var (bannerName, slotsX, slotY) in BannerSlots)
            {
                var layout = CreateRect(bannerName, content);
                layout.sizeDelta = BannerSize;
                var art = CreateImage("Art", layout, LoadSprite(bannerName), Color.white);
                art.rectTransform.sizeDelta = BannerSize;
                var coin = Slot(layout, font, 0, slotsX[0], slotY, LoadSprite("RewardCoin"),
                    new Vector2(100f, 69f));
                var silver = Slot(layout, font, 1, slotsX[1], slotY, LoadSprite("RewardSilver"),
                    new Vector2(100f, 69f));
                var points = Slot(layout, font, 2, slotsX[2], slotY, LoadSprite("RewardRankGold"),
                    new Vector2(110f, 87f));
                string field = bannerName switch
                {
                    "BannerVictory" => "victoryLayout",
                    "BannerDefeat" => "defeatLayout",
                    _ => "drawLayout",
                };
                serializedView.FindProperty(field + ".root").objectReferenceValue = layout.gameObject;
                serializedView.FindProperty(field + ".coinsValue").objectReferenceValue = coin.value;
                serializedView.FindProperty(field + ".silverValue").objectReferenceValue = silver.value;
                serializedView.FindProperty(field + ".pointsValue").objectReferenceValue = points.value;
                layout.gameObject.SetActive(bannerName == "BannerVictory");
            }
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            var status = CreateText("Status", content, font, string.Empty, 30f, Cream,
                new Vector2(680f, 40f), BannerPoint(403f, 938f));

            var button = PlaqueButton("ContinueButton", content, font, "Tiếp tục",
                LoadSprite("ButtonRed"), new Vector2(0f, (BottomButtonY - BannerOffsetY) / BannerScale));
            // Cancels the banner scale so Continue matches the Stage 2 Exit button.
            button.transform.localScale = Vector3.one / BannerScale;

            Assign(view, ("group", group), ("content", content),
                ("statusText", status), ("continueButton", button));
            return view;
        }

        private static (Image icon, TMP_Text value) Slot(RectTransform parent,
            TMP_FontAsset font, int index, float centerX, float centerY, Sprite sprite,
            Vector2 iconSize)
        {
            var slot = CreateRect($"Slot{index}", parent);
            slot.sizeDelta = SlotSize;
            slot.anchoredPosition = BannerPoint(centerX, centerY);
            var icon = CreateImage("Icon", slot, sprite, Color.white);
            icon.rectTransform.sizeDelta = iconSize;
            icon.rectTransform.anchoredPosition = new Vector2(0f, 14f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var value = CreateText("Value", slot, font, MatchResultRewards.Missing, 40f, Color.white,
                new Vector2(SlotSize.x - 6f, 48f), new Vector2(0f, -37f));
            // Margin is in banner units; the banner is scaled by BannerScale on screen.
            value.margin = new Vector4(0f, 0f, ValuePaddingRight / BannerScale, 0f);
            return (icon, value);
        }

        private static Vector2 BannerPoint(float x, float y)
        {
            return new Vector2(x - BannerSize.x / 2f, BannerSize.y / 2f - y);
        }

        #endregion

        #region Stage 2

        private static RankProgressView BuildProgress(RectTransform parent, TMP_FontAsset font)
        {
            var root = CreateRect("Stage2", parent);
            Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            var view = root.gameObject.AddComponent<RankProgressView>();

            var body = CreateRect("Body", root);
            body.sizeDelta = new Vector2(1500f, 420f);
            body.localScale = Vector3.one * Stage2Scale;
            var bodyGroup = body.gameObject.AddComponent<CanvasGroup>();

            // Progress layout: icon · points over bar · next icon.
            var barLayout = CreateRect("BarLayout", body);
            Stretch(barLayout);
            var barGroup = barLayout.gameObject.AddComponent<CanvasGroup>();
            var current = CreateImage("CurrentIcon", barLayout, null, Color.white);
            current.rectTransform.sizeDelta = new Vector2(190f, 190f);
            current.rectTransform.anchoredPosition = new Vector2(-590f, 0f);
            current.preserveAspect = true;
            var next = CreateImage("NextIcon", barLayout, null, Color.white);
            next.rectTransform.sizeDelta = new Vector2(190f, 190f);
            next.rectTransform.anchoredPosition = new Vector2(590f, 0f);
            next.preserveAspect = true;
            var pointsText = CreateText("PointsText", barLayout, font, "0/60", 64f, Cream,
                new Vector2(700f, 90f), new Vector2(0f, 92f));
            pointsText.fontStyle = FontStyles.Italic;

            var bar = CreateImage("Bar", barLayout, LoadSprite("BarFrame"), Color.white);
            bar.rectTransform.sizeDelta = new Vector2(920f, 64f);
            var barArea = CreateRect("BarArea", bar.rectTransform);
            barArea.sizeDelta = new Vector2(896f, 40f);
            var backdrop = Filled("Backdrop", barArea, LoadSprite("BarBackdrop"));
            var fill = Filled("Fill", barArea, LoadSprite("BarFill"));
            var tip = CreateImage("TipGlow", barArea, LoadSprite("FxGlow"), new Color(1f, 0.88f, 0.5f, 1f));
            tip.rectTransform.sizeDelta = new Vector2(140f, 140f);

            // Top tier layout: icon and text column centred as one group.
            var maxLayout = CreateRect("MaxLayout", body);
            var maxGroup = maxLayout.gameObject.AddComponent<CanvasGroup>();
            var row = maxLayout.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = 40f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            Fit(maxLayout);
            var maxIcon = CreateImage("MaxIcon", maxLayout, null, Color.white);
            maxIcon.preserveAspect = true;
            var iconSize = maxIcon.gameObject.AddComponent<LayoutElement>();
            iconSize.preferredWidth = 230f;
            iconSize.preferredHeight = 230f;
            var column = CreateRect("MaxText", maxLayout);
            var stack = column.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.childAlignment = TextAnchor.MiddleLeft;
            stack.spacing = 14f;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = false;
            stack.childForceExpandHeight = false;
            var maxPoints = CreateText("MaxPoints", column, font, "0", 72f, Cream,
                new Vector2(400f, 90f), Vector2.zero);
            maxPoints.alignment = TextAlignmentOptions.Left;
            var caption = CreateText("MaxCaption", column, font, "Bạn đang ở mức xếp hạng cao nhất!",
                34f, SoftWhite, new Vector2(700f, 50f), Vector2.zero);
            caption.alignment = TextAlignmentOptions.Left;

            // Promotion stage sits outside Body so the demotion drift never moves it.
            var promo = CreateRect("PromoStage", root);
            promo.sizeDelta = new Vector2(10f, 10f);
            promo.localScale = Vector3.one * Stage2Scale;
            var back = Emitter("BackSparkles", promo, new Vector2(14f, 40f),
                new Vector2(90f, 330f), new Vector2(0.6f, 1.4f), 70f,
                new Color(1f, 0.92f, 0.7f, 1f));
            var rays = CreateImage("Rays", promo, LoadSprite("FxRays"), new Color(1f, 0.95f, 0.8f, 0f));
            rays.rectTransform.sizeDelta = new Vector2(680f, 680f);
            var glow = CreateImage("Glow", promo, LoadSprite("FxGlow"), new Color(1f, 0.9f, 0.6f, 0f));
            glow.rectTransform.sizeDelta = new Vector2(560f, 560f);
            var promoIcon = CreateImage("PromoIcon", promo, null, Color.white);
            promoIcon.rectTransform.sizeDelta = new Vector2(190f, 190f);
            promoIcon.preserveAspect = true;
            var front = Emitter("FrontSparkles", promo, new Vector2(10f, 30f),
                new Vector2(30f, 170f), new Vector2(0.4f, 0.9f), 120f, Color.white);

            var exit = PlaqueButton("ExitButton", root, font, "Thoát", LoadSprite("ButtonDark"),
                new Vector2(0f, BottomButtonY));
            var exitGroup = exit.gameObject.AddComponent<CanvasGroup>();

            Assign(view, ("group", group), ("body", body), ("bodyGroup", bodyGroup),
                ("barGroup", barGroup), ("currentIcon", current), ("nextIcon", next),
                ("pointsText", pointsText), ("barArea", barArea), ("backdrop", backdrop),
                ("fill", fill), ("tipGlow", tip), ("maxGroup", maxGroup), ("maxIcon", maxIcon),
                ("maxPoints", maxPoints), ("promoIcon", promoIcon.rectTransform),
                ("promoIconImage", promoIcon), ("rays", rays), ("glow", glow),
                ("backSparkles", back), ("frontSparkles", front), ("exitGroup", exitGroup),
                ("exitButton", exit));
            return view;
        }

        private static Image Filled(string name, RectTransform parent, Sprite sprite)
        {
            var image = CreateImage(name, parent, sprite, Color.white);
            Stretch(image.rectTransform);
            image.type = UnityEngine.UI.Image.Type.Filled;
            image.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            image.fillOrigin = (int)UnityEngine.UI.Image.OriginHorizontal.Left;
            image.fillAmount = 0f;
            return image;
        }

        private static UiSparkleEmitter Emitter(string name, RectTransform parent,
            Vector2 size, Vector2 speed, Vector2 lifetime, float radius, Color tint)
        {
            var rect = CreateRect(name, parent);
            var emitter = rect.gameObject.AddComponent<UiSparkleEmitter>();
            Assign(emitter, ("sprite", LoadSprite("FxSparkle")));
            var serialized = new SerializedObject(emitter);
            serialized.FindProperty("sizeRange").vector2Value = size;
            serialized.FindProperty("speedRange").vector2Value = speed;
            serialized.FindProperty("lifetimeRange").vector2Value = lifetime;
            serialized.FindProperty("spawnRadius").floatValue = radius;
            serialized.FindProperty("tint").colorValue = tint;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return emitter;
        }

        #endregion

        #region Helpers

        private static void ConfigureTextures()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtFolder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.spriteBorder = Vector4.zero;
                importer.SaveAndReimport();
            }
        }

        private static Sprite LoadSprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + name + ".png");
            if (sprite == null) throw new InvalidOperationException($"Missing sprite {name}.");
            return sprite;
        }

        private static Button PlaqueButton(string name, RectTransform parent, TMP_FontAsset font,
            string label, Sprite sprite, Vector2 position)
        {
            var image = CreateImage(name, parent, sprite, Color.white);
            // Simple, not sliced: the notched corners distort when the plaque is squeezed.
            image.type = UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = true;
            image.rectTransform.sizeDelta =
                new Vector2(330f, 330f * sprite.rect.height / sprite.rect.width);
            image.rectTransform.anchoredPosition = position;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            // Normal is slightly dimmed so hover can brighten visibly.
            var colors = button.colors;
            colors.normalColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var text = CreateText("Label", image.rectTransform, font, label, 40f, Cream,
                new Vector2(300f, 80f), Vector2.zero);
            text.fontStyle = FontStyles.Italic;
            return button;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, TMP_FontAsset font,
            string value, float size, Color color, Vector2 box, Vector2 position)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            rect.sizeDelta = box;
            rect.anchoredPosition = position;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Fit(RectTransform rect)
        {
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private static void Assign(Object target, params (string field, Object value)[] values)
        {
            var serialized = new SerializedObject(target);
            foreach (var (field, value) in values)
            {
                var property = serialized.FindProperty(field);
                if (property == null)
                    throw new InvalidOperationException($"{target.GetType().Name}.{field} not found.");
                property.objectReferenceValue = value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RequireReferences(Object target, params string[] fields)
        {
            Require(target != null, "Missing result view");
            var serialized = new SerializedObject(target);
            foreach (string field in fields)
                Require(serialized.FindProperty(field)?.objectReferenceValue != null,
                    $"{target.GetType().Name}.{field} is not assigned");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        #endregion
    }
}
#endif
