#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using KLTN.Audio;
using KLTN.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Installs the menu controls as editable, serialized scene objects.
    /// </summary>
    public static class MenuPolishInstaller
    {
        #region Configuration

        private const string Art = "Assets/ConMaConMa/UI/Menu/";
        private static TMP_FontAsset font;
        private static readonly Color Cream = new Color(0.91f, 0.85f, 0.74f);

        #endregion

        #region Public Commands

        [MenuItem("Tools/KLTN/Install Menu Polish")]
        public static void Install()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            ImportSprites("Store.png");
            ImportSprites("Leaderboard.png");
            ImportSprites("AudioIcons.png",
                Slice("SoundOn", 65, 250, 380, 390),
                Slice("SoundOff", 480, 250, 380, 390),
                Slice("MusicOn", 900, 250, 380, 390),
                Slice("MusicOff", 1315, 250, 380, 390));
            ImportSprites("PlayModes.png",
                Slice("PrivateRoom", 80, 45, 735, 680),
                Slice("Matchmaking", 935, 45, 780, 680));
            InstallMainMenu();
            InstallLogin();
            InstallFindMatch();
            AssetDatabase.SaveAssets();
            SharedSettingsInstaller.Install();
            Debug.Log("[MenuPolish] Installed all three scenes.");
        }

        #endregion

        #region Scene Installation

        private static void InstallMainMenu()
        {
            Open("MainMenu");
            Canvas canvas = Object.FindAnyObjectByType<Canvas>();
            var controller = Object.FindAnyObjectByType<MainMenuController>();
            Button play = Find<Button>("HostClientButton") ?? Find<Button>("PlayButton");
            font = play.GetComponentInChildren<TMP_Text>().font;
            play.name = "PlayButton";
            play.GetComponentInChildren<TMP_Text>().text = "Chơi";
            play.onClick = new Button.ButtonClickedEvent();
            Remove("FindMatchButton");
            Remove("ShopButton");
            Remove("LeaderboardButton");
            Remove("SettingsButton");
            Remove("MenuShortcuts");
            Remove("PlayModeOverlay");

            var menuGroup = canvas.GetComponent<CanvasGroup>();
            if (menuGroup == null)
            {
                menuGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            }
            Button settings = Object.Instantiate(play, play.transform.parent);
            settings.name = "SettingsButton";
            settings.onClick = new Button.ButtonClickedEvent();
            settings.GetComponentInChildren<TMP_Text>().text = "Cài đặt";
            ((RectTransform)settings.transform).anchoredPosition =
                ((RectTransform)play.transform).anchoredPosition + new Vector2(0, -150);
            Button logout = Find<Button>("LogoutButton");
            ((RectTransform)logout.transform).anchoredPosition =
                ((RectTransform)play.transform).anchoredPosition + new Vector2(0, -300);

            RectTransform shortcuts = Rect("MenuShortcuts", canvas.transform);
            Stretch(shortcuts, Vector2.zero, Vector2.one);
            Button shop = Icon("ShopIconButton", shortcuts, Sprite("Store.png"));
            TopRight(shop, -200, -95);
            CircleCrop(shop);
            UnityEventTools.AddPersistentListener(shop.onClick, controller.OpenShop);
            Button leaderboard = Icon("LeaderboardIconButton", shortcuts,
                Sprite("Leaderboard.png"));
            TopRight(leaderboard, -80, -95);
            CircleCrop(leaderboard);
            UnityEventTools.AddPersistentListener(leaderboard.onClick,
                controller.OpenLeaderboard);

            RectTransform overlay = Panel("PlayModeOverlay", canvas.transform,
                new Color(0, 0, 0, 0.72f));
            Stretch(overlay, Vector2.zero, Vector2.one);
            CanvasGroup modalGroup = overlay.gameObject.AddComponent<CanvasGroup>();
            modalGroup.ignoreParentGroups = true;
            RectTransform panel = Panel("ModePanel", overlay,
                new Color(0.075f, 0.065f, 0.09f, 0.98f));
            Stretch(panel, new Vector2(0.15f, 0.15f), new Vector2(0.85f, 0.85f));
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.26f, 0.16f);
            outline.effectDistance = new Vector2(3, -3);
            Text("Title", panel, "CHỌN CHẾ ĐỘ", new Vector2(0.1f, 0.83f),
                new Vector2(0.9f, 0.96f), 44);
            Button privateRoom = ModeOption(panel, "PrivateRoomOption", "Phòng Riêng",
                Sprite("PlayModes.png", "PrivateRoom"), 0.06f, 0.47f);
            Button match = ModeOption(panel, "MatchmakingOption", "Ghép Trận",
                Sprite("PlayModes.png", "Matchmaking"), 0.53f, 0.94f);
            UnityEventTools.AddPersistentListener(privateRoom.onClick,
                controller.OpenHostClientMenu);
            UnityEventTools.AddPersistentListener(
                match.onClick, controller.OpenFindMatch);

            var modal = canvas.GetComponent<PlayModeModal>();
            if (modal == null)
            {
                modal = canvas.gameObject.AddComponent<PlayModeModal>();
            }
            Set(modal, "modalRoot", overlay.gameObject);
            Set(modal, "menuGroup", menuGroup);
            Set(modal, "playButton", play);
            Set(modal, "firstOption", privateRoom);
            Button close = PlainButton("CloseModeButton", panel, "×");
            TopRight(close, -42, -42, 64);
            UnityEventTools.AddPersistentListener(close.onClick, modal.Close);
            UnityEventTools.AddPersistentListener(play.onClick, modal.Open);
            overlay.gameObject.SetActive(false);
            ConfigureAudio(false);
            InstallButtonSounds();
            Save();
            Preview("MainMenu");
            overlay.gameObject.SetActive(true);
            Preview("PlayModeModal");
            overlay.gameObject.SetActive(false);
        }

        private static void InstallLogin()
        {
            Open("LoginScene");
            Canvas canvas = Object.FindAnyObjectByType<Canvas>();
            Remove("AudioControls");
            RectTransform root = Rect("AudioControls", canvas.transform);
            Stretch(root, Vector2.zero, Vector2.one);
            foreach (bool music in new[] { true, false })
            {
                string prefix = music ? "Music" : "Sound";
                Button button = Icon(prefix + "Toggle", root,
                    Sprite("AudioIcons.png", prefix + "On"));
                TopRight(button, music ? -200 : -80, -95);
                var toggle = button.gameObject.AddComponent<AudioMuteButton>();
                var serialized = new SerializedObject(toggle);
                serialized.FindProperty("controlsMusic").boolValue = music;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Set(toggle, "enabledIcon", Sprite("AudioIcons.png", prefix + "On"));
                Set(toggle, "mutedIcon", Sprite("AudioIcons.png", prefix + "Off"));
            }
            ConfigureAudio(true);
            InstallButtonSounds();
            Save();
            Preview("LoginScene");
        }

        private static void InstallFindMatch()
        {
            Open("FindMatch");
            Canvas canvas = Object.FindAnyObjectByType<Canvas>();
            Remove("BackToMenuButton");
            Button back = Icon("BackToMenuButton", canvas.transform,
                AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/ConMaConMa/UI/icon/ingame - RETURN.png"));
            RectTransform rect = (RectTransform)back.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(85, -85);
            rect.sizeDelta = new Vector2(100, 100);
            var client = Object.FindAnyObjectByType<DedicatedMatchmakerClient>();
            Set(client, "backButton", back);
            UnityEventTools.AddPersistentListener(back.onClick, client.BackToMainMenu);
            Save();
            Preview("FindMatch");
        }

        #endregion

        #region Audio Installation

        private static void ConfigureAudio(bool addMusic)
        {
            UIClickManager click = Object.FindAnyObjectByType<UIClickManager>();
            if (click == null)
            {
                click = new GameObject("UIClickManager").AddComponent<UIClickManager>();
            }
            Set(click, "clickSound", AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/Audio/SFX/MenuClick.mp3"));
            Set(click, "hoverSound", AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/Audio/SFX/MenuHover.mp3"));
            click.GetComponent<AudioSource>().playOnAwake = false;
            if (addMusic && Object.FindAnyObjectByType<MenuMusicManager>() == null)
            {
                var music = new GameObject("MenuMusicManager")
                    .AddComponent<MenuMusicManager>();
                var source = music.GetComponent<AudioSource>();
                source.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/Audio/Music/MenuMusic.mp3");
                source.loop = true;
                source.playOnAwake = false;
                source.volume = 0.5f;
            }
            foreach (var music in Object.FindObjectsByType<MenuMusicManager>())
            {
                music.GetComponent<AudioSource>().playOnAwake = false;
            }
        }

        private static void InstallButtonSounds()
        {
            foreach (Button button in Object.FindObjectsByType<Button>(
                FindObjectsInactive.Include))
            {
                // Full-screen decorative buttons must not restore global click audio.
                if (button.name == "BackGroundButton")
                {
                    Object.DestroyImmediate(button);
                    continue;
                }
                var sound = button.GetComponent<UIButtonSound>();
                if (sound == null)
                {
                    sound = button.gameObject.AddComponent<UIButtonSound>();
                }
                bool wired = Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                    .Any(i => button.onClick.GetPersistentTarget(i) == sound);
                if (wired)
                {
                    continue;
                }
                UnityEventTools.AddPersistentListener(button.onClick, sound.PlayClick);
                // Feedback precedes handlers that disable the button or change scene.
                var serialized = new SerializedObject(button);
                var calls = serialized.FindProperty(
                    "m_OnClick.m_PersistentCalls.m_Calls");
                calls.MoveArrayElement(calls.arraySize - 1, 0);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        #endregion

        #region Sprite Import

        private static SpriteRect Slice(string name, int x, int y, int w, int h)
        {
            return new SpriteRect
            {
                name = name, rect = new Rect(x, y, w, h),
                pivot = new Vector2(0.5f, 0.5f), alignment = SpriteAlignment.Center,
                spriteID = GUID.Generate()
            };
        }

        private static void ImportSprites(string name, params SpriteRect[] slices)
        {
            string path = Art + name;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = slices.Length == 0
                ? SpriteImportMode.Single : SpriteImportMode.Multiple;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            if (slices.Length == 0)
            {
                return;
            }
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var previous = provider.GetSpriteRects();
            foreach (var slice in slices)
            {
                var existing = previous.FirstOrDefault(p => p.name == slice.name);
                if (existing != null)
                {
                    slice.spriteID = existing.spriteID;
                }
            }
            provider.SetSpriteRects(slices);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(
                slices.Select(s => new SpriteNameFileIdPair(s.name, s.spriteID)));
            provider.Apply();
            importer.SaveAndReimport();
        }

        private static Sprite Sprite(string path, string name = null)
        {
            return AssetDatabase.LoadAllAssetsAtPath(Art + path).OfType<Sprite>()
                .First(s => name == null || s.name == name);
        }

        #endregion

        #region UI Helpers

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform))
                .GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Panel(string name, Transform parent, Color color)
        {
            RectTransform rect = Rect(name, parent);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }

        private static Button Icon(string name, Transform parent, Sprite sprite)
        {
            RectTransform rect = Panel(name, parent, Color.white);
            Image image = rect.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1, 0.87f, 0.65f);
            colors.pressedColor = new Color(0.7f, 0.55f, 0.4f);
            colors.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.55f);
            button.colors = colors;
            return button;
        }

        private static void TopRight(Button button, float x, float y, float size = 100)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(size, size);
        }

        private static void CircleCrop(Button button)
        {
            // The supplied icons contain opaque square corners. Clip their display
            // to the circular rim while retaining the original source artwork.
            Image maskImage = button.GetComponent<Image>();
            Sprite artwork = maskImage.sprite;
            Object.DestroyImmediate(maskImage);
            button.gameObject.AddComponent<CircularMaskGraphic>();
            button.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            RectTransform rect = Rect("Artwork", button.transform);
            Stretch(rect, new Vector2(-0.065f, -0.065f), new Vector2(1.065f, 1.065f));
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = artwork;
            image.raycastTarget = false;
            button.targetGraphic = image;
        }

        private static void Text(string name, Transform parent, string value,
            Vector2 min, Vector2 max, float size)
        {
            RectTransform rect = Rect(name, parent);
            Stretch(rect, min, max);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = value;
            label.fontSize = size;
            label.color = Cream;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        private static Button PlainButton(string name, Transform parent, string label)
        {
            Button button = Icon(name, parent, null);
            button.GetComponent<Image>().color = new Color(0.35f, 0.12f, 0.10f);
            Text("Label", button.transform, label, Vector2.zero, Vector2.one, 42);
            return button;
        }

        private static Button ModeOption(Transform parent, string name, string label,
            Sprite illustration, float minX, float maxX)
        {
            Button button = Icon(name, parent, null);
            button.GetComponent<Image>().color = new Color(0.19f, 0.16f, 0.17f);
            Stretch((RectTransform)button.transform, new Vector2(minX, 0.10f),
                new Vector2(maxX, 0.80f));
            RectTransform art = Rect("Illustration", button.transform);
            Stretch(art, new Vector2(0.08f, 0.23f), new Vector2(0.92f, 0.95f));
            var image = art.gameObject.AddComponent<Image>();
            image.sprite = illustration;
            image.preserveAspect = true;
            image.raycastTarget = false;
            Text("Label", button.transform, label, new Vector2(0.02f, 0.03f),
                new Vector2(0.98f, 0.22f), 44);
            return button;
        }

        private static T Find<T>(string name) where T : Component
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include)
                .FirstOrDefault(c => c.name == name);
        }

        private static void Remove(string name)
        {
            Transform existing = Find<Transform>(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }
        }

        private static void Set(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Open(string scene)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/" + scene + ".unity");
        }

        private static void Save()
        {
            EditorSceneManager.SaveScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        public static void Preview(string name)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Canvas canvas = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Canvas>())
                .First(c => c.isRootCanvas);
            Camera camera = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Camera>()).First();
            RenderMode mode = canvas.renderMode;
            Camera previousCamera = canvas.worldCamera;
            float distance = canvas.planeDistance;
            var texture = new RenderTexture(1920, 1080, 24);
            texture.Create();
            camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            image.Apply();
            Directory.CreateDirectory("Logs/MenuPolish");
            File.WriteAllBytes("Logs/MenuPolish/" + name + ".png", image.EncodeToPNG());
            RenderTexture.active = previous;
            camera.targetTexture = null;
            canvas.renderMode = mode;
            canvas.worldCamera = previousCamera;
            canvas.planeDistance = distance;
            Object.DestroyImmediate(image);
            texture.Release();
            Object.DestroyImmediate(texture);
        }

        #endregion
    }
}
#endif
