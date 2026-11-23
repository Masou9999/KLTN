#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using KLTN.Audio;
using KLTN.UI;
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
                // MainMenu shares audio controls, never match-only actions.
                foreach (Button action in copy.GetComponentsInChildren<Button>(true))
                {
                    if (action.name == "SurrenderButton")
                    {
                        Object.DestroyImmediate(action.gameObject);
                    }
                }
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

        /// <summary>Updates existing panels without rebuilding either scene's canvas.</summary>
        [MenuItem("Tools/KLTN/Update Settings Audio Icons")]
        public static void UpdateAudioIcons()
        {
            if (EditorApplication.isPlaying ||
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string name in new[] { "GameScene", "MainMenu" })
                {
                    Scene scene = Load(name);
                    var controller = Find<GameSettingsUI>(scene);
                    GameObject root = Get<GameObject>(controller, "settingsPanel");
                    GameObject panel = root.name == "SettingsOverlay"
                        ? root.transform.Find("SettingsPanel").gameObject : root;
                    AddUiSoundSlider(panel);
                    AddMuteButtons(panel);
                    Wire(controller, panel, root);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                Debug.Log("[SettingsAudioIcons] Updated both settings panels.");
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
            controller.InitializeControls();
        }

        private static void AddUiSoundSlider(GameObject panel)
        {
            Transform window = panel.transform.Find("SettingsWindow");
            if (window.Find("UiSoundSlider") == null)
            {
                Transform ui = Object.Instantiate(window.Find("SFXSlider"), window, false);
                ui.name = "UiSoundSlider";
            }
            foreach (string row in new[] { "Music", "UiSound", "SFX" })
            {
                Transform label = window.Find(row + "Label");
                if (label != null) Object.DestroyImmediate(label.gameObject);
            }
            SettingsPanelLayout.ApplyLayout(window);
        }

        private static void AddMuteButtons(GameObject panel)
        {
            Transform window = panel.transform.Find("SettingsWindow");
            foreach (string name in new[] { "Music", "Sound", "UiSound", "SFX" })
            {
                Transform old = window.Find(name + "MuteControl");
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            foreach (var row in new[]
            {
                ("Music", "background_music", "background_musicmuted", 160f),
                ("UiSound", "ui_sound", "ui_sound_muted", 88f),
                ("SFX", "sfx", "sfx_muted", 16f),
            })
            {
                var go = new GameObject(row.Item1 + "MuteControl", typeof(RectTransform),
                    typeof(Image), typeof(Button), typeof(AudioMuteButton));
                go.transform.SetParent(window, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(-220, row.Item4);
                rect.sizeDelta = new Vector2(64, 52);
                var toggle = go.GetComponent<AudioMuteButton>();
                var serialized = new SerializedObject(toggle);
                serialized.FindProperty("controlsMusic").boolValue = row.Item1 == "Music";
                serialized.FindProperty("controlsCombat").boolValue = row.Item1 == "SFX";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Sprite on = Icon(row.Item2);
                Set(toggle, "enabledIcon", on);
                Set(toggle, "mutedIcon", Icon(row.Item3));
                var image = go.GetComponent<Image>();
                image.sprite = on;
                image.preserveAspect = true;
                go.GetComponent<Button>().targetGraphic = image;
                Sound(go.GetComponent<Button>());
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
                if (panel == mainPanel)
                {
                    Require(!panel.GetComponentsInChildren<Button>(true)
                        .Any(b => b.name == "SurrenderButton"),
                        "MainMenu contains a match-only action");
                }
                Require(panel.GetComponentsInChildren<AudioMuteButton>(true).Length == 3,
                    "Missing mute controls");
                foreach (var transform in panel.GetComponentsInChildren<Transform>(true))
                {
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transform.gameObject) == 0, "Missing script");
                }
            }
            var checkpoint = AudioVolumeSettings.CapturePreview();
            try
            {
                Invoke(game, "OnEnable");
                Invoke(main, "OnEnable");
                main.SetMusicVolume(0.4f);
                main.SetSFXVolume(0.2f);
                main.SetUiSoundVolume(0.6f);
                game.SetSFXVolume(0f);
                Require(Mathf.Approximately(AudioVolumeSettings.UiSoundVolume, 0.6f) &&
                    !AudioVolumeSettings.UiSoundMuted &&
                    Mathf.Approximately(Get<Slider>(game, "uiSoundSlider").value, 0.6f),
                    "Combat slider changed UI audio");
                main.SetSFXVolume(0.2f);
                AudioVolumeSettings.SetSfxMuted(true);
                Require(Get<Slider>(main, "sfxSlider").value == 0
                    && Get<AudioSource>(game, "sfxAudioSource").volume == 0
                    && Mathf.Approximately(AudioVolumeSettings.UiSoundVolume, 0.6f),
                    "Combat mute did not silence combat independently");
                AudioVolumeSettings.SetSfxMuted(false);
                Require(Mathf.Approximately(AudioVolumeSettings.SfxVolume, 0.2f),
                    "Combat unmute did not restore volume");
                game.SetUiSoundVolume(0f);
                Require(AudioVolumeSettings.UiSoundMuted &&
                    Mathf.Approximately(AudioVolumeSettings.SfxVolume, 0.2f) &&
                    Get<Slider>(main, "uiSoundSlider").value == 0,
                    "UI slider changed combat audio or did not synchronize");
                AudioVolumeSettings.SetUiSoundMuted(false);
                Require(Get<Slider>(game, "uiSoundSlider").value > 0,
                    "Unmuting UI did not restore audible volume");
                Require(Mathf.Approximately(Get<Slider>(game, "musicSlider").value, 0.4f),
                    "Menu music change did not reach Game settings");
                Require(Mathf.Approximately(Get<AudioSource>(game,
                    "sfxAudioSource").volume, 0.2f), "SFX source not synchronized");
                AudioVolumeSettings.SetMusicMuted(true);
                Require(Get<Slider>(main, "musicSlider").value == 0 &&
                    Get<Slider>(game, "musicSlider").value == 0 &&
                    Get<AudioSource>(game, "musicAudioSource").mute,
                    "Mute not synchronized");
                AudioVolumeSettings.SetUiSoundMuted(true);
                Require(Mathf.Approximately(Get<AudioSource>(game,
                    "sfxAudioSource").volume, 0.2f), "UI mute changed combat SFX");
                game.SetMusicVolume(0.6f);
                Require(!AudioVolumeSettings.MusicMuted &&
                    Mathf.Approximately(Get<Slider>(main, "musicSlider").value, 0.6f),
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
                AudioVolumeSettings.RestorePreview(checkpoint);
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
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Ui/Settings/AudioIcons/" + name + ".png");
            Require(sprite != null, "Missing audio icon: " + name);
            return sprite;
        }

        #endregion
    }
}
#endif
