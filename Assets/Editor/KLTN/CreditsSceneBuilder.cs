#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using KLTN.Audio;
using KLTN.UI.Credits;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    /// <summary>Creates an editable credits scene and installs its menu entry once.</summary>
    public static class CreditsSceneBuilder
    {
        #region Configuration

        public const string ScenePath = "Assets/Scenes/CreditsScene.unity";
        private const string Root = "Assets/Ui/Credits";
        private const string Login = "Assets/Ui/ImproveLogin";
        private static readonly Color Coral = new Color32(217, 104, 98, 255);
        private static readonly Color Cream = new Color32(242, 229, 207, 255);
        private static TMP_FontAsset regular;
        private static Material tint;

        #endregion

        #region Build Commands

        [MenuItem("Tools/KLTN/Build Credits Scene")]
        public static void Build()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath))
                throw new InvalidOperationException("CreditsScene already exists; edit it directly.");
            AssetDatabase.Refresh();
            Sprite background = ImportSprite(Root + "/Background.png");
            ImportSprite(Root + "/CreditsIcons.png");
            regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                Login + "/Fonts/BeVietnamPro-Regular SDF.asset");
            tint = AssetDatabase.LoadAssetAtPath<Material>(Login + "/AlphaTint.mat");
            var titleFont = TMP_FontAsset.CreateFontAsset(
                AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/Cinzel.ttf"));
            titleFont.name = "Credits Cinzel SDF";
            AssetDatabase.CreateAsset(titleFont, Root + "/Fonts/Credits Cinzel SDF.asset");
            AssetDatabase.AddObjectToAsset(titleFont.material, titleFont);
            foreach (var atlas in titleFont.atlasTextures)
                AssetDatabase.AddObjectToAsset(atlas, titleFont);
            titleFont.TryAddCharacters("CREDITS");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .06f, .09f);
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.gameObject.AddComponent<AudioListener>();
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var viewport = Rect("BackgroundViewport", canvas.transform, 0, 0, 0, 0);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.gameObject.AddComponent<Canvas>();
            var bg = Picture("Background", viewport, background, 0, 0, 2700, 1520, Color.white);
            bg.gameObject.AddComponent<CreditsBackgroundMotion>();
            var design = Rect("CreditsDesign", canvas.transform, 0, 0, 1920, 1080);
            Logo(design);
            var frame = Rect("CreditsPanel", design, 0, -40, 1360, 800);
            Picture("PanelFill", frame, LoadLogin("rounded-fill.png"), 0, 0,
                1360, 800, new Color(.09f, .07f, .08f, .65f)).type = Image.Type.Sliced;
            Picture("PanelBorder", frame, LoadLogin("panel-outline.png"), 0, 0,
                1360, 800, Coral).type = Image.Type.Sliced;
            for (int i = 0; i < 4; i++)
            {
                var corner = Picture("Corner" + i, frame, LoadLogin("corner-ornament.png"),
                    i % 2 == 0 ? -612 : 612, i < 2 ? 340 : -340, 94, 94, Coral);
                corner.material = tint;
                corner.transform.localScale = new Vector3(i % 2 == 0 ? -1 : 1, i < 2 ? 1 : -1, 1);
            }
            var lotus = Picture("Lotus", design, LoadLogin("LotusEmblem.asset"),
                0, 300, 112, 68, Coral);
            lotus.material = tint;
            var title = Label("Title", design, "CREDITS", 0, 216, 520, 100, 76);
            title.font = titleFont;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            Line(design, "TitleLeft", -303, 216, 114, 2, Coral);
            Line(design, "TitleRight", 303, 216, 114, 2, Coral);
            Ornament(design, "HeadingRule", 0, 146, 558, Coral);
            Row(design, "Artwork", Icon("Artwork", new Rect(180, 1989 - 690, 510, 490)),
                48, "Phần lớn hình ảnh thẻ bài, nền, tương tác giao diện\n"
                + "là sản phẩm đến từ <b>Khoa Quan hệ công chúng - Truyền thông</b>");
            Ornament(design, "ArtworkRule", 90, -34, 892, Cream);
            Row(design, "Sound", Icon("Sound", new Rect(180, 1989 - 1260, 510, 480)),
                -116, "Phần lớn SFX đến từ <b>Pixabay</b>");
            Ornament(design, "SoundRule", 90, -193, 892, Cream);
            Row(design, "Development", Icon("Development", new Rect(180, 1989 - 1835, 510, 490)),
                -275, "Những nội dung còn lại được phát triển và xây dựng\n"
                + "từ <b>Khoa Kỹ thuật Cơ - Điện và Máy tính</b>");
            Ornament(design, "FooterRule", 0, -382, 558, Coral);

            var controller = new GameObject("CreditsController").AddComponent<CreditsController>();
            Sprite backSprite = AssetDatabase.LoadAllAssetsAtPath(
                "Assets/ConMaConMa/UI/icon/ingame - RETURN.png").OfType<Sprite>().First();
            var back = Picture("BackButton", canvas.transform, backSprite, 85, 85, 100, 100, Color.white);
            back.rectTransform.anchorMin = back.rectTransform.anchorMax = Vector2.zero;
            back.preserveAspect = true;
            back.raycastTarget = true;
            var button = back.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            var colors = button.colors;
            colors.highlightedColor = new Color(1, .87f, .65f);
            colors.pressedColor = new Color(.7f, .55f, .4f);
            button.colors = colors;
            var sound = back.gameObject.AddComponent<UIButtonSound>();
            UnityEventTools.AddPersistentListener(button.onClick, sound.PlayClick);
            UnityEventTools.AddPersistentListener(button.onClick, controller.BackToMainMenu);
            var binding = new SerializedObject(controller);
            binding.FindProperty("backButton").objectReferenceValue = button;
            binding.ApplyModifiedPropertiesWithoutUndo();
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            EditorSceneManager.SaveScene(scene, ScenePath);
            InstallMenuEntry();
            RegisterScene();
            AssetDatabase.SaveAssets();
            ValidateAndCapture();
        }

        private static void InstallMenuEntry()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            var controller = Object.FindAnyObjectByType<MainMenuController>();
            var button = Object.FindObjectsByType<Button>(FindObjectsInactive.Include)
                .First(b => b.name == "QuitButton" || b.name == "CreditsButton");
            button.name = "CreditsButton";
            button.GetComponentInChildren<TMP_Text>(true).text = "Credits";
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            var sound = button.GetComponent<UIButtonSound>();
            if (sound != null) UnityEventTools.AddPersistentListener(button.onClick, sound.PlayClick);
            UnityEventTools.AddPersistentListener(button.onClick, controller.OpenCredits);
            EditorSceneManager.SaveScene(scene);
        }

        private static void RegisterScene()
        {
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(
                    new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            // Profiles that override the global client list need the same scene entry.
            foreach (string path in new[] { "Assets/Settings/Build Profiles/Windows.asset",
                "Assets/Settings/Build Profiles/Web - Dev - Desktop - Development.asset" })
            {
                var profile = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(path));
                var scenes = profile.FindProperty("m_Scenes");
                bool exists = false;
                for (int i = 0; i < scenes.arraySize; i++)
                    exists |= scenes.GetArrayElementAtIndex(i).FindPropertyRelative("m_path").stringValue == ScenePath;
                if (exists) continue;
                int index = scenes.arraySize;
                scenes.InsertArrayElementAtIndex(index);
                var entry = scenes.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("m_path").stringValue = ScenePath;
                entry.FindPropertyRelative("m_enabled").boolValue = true;
                entry.FindPropertyRelative("m_guid").stringValue = AssetDatabase.AssetPathToGUID(ScenePath);
                profile.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        #endregion

        #region UI Helpers

        private static Sprite ImportSprite(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite Icon(string name, Rect region)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/CreditsIcons.png");
            var sprite = Sprite.Create(texture, region, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            AssetDatabase.CreateAsset(sprite, Root + "/" + name + ".asset");
            return sprite;
        }

        private static Sprite LoadLogin(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(Login + "/Sprites/" + name);

        private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private static Image Picture(string name, Transform parent, Sprite sprite,
            float x, float y, float w, float h, Color color)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Label(string name, Transform parent, string text,
            float x, float y, float w, float h, float size)
        {
            var label = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = regular;
            label.text = text;
            label.fontSize = size;
            label.color = Cream;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            return label;
        }

        private static void Line(Transform parent, string name, float x, float y, float w, float h, Color color)
            => Picture(name, parent, null, x, y, w, h, color);

        private static void Ornament(Transform parent, string name, float x, float y, float width, Color color)
        {
            var root = Rect(name, parent, x, y, width, 24);
            Line(root, "Left", -width / 4 - 13, 0, width / 2 - 26, 1.5f, color);
            Line(root, "Right", width / 4 + 13, 0, width / 2 - 26, 1.5f, color);
            for (int i = -1; i <= 1; i++)
            {
                var diamond = Picture("Diamond" + i, root, null, i * 16, 0,
                    i == 0 ? 13 : 5, i == 0 ? 13 : 5, Coral);
                diamond.transform.localRotation = Quaternion.Euler(0, 0, 45);
                if (i == 0) Picture("Inset", diamond.transform, null, 0, 0, 7, 7,
                    new Color(.13f, .10f, .12f));
            }
        }

        private static void Logo(Transform parent)
        {
            var emblem = Picture("UniversityEmblem", parent, LoadLogin("UniversityEmblem.asset"),
                -155, 454, 78, 88, new Color32(180, 62, 60, 255));
            emblem.material = tint;
            var wordmark = Picture("UniversityWordmark", parent, LoadLogin("UniversityWordmark.asset"),
                65, 454, 270, 88, Cream);
            wordmark.material = tint;
            Line(parent, "LogoDivider", -88, 454, 1.5f, 76, Cream);
            Ornament(parent, "LogoLeft", -385, 450, 204, Coral);
            Ornament(parent, "LogoRight", 385, 450, 204, Coral);
        }

        private static void Row(Transform parent, string name, Sprite icon, float y, string text)
        {
            Picture(name + "Icon", parent, icon, -501, y, 136, 130, Color.white).preserveAspect = true;
            Line(parent, name + "VerticalRule", -386, y, 1.5f, 70, Cream);
            Label(name + "Text", parent, text, 150, y, 980, 112, 31);
        }

        #endregion

        #region Validation and Preview

        [MenuItem("Tools/KLTN/Validate Credits Scene")]
        public static void ValidateAndCapture()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            var canvas = Object.FindObjectsByType<Canvas>().First(c => c.isRootCanvas);
            foreach (var image in canvas.GetComponentsInChildren<Image>())
                if (image.name.EndsWith("Icon") && image.sprite == null)
                    throw new InvalidOperationException("Missing sprite: " + image.name);
            var back = GameObject.Find("BackButton").GetComponent<Button>();
            if (back.onClick.GetPersistentMethodName(1) != "BackToMainMenu")
                throw new InvalidOperationException("Back is not connected.");
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1366, 768),
                new Vector2Int(1920, 1200), new Vector2Int(2560, 1080) })
                Capture(canvas, size.x, size.y);
            Debug.Log("[Credits] PASS: references, back binding and four preview resolutions.");
        }

        private static void Capture(Canvas canvas, int width, int height)
        {
            Camera camera = Object.FindAnyObjectByType<Camera>();
            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            // Match runtime Expand scaling for off-screen render targets.
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            canvas.scaleFactor = Mathf.Min(width / 1920f, height / 1080f);
            Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate();
                if (text.isTextOverflowing)
                    throw new InvalidOperationException("Text overflows: " + text.name);
            }
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var old = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            Directory.CreateDirectory("Logs/Credits");
            File.WriteAllBytes($"Logs/Credits/Credits-{width}x{height}.png", image.EncodeToPNG());
            RenderTexture.active = old;
            camera.targetTexture = null;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler.enabled = true;
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }

        #endregion
    }
}
#endif
