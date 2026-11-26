#if UNITY_EDITOR
using System.Linq;
using KLTN.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Builds the MainMenu play-mode modal from the supplied PlayMode sprites.
    /// </summary>
    /// <remarks>
    /// The modal is rebuilt as serialized scene objects. Object names are kept stable
    /// because <see cref="MenuPolishValidation"/> resolves the buttons by name.
    /// </remarks>
    public static class PlayModeModalInstaller
    {
        #region Configuration

        private const string Art = "Assets/Ui/PlayMode/Sprites/";
        public static readonly Vector2 FrameSize = new Vector2(1300, 780);
        public static readonly Vector2 CardSize = new Vector2(480, 446);
        public const float CardOffset = 290;
        public const float CardY = -15;
        public const float HoverScale = 1.1f;
        public const float FrameScale = 1.2f;

        private static readonly Color Cream = new Color(0.91f, 0.85f, 0.74f);
        private static readonly Color LabelInk = new Color(0.23f, 0.13f, 0.19f);
        private static readonly Color BorderNormal = new Color32(226, 118, 109, 255);
        private static readonly Color BorderHighlight = new Color32(255, 150, 132, 255);
        private static readonly Color GlowTint = new Color32(226, 118, 109, 0);
        private static readonly Color LabelHighlight = new Color32(166, 44, 50, 255);

        #endregion

        #region Public Commands

        [MenuItem("Tools/KLTN/Install Play Mode Modal")]
        public static void Install()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            Canvas canvas = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)
                .First(c => c.isRootCanvas && c.GetComponent<PlayModeModal>() != null);
            var controller = Object.FindAnyObjectByType<MainMenuController>();
            Button play = Find<Button>("PlayButton");
            GameObject overlay = Build(canvas, controller, play,
                play.GetComponentInChildren<TMP_Text>().font);
            MenuPolishInstaller.InstallButtonSounds();
            EditorSceneManager.SaveScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            RenderPreviews(overlay);
            Debug.Log("[PlayModeModal] Installed MainMenu play-mode modal.");
        }

        /// <summary>
        /// Replaces the existing modal and rewires <see cref="PlayModeModal"/>.
        /// </summary>
        /// <returns>The inactive overlay root.</returns>
        /// <remarks>
        /// Does not touch the Play button's listeners; the caller owns that wiring.
        /// </remarks>
        public static GameObject Build(Canvas canvas, MainMenuController controller,
            Button play, TMP_FontAsset font)
        {
            ImportSprites();
            Transform old = Find<Transform>("PlayModeOverlay");
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }

            var menuGroup = canvas.GetComponent<CanvasGroup>();
            if (menuGroup == null)
            {
                menuGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            }
            var modal = canvas.GetComponent<PlayModeModal>();
            if (modal == null)
            {
                modal = canvas.gameObject.AddComponent<PlayModeModal>();
            }

            RectTransform overlay = Rect("PlayModeOverlay", canvas.transform, 0, 0, 0, 0);
            Stretch(overlay);
            overlay.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.72f);
            overlay.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;

            RectTransform frame = Rect("ModeFrame", overlay, 0, 0,
                FrameSize.x, FrameSize.y);
            // Uniform scale keeps every proportion; 1300×780 becomes 1560×936.
            frame.localScale = Vector3.one * FrameScale;
            Image background = Picture("Background", frame, "modal_background",
                0, 0, 0, 0);
            Stretch((RectTransform)background.transform);
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;
            Corners(frame);
            Title(frame, font);

            Button privateRoom = ModeOption(frame, "PrivateRoomOption", "Phòng Riêng",
                "private_room_icon", -CardOffset, font);
            Button match = ModeOption(frame, "MatchmakingOption", "Ghép Trận",
                "matchmaking_icon", CardOffset, font);
            UnityEventTools.AddPersistentListener(privateRoom.onClick,
                controller.OpenHostClientMenu);
            UnityEventTools.AddPersistentListener(match.onClick,
                controller.OpenFindMatch);

            Picture("Divider", frame, "divider", 0, -335, 300, 44);

            // Centred on the frame's top-right corner: half inside, half outside.
            Image closeImage = Picture("CloseModeButton", frame, "close_button",
                0, 0, 80, 80);
            var closeRect = (RectTransform)closeImage.transform;
            closeRect.anchorMin = closeRect.anchorMax = Vector2.one;
            closeImage.raycastTarget = true;
            Button close = closeImage.gameObject.AddComponent<Button>();
            close.targetGraphic = closeImage;
            var colors = close.colors;
            colors.highlightedColor = new Color(1, 0.87f, 0.8f);
            colors.pressedColor = new Color(0.75f, 0.6f, 0.55f);
            close.colors = colors;
            UnityEventTools.AddPersistentListener(close.onClick, modal.Close);

            Set(modal, "modalRoot", overlay.gameObject);
            Set(modal, "menuGroup", menuGroup);
            Set(modal, "playButton", play);
            Set(modal, "firstOption", privateRoom);
            overlay.gameObject.SetActive(false);
            return overlay.gameObject;
        }

        #endregion

        #region Construction

        private static void Corners(Transform frame)
        {
            float x = FrameSize.x * 0.5f - 60;
            float y = FrameSize.y * 0.5f - 57;
            for (int i = 0; i < 4; i++)
            {
                bool right = i % 2 == 1;
                bool bottom = i >= 2;
                Image corner = Picture("Corner" + i, frame, "corner",
                    right ? x : -x, bottom ? -y : y, 84, 79);
                // corner.png is drawn for the top-left corner.
                corner.transform.localScale =
                    new Vector3(right ? -1 : 1, bottom ? -1 : 1, 1);
            }
        }

        private static void Title(Transform frame, TMP_FontAsset font)
        {
            TextMeshProUGUI title = Label("Title", frame, "CHỌN CHẾ ĐỘ", font, 52,
                Cream, 0, 305, 560, 80);
            title.fontStyle = FontStyles.Bold;
            Picture("TitleLineLeft", frame, "title_line", -275, 305, 135, 27);
            Image right = Picture("TitleLineRight", frame, "title_line",
                275, 305, 135, 27);
            right.transform.localScale = new Vector3(-1, 1, 1);
        }

        private static Button ModeOption(Transform frame, string name, string label,
            string icon, float x, TMP_FontAsset font)
        {
            // The root keeps a fixed, invisible hit area; only Visual is scaled.
            RectTransform root = Rect(name, frame, x, CardY, CardSize.x, CardSize.y);
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;

            RectTransform visual = Rect("Visual", root, 0, 0, CardSize.x, CardSize.y);
            Picture("CardFill", visual, "mode_background", 0, 0, CardSize.x, CardSize.y);
            // Sits between the fill and the border/artwork, so it never covers assets.
            Image glow = Picture("HoverGlow", visual, "mode_hover_glow", 0, 0,
                CardSize.x, CardSize.y);
            glow.color = GlowTint;
            Image border = Picture("CardBorder", visual, "mode_border", 0, 0,
                CardSize.x, CardSize.y);
            border.color = BorderNormal;
            Image art = Picture("Illustration", visual, icon, 0, 60, 345, 285);
            art.preserveAspect = true;
            RectTransform plate = (RectTransform)Picture("LabelPlate", visual,
                "blank_label", 0, -150, 340, 118).transform;
            TextMeshProUGUI text = Label("Label", plate, label, font, 34, LabelInk,
                0, 2, 300, 72);
            text.fontStyle = FontStyles.Bold;

            var hover = root.gameObject.AddComponent<PlayModeOptionHover>();
            Set(hover, "visual", visual);
            Set(hover, "border", border);
            var serialized = new SerializedObject(hover);
            serialized.FindProperty("normalBorder").colorValue = BorderNormal;
            serialized.FindProperty("highlightBorder").colorValue = BorderHighlight;
            serialized.FindProperty("highlightScale").floatValue = HoverScale;
            serialized.FindProperty("glow").objectReferenceValue = glow;
            serialized.FindProperty("label").objectReferenceValue = text;
            serialized.FindProperty("normalLabel").colorValue = LabelInk;
            serialized.FindProperty("highlightLabel").colorValue = LabelHighlight;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return button;
        }

        #endregion

        #region Previews

        private static void RenderPreviews(GameObject overlay)
        {
            overlay.SetActive(true);
            MenuPolishInstaller.Preview("PlayModeModal");
            var card = Find<PlayModeOptionHover>("MatchmakingOption");
            var serialized = new SerializedObject(card);
            var visual = (RectTransform)serialized.FindProperty("visual")
                .objectReferenceValue;
            var border = (Graphic)serialized.FindProperty("border").objectReferenceValue;
            var glow = (Graphic)serialized.FindProperty("glow").objectReferenceValue;
            var label = (Graphic)serialized.FindProperty("label").objectReferenceValue;
            Color normal = border.color;
            visual.localScale = Vector3.one * HoverScale;
            border.color = BorderHighlight;
            glow.color = new Color(GlowTint.r, GlowTint.g, GlowTint.b,
                serialized.FindProperty("glowAlpha").floatValue);
            label.color = LabelHighlight;
            MenuPolishInstaller.Preview("PlayModeModalHover");
            visual.localScale = Vector3.one;
            border.color = normal;
            glow.color = GlowTint;
            label.color = LabelInk;
            overlay.SetActive(false);
        }

        #endregion

        #region Sprite Import

        private static void ImportSprites()
        {
            foreach (string name in new[]
            {
                "modal_background", "corner", "title_line", "divider", "mode_background",
                "mode_border", "mode_hover_glow", "private_room_icon",
                "matchmaking_icon", "blank_label",
                "close_button"
            })
            {
                var importer =
                    (TextureImporter)AssetImporter.GetAtPath(Art + name + ".png");
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                // Rounded corners stay intact when the frame is stretched.
                importer.spriteBorder = name == "modal_background"
                    ? new Vector4(48, 48, 48, 48) : Vector4.zero;
                importer.SaveAndReimport();
            }
        }

        #endregion

        #region Helpers

        private static RectTransform Rect(string name, Transform parent,
            float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform))
                .GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Image Picture(string name, Transform parent, string sprite,
            float x, float y, float width, float height)
        {
            var image = Rect(name, parent, x, y, width, height).gameObject
                .AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + sprite + ".png");
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Label(string name, Transform parent, string value,
            TMP_FontAsset font, float size, Color color,
            float x, float y, float width, float height)
        {
            var text = Rect(name, parent, x, y, width, height).gameObject
                .AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        private static T Find<T>(string name) where T : Component
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include)
                .FirstOrDefault(c => c.name == name);
        }

        private static void Set(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        #endregion
    }
}
#endif
