#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using KLTN.Audio;
using KLTN.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Checks serialized wiring and local UI state without contacting services.
    /// </summary>
    public static class MenuPolishValidation
    {
        #region Public Commands

        public static void InstallAndValidate()
        {
            MenuPolishInstaller.Install();
            Validate();
        }

        [MenuItem("Tools/KLTN/Validate Menu Polish")]
        public static void Validate()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            Open("MainMenu");
            ValidateSounds();
            Require(Find<Button>("HostClientButton") == null, "Old host button remains.");
            Require(Find<Button>("FindMatchButton") == null, "Old find button remains.");
            ValidateAction("ShopIconButton", "OpenShop");
            ValidateAction("LeaderboardIconButton", "OpenLeaderboard");
            ValidateAction("PrivateRoomOption", "OpenHostClientMenu");
            ValidateAction("MatchmakingOption", "OpenFindMatch");
            ValidateAction("PlayButton", "Open");
            ValidateAction("CloseModeButton", "Close");
            ValidateAction("SettingsButton", "OpenSettings");
            var modal = Object.FindAnyObjectByType<PlayModeModal>();
            var overlay = Find<Transform>("PlayModeOverlay").gameObject;
            var menu = modal.GetComponent<CanvasGroup>();
            Require(!overlay.activeSelf, "Modal must start closed.");
            modal.Open();
            Require(overlay.activeSelf && !menu.interactable,
                "Open must disable underlying menu navigation.");
            Require(overlay.GetComponent<CanvasGroup>().ignoreParentGroups,
                "Modal must remain interactable.");
            modal.Close();
            Require(!overlay.activeSelf && menu.interactable,
                "Close must restore underlying menu navigation.");

            Open("LoginScene");
            ValidateSounds();
            foreach (var toggle in Object.FindObjectsByType<AudioMuteButton>())
            {
                var serialized = new SerializedObject(toggle);
                Require(serialized.FindProperty("enabledIcon").objectReferenceValue,
                    "Missing enabled icon.");
                Require(serialized.FindProperty("mutedIcon").objectReferenceValue,
                    "Missing muted icon.");
            }
            Require(Object.FindObjectsByType<AudioMuteButton>().Length == 2,
                "Login must have exactly two audio toggles.");
            Require(Object.FindAnyObjectByType<MenuMusicManager>() != null,
                "Login music source is missing.");

            Open("FindMatch");
            ValidateAction("BackToMenuButton", "BackToMainMenu");
            var client = Object.FindAnyObjectByType<DedicatedMatchmakerClient>();
            Button back = Find<Button>("BackToMenuButton");
            var state = new SerializedObject(client);
            Require(state.FindProperty("backButton").objectReferenceValue == back,
                "Back button reference is missing.");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo refresh = typeof(DedicatedMatchmakerClient)
                .GetMethod("RefreshBackButton", flags);
            foreach (string field in new[]
                { "isSearching", "isConnecting", "isCancelling", "isLeaving" })
            {
                var member = typeof(DedicatedMatchmakerClient).GetField(field, flags);
                member.SetValue(client, true);
                refresh.Invoke(client, null);
                Require(!back.interactable, "Back was enabled during " + field);
                member.SetValue(client, false);
                refresh.Invoke(client, null);
                Require(back.interactable, "Back did not recover after " + field);
            }
            Require(back.colors.disabledColor.a < 1,
                "Back requires a visible disabled appearance.");
            Debug.Log("[MenuPolish] Validation passed: scene wiring, button audio, " +
                "modal isolation, audio icons, Back lock/recovery.");
        }

        #endregion

        #region Helpers

        private static void Open(string scene)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/" + scene + ".unity");
            foreach (var transform in Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include))
            {
                Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    transform.gameObject) == 0, "Missing script on " + transform.name);
            }
        }

        private static void ValidateSounds()
        {
            foreach (Button button in Object.FindObjectsByType<Button>(
                FindObjectsInactive.Include))
            {
                Require(button.onClick.GetPersistentEventCount() > 0 &&
                    button.onClick.GetPersistentTarget(0) is UIButtonSound,
                    "Sound must precede actions on " + button.name);
            }
            var audio = Object.FindAnyObjectByType<UIClickManager>();
            Require(audio != null, "UI sound source is missing.");
            var serialized = new SerializedObject(audio);
            Require(serialized.FindProperty("clickSound").objectReferenceValue,
                "Click clip is missing.");
            Require(serialized.FindProperty("hoverSound").objectReferenceValue,
                "Hover clip is missing.");
        }

        private static void ValidateAction(string name, string method)
        {
            Button button = Find<Button>(name);
            Require(button != null, "Missing button " + name);
            Require(Enumerable.Range(0, button.onClick.GetPersistentEventCount())
                .Any(i => button.onClick.GetPersistentMethodName(i) == method &&
                    button.onClick.GetPersistentTarget(i) != null),
                "Missing action " + method + " on " + name);
        }

        private static T Find<T>(string name) where T : Component
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include)
                .FirstOrDefault(c => c.name == name);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        #endregion
    }
}
#endif
