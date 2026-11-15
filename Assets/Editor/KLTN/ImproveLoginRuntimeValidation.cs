#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using KLTN.UI.Authentication;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    /// <summary>Offline Play Mode smoke check; does not send credentials or contact auth services.</summary>
    public static class ImproveLoginRuntimeValidation
    {
        private const string Pending = "KLTN.ImproveLogin.RuntimeValidation";
        private static int frames;
        private static int step;
        private static readonly List<string> errors = new();

        public static void Run()
        {
            EditorSceneManager.OpenScene(ImproveLoginSceneBuilder.ScenePath);
            // These changes exist only in the unsaved test scene, never in the delivered scene.
            foreach (string name in new[] { "AuthManager", "AppFlowManager", "UGSInitializer" })
                GameObject.Find(name).SetActive(false);
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            Application.logMessageReceived += RecordError;
            EditorApplication.update += Tick;
        }

        private static void RecordError(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors.Add(message);
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || ++frames < 12 || frames % 4 != 0) return;
            int exitCode = 0;
            try
            {
                var nav = Find<ImprovedAuthNavigation>("FormsRoot");
                var manager = Find<AuthUIManager>("UIManager");
                var email = Find<TMP_InputField>("LoginEmail");
                var password = Find<TMP_InputField>("LoginPassword");
                if (step == 0)
                {
                    Require(password.inputType == TMP_InputField.InputType.Password, "Password starts exposed");
                    CheckButtons();
                    Find<Button>("LoginButton").onClick.Invoke();
                    Require(!manager.IsBusy && VisibleNotification(), "Invalid login did not show feedback");
                    email.text = "player@example.com";
                    Find<Button>("ForgotPassword").onClick.Invoke();
                    Require(Find<TMP_InputField>("RecoveryEmail").text == email.text, "Recovery email handoff failed");
                    step++;
                    return;
                }
                if (step == 1)
                {
                    CheckButtons();
                    Find<TMP_InputField>("RecoveryEmail").text = "invalid";
                    Find<Button>("SendRecovery").onClick.Invoke();
                    Require(!manager.IsBusy && VisibleNotification(), "Invalid recovery did not show feedback");
                    manager.TryBeginOperation("Test"); nav.ShowLogin();
                    Require(Find<RectTransform>("RecoveryForm").gameObject.activeSelf, "Busy operation allowed navigation");
                    Require(!Find<Button>("SendRecovery").IsInteractable(), "Busy form remains interactive");
                    manager.EndOperation(); nav.ShowLogin(); nav.ShowRegister();
                    step++;
                    return;
                }
                CheckButtons();
                var registerPassword = Find<TMP_InputField>("RegisterPassword");
                registerPassword.text = "example-secret";
                var eye = registerPassword.GetComponentInChildren<PasswordVisibilityToggle>();
                eye.Toggle(); Require(registerPassword.inputType == TMP_InputField.InputType.Standard, "Reveal failed");
                nav.ShowLogin(); nav.ShowRegister();
                Require(registerPassword.inputType == TMP_InputField.InputType.Password, "Password stays exposed after leaving form");
                Find<Button>("CreateAccount").onClick.Invoke();
                Require(!manager.IsBusy && VisibleNotification(), "Invalid registration did not show feedback");
                nav.ShowLogin();
                Require(errors.Count == 0, string.Join("\n", errors));
                Debug.Log("[ImproveLoginRuntime] PASS: Awake/OnEnable, button raycasts on all panels, validation feedback for all forms, email handoff, busy lock/recovery, password reveal and remasking. Network services disabled for this smoke check.");
            }
            catch (Exception exception)
            {
                exitCode = 1;
                Debug.LogException(exception);
            }
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= RecordError;
            EditorApplication.Exit(exitCode);
        }

        private static bool VisibleNotification() => Object.FindObjectsByType<AuthNotificationItem>().Length > 0;

        private static void CheckButtons()
        {
            Canvas.ForceUpdateCanvases();
            var raycaster = Find<GraphicRaycaster>("Canvas");
            foreach (var button in Object.FindObjectsByType<Button>())
            {
                var hits = new List<RaycastResult>();
                raycaster.Raycast(new PointerEventData(EventSystem.current) {
                    position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position)
                }, hits);
                Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                    "Button blocked or has no hit target: " + button.name + "; hits: " + string.Join(", ",hits.Select(hit=>hit.gameObject.name)));
            }
        }

        private static T Find<T>(string name) where T : Component =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include).First(c => c.name == name);
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
