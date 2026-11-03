#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using KLTN.Audio;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Copies the match settings panel into MainMenu and wires shared audio.
    /// </summary>
    public static class SharedSettingsInstaller
    {
        #region Public Commands

        [MenuItem("Tools/KLTN/Install Shared Settings")]
        public static void Install()
        {
            if (EditorApplication.isPlaying ||
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene game = Load("GameScene");
                var gameSettings = Find<GameSettingsUI>(game);
                GameObject panel = Get<GameObject>(gameSettings, "settingsPanel");
                var surrender = panel.GetComponentsInChildren<Button>(true)
                    .FirstOrDefault(b => b.name == "SurrenderButton");
                if (surrender != null)
                {
                    Object.DestroyImmediate(surrender.gameObject);
                }
                AddUiSoundSlider(panel);
                AddMuteButtons(panel);
                EnsureUiAudio(game);
                Wire(gameSettings, panel, panel);
                panel.SetActive(false);
                EditorSceneManager.SaveScene(game);

                Scene main = Load("MainMenu");
                var canvas = Find<Canvas>(main);
                var old = canvas.transform.Find("SettingsOverlay");
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }
                var overlay = new GameObject("SettingsOverlay", typeof(RectTransform),
                    typeof(Image), typeof(CanvasGroup));
                overlay.transform.SetParent(canvas.transform, false);
                var rect = (RectTransform)overlay.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                overlay.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f);
                overlay.GetComponent<CanvasGroup>().ignoreParentGroups = true;
                GameObject copy = Object.Instantiate(panel, overlay.transform, false);
                copy.name = "SettingsPanel";
                copy.SetActive(true);
                var copyRect = (RectTransform)copy.transform;
                copyRect.anchorMin = copyRect.anchorMax = new Vector2(0.5f, 0.5f);
                copyRect.anchoredPosition = Vector2.zero;
                var mainSettings = canvas.GetComponent<GameSettingsUI>();
                if (mainSettings == null)
                {
                    mainSettings = canvas.gameObject.AddComponent<GameSettingsUI>();
                }
                Set(mainSettings, "musicAudioSource", null);
                Set(mainSettings, "sfxAudioSource", null);
                Set(mainSettings, "menuGroup", canvas.GetComponent<CanvasGroup>());
                Button open = FindAll<Button>(main).First(b => b.name == "SettingsButton");
                Set(mainSettings, "openButton", open);
                open.onClick = new Button.ButtonClickedEvent();
                Sound(open);
                UnityEventTools.AddPersistentListener(open.onClick, mainSettings.OpenSettings);
                Wire(mainSettings, copy, overlay);
                overlay.SetActive(false);
                EditorSceneManager.SaveScene(main);
                Validate(gameSettings, mainSettings, panel, copy);
                SceneManager.SetActiveScene(main);
                overlay.SetActive(true);
                MenuPolishInstaller.Preview("MainMenuSettings");
                overlay.SetActive(false);
                SceneManager.SetActiveScene(game);
                panel.SetActive(true);
                MenuPolishInstaller.Preview("GameSettings");
                panel.SetActive(false);
                Debug.Log("[SharedSettings] Installed and validated: both settings panels, " +
                    "three independent audio sliders, shared mute and menu isolation.");
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
        }

        #endregion

        #region Scene Wiring

        private static void Wire(GameSettingsUI controller, GameObject panel,
            GameObject visibilityRoot)
        {
            Slider music = panel.GetComponentsInChildren<Slider>(true)
                .First(s => s.name == "MusicSlider");
            Slider sfx = panel.GetComponentsInChildren<Slider>(true)
                .First(s => s.name == "SFXSlider");
            Set(controller, "settingsPanel", visibilityRoot);
            Set(controller, "musicSlider", music);
            Set(controller, "sfxSlider", sfx);
            Slider ui = panel.GetComponentsInChildren<Slider>(true)
                .First(s => s.name == "UiSoundSlider");
            Set(controller, "uiSoundSlider", ui);
            ui.onValueChanged = new Slider.SliderEvent();
            UnityEventTools.AddPersistentListener(ui.onValueChanged,
                controller.SetUiSoundVolume);
            music.onValueChanged = new Slider.SliderEvent();
            sfx.onValueChanged = new Slider.SliderEvent();
            UnityEventTools.AddPersistentListener(music.onValueChanged,
                controller.SetMusicVolume);
            UnityEventTools.AddPersistentListener(sfx.onValueChanged,
                controller.SetSFXVolume);
            Button close = panel.GetComponentsInChildren<Button>(true)
                .First(b => b.name == "CloseButton");
            close.onClick = new Button.ButtonClickedEvent();
            Sound(close);
            UnityEventTools.AddPersistentListener(close.onClick, controller.CloseSettings);
        }

        private static void AddUiSoundSlider(GameObject panel)
        {
            Transform window = panel.transform.Find("SettingsWindow");
            var musicLabel = window.Find("MusicLabel").GetComponent<TMP_Text>();
            var sfxLabel = window.Find("SFXLabel").GetComponent<TMP_Text>();
            musicLabel.text = "Nhạc nền";
            sfxLabel.text = "Âm thanh trận đấu";
            Transform ui = window.Find("UiSoundSlider");
            if (ui == null)
            {
                ui = Object.Instantiate(window.Find("SFXSlider"), window, false);
                ui.name = "UiSoundSlider";
            }
            Transform label = window.Find("UiSoundLabel");
            if (label == null)
            {
                label = Object.Instantiate(sfxLabel.transform, window, false);
                label.name = "UiSoundLabel";
            }
            label.GetComponent<TMP_Text>().text = "Âm thanh giao diện";
            foreach (var row in new[]
                { ("Music", 85f), ("UiSound", 30f), ("SFX", -25f) })
            {
                var slider = (RectTransform)window.Find(row.Item1 + "Slider");
                slider.anchoredPosition = new Vector2(115, row.Item2);
                var text = window.Find(row.Item1 + "Label").GetComponent<TMP_Text>();
                text.rectTransform.anchoredPosition = new Vector2(-215, row.Item2);
                text.rectTransform.sizeDelta = new Vector2(290, 45);
                text.enableAutoSizing = true;
                text.fontSizeMin = 21;
                text.fontSizeMax = 24;
            }
            ((RectTransform)window.Find("CloseButton")).anchoredPosition =
                new Vector2(0, -175);
        }

        private static void AddMuteButtons(GameObject panel)
        {
            Transform window = panel.transform.Find("SettingsWindow");
            TMP_FontAsset font = window.GetComponentInChildren<TMP_Text>(true).font;
            foreach (bool music in new[] { true, false })
            {
                string prefix = music ? "Music" : "Sound";
                Transform old = window.Find(prefix + "MuteControl");
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }
                var go = new GameObject(prefix + "MuteControl", typeof(RectTransform),
                    typeof(Image), typeof(Button), typeof(AudioMuteButton));
                go.transform.SetParent(window, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(music ? -245 : 35, -105);
                rect.sizeDelta = new Vector2(52, 52);
                var toggle = go.GetComponent<AudioMuteButton>();
                var serialized = new SerializedObject(toggle);
                serialized.FindProperty("controlsMusic").boolValue = music;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Sprite on = Icon(prefix + "On");
                Set(toggle, "enabledIcon", on);
                Set(toggle, "mutedIcon", Icon(prefix + "Off"));
                go.GetComponent<Image>().sprite = on;
                go.GetComponent<Image>().preserveAspect = true;
                var button = go.GetComponent<Button>();
                button.targetGraphic = go.GetComponent<Image>();
                Sound(button);
                var label = new GameObject("Label", typeof(RectTransform),
                    typeof(TextMeshProUGUI));
                label.transform.SetParent(go.transform, false);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
                labelRect.anchoredPosition = new Vector2(150, 0);
                labelRect.sizeDelta = new Vector2(225, 50);
                var text = label.GetComponent<TextMeshProUGUI>();
                text.font = font;
                text.fontSize = 22;
                text.text = music ? "Nhạc nền" : "Âm thanh giao diện";
                text.color = Color.white;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.raycastTarget = false;
            }
        }

        private static void EnsureUiAudio(Scene scene)
        {
            if (Find<UIClickManager>(scene) != null)
            {
                return;
            }
            var go = new GameObject("UIClickManager");
            SceneManager.MoveGameObjectToScene(go, scene);
            var manager = go.AddComponent<UIClickManager>();
            go.GetComponent<AudioSource>().playOnAwake = false;
            Set(manager, "clickSound", AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/Audio/SFX/MenuClick.mp3"));
            Set(manager, "hoverSound", AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/Audio/SFX/MenuHover.mp3"));
        }

        private static void Sound(Button button)
        {
            var sound = button.GetComponent<UIButtonSound>();
            if (sound == null)
            {
                sound = button.gameObject.AddComponent<UIButtonSound>();
            }
            UnityEventTools.AddPersistentListener(button.onClick, sound.PlayClick);
        }

        #endregion

        #region Validation

        private static void Validate(GameSettingsUI game, GameSettingsUI main,
            GameObject gamePanel, GameObject mainPanel)
        {
            foreach (var panel in new[] { gamePanel, mainPanel })
            {
                Require(!panel.GetComponentsInChildren<Button>(true)
                    .Any(b => b.name == "SurrenderButton"), "Surrender still present");
                Require(panel.GetComponentsInChildren<AudioMuteButton>(true).Length == 2,
                    "Missing mute controls");
                foreach (var transform in panel.GetComponentsInChildren<Transform>(true))
                {
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transform.gameObject) == 0, "Missing script");
                }
            }
            float music = AudioVolumeSettings.MusicVolume;
            float sfx = AudioVolumeSettings.SfxVolume;
            float uiVolume = AudioVolumeSettings.UiSoundVolume;
            bool hadUiVolume = PlayerPrefs.HasKey(AudioVolumeSettings.UiSoundVolumeKey);
            bool musicMuted = AudioVolumeSettings.MusicMuted;
            bool uiMuted = AudioVolumeSettings.UiSoundMuted;
            try
            {
                Invoke(game, "OnEnable");
                Invoke(main, "OnEnable");
                main.SetMusicVolume(0.42f);
                main.SetSFXVolume(0.23f);
                main.SetUiSoundVolume(0.56f);
                game.SetSFXVolume(0f);
                Require(Mathf.Approximately(AudioVolumeSettings.UiSoundVolume, 0.56f) &&
                    !AudioVolumeSettings.UiSoundMuted &&
                    Mathf.Approximately(Get<Slider>(game, "uiSoundSlider").value, 0.56f),
                    "Combat slider changed UI audio");
                main.SetSFXVolume(0.23f);
                game.SetUiSoundVolume(0f);
                Require(AudioVolumeSettings.UiSoundMuted &&
                    Mathf.Approximately(AudioVolumeSettings.SfxVolume, 0.23f) &&
                    Get<Slider>(main, "uiSoundSlider").value == 0,
                    "UI slider changed combat audio or did not synchronize");
                AudioVolumeSettings.SetUiSoundMuted(false);
                Require(Get<Slider>(game, "uiSoundSlider").value > 0,
                    "Unmuting UI did not restore audible volume");
                Require(Mathf.Approximately(Get<Slider>(game, "musicSlider").value, 0.42f),
                    "Menu music change did not reach Game settings");
                Require(Mathf.Approximately(Get<AudioSource>(game,
                    "sfxAudioSource").volume, 0.23f), "SFX source not synchronized");
                AudioVolumeSettings.SetMusicMuted(true);
                Require(Get<Slider>(main, "musicSlider").value == 0 &&
                    Get<Slider>(game, "musicSlider").value == 0 &&
                    Get<AudioSource>(game, "musicAudioSource").mute,
                    "Mute not synchronized");
                AudioVolumeSettings.SetUiSoundMuted(true);
                Require(Mathf.Approximately(Get<AudioSource>(game,
                    "sfxAudioSource").volume, 0.23f), "UI mute changed combat SFX");
                game.SetMusicVolume(0.64f);
                Require(!AudioVolumeSettings.MusicMuted &&
                    Mathf.Approximately(Get<Slider>(main, "musicSlider").value, 0.64f),
                    "Game music change did not reach MainMenu");
                main.OpenSettings();
                Require(!Get<CanvasGroup>(main, "menuGroup").interactable,
                    "Underlying menu was not blocked");
                main.CloseSettings();
                Require(Get<CanvasGroup>(main, "menuGroup").interactable,
                    "Underlying menu was not restored");
            }
            finally
            {
                AudioVolumeSettings.SetMusicVolume(music);
                AudioVolumeSettings.SetMusicMuted(musicMuted);
                AudioVolumeSettings.SetSfxVolume(sfx);
                AudioVolumeSettings.SetUiSoundVolume(uiVolume);
                AudioVolumeSettings.SetUiSoundMuted(uiMuted);
                if (!hadUiVolume)
                {
                    PlayerPrefs.DeleteKey(AudioVolumeSettings.UiSoundVolumeKey);
                    PlayerPrefs.Save();
                }
                Invoke(game, "OnDisable");
                Invoke(main, "OnDisable");
            }
        }

        #endregion

        #region Helpers

        private static void Invoke(GameSettingsUI settings, string method)
        {
            typeof(GameSettingsUI).GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(settings, null);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static Scene Load(string name)
        {
            Scene scene = SceneManager.GetSceneByName(name);
            return scene.isLoaded ? scene : EditorSceneManager.OpenScene(
                "Assets/Scenes/" + name + ".unity", OpenSceneMode.Additive);
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            return FindAll<T>(scene).FirstOrDefault();
        }

        private static T[] FindAll<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        }

        private static T Get<T>(Object target, string property) where T : Object
        {
            return new SerializedObject(target).FindProperty(property)
                .objectReferenceValue as T;
        }

        private static void Set(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Sprite Icon(string name)
        {
            return AssetDatabase.LoadAllAssetsAtPath(
                "Assets/ConMaConMa/UI/Menu/AudioIcons.png").OfType<Sprite>()
                .First(sprite => sprite.name == name);
        }

        #endregion
    }
}
#endif
