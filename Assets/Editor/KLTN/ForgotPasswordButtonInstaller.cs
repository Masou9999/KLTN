#if UNITY_EDITOR
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Adds a "Quên mật khẩu?" text button to the email login form in LoginScene and wires it
    /// to <see cref="LoginUI.OnForgotPasswordButtonClicked"/> (FR-AUTH-10 / UC-21), so the scene
    /// YAML does not have to be edited by hand. Safe to run again; an existing button is kept.
    /// </summary>
    public static class ForgotPasswordButtonInstaller
    {
        #region Configuration

        private const string ScenePath = "Assets/Scenes/LoginScene.unity";
        private const string ButtonName = "ForgotPasswordButton";
        private const string Label = "Quên mật khẩu?";
        private const float Gap = 16f;

        private static readonly Vector2 ButtonSize = new Vector2(360f, 44f);
        private static readonly Color LabelColor = new Color(0.86f, 0.86f, 0.9f, 1f);

        #endregion

        #region Menu

        [MenuItem("Tools/KLTN/Install Forgot Password Button (LoginScene)")]
        public static void Install()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include)
                .Where(b => b.gameObject.scene == scene)
                .ToArray();

            if (buttons.Any(b => HasListener(b, nameof(LoginUI.OnForgotPasswordButtonClicked))))
            {
                Debug.Log("[ForgotPassword] LoginScene already has a forgot password button.");
                return;
            }

            Button loginButton = buttons.FirstOrDefault(
                b => HasListener(b, nameof(LoginUI.OnLoginButtonClicked)));
            LoginUI loginUI = loginButton != null
                ? loginButton.onClick.GetPersistentTarget(0) as LoginUI
                : null;

            if (loginButton == null || loginUI == null)
            {
                Debug.LogError(
                    "[ForgotPassword] Could not find the login button wired to LoginUI. " +
                    "Add a button manually (OnClick -> LoginUI.OnForgotPasswordButtonClicked).");
                return;
            }

            Button forgot = CreateTextButton(loginButton);
            UnityEventTools.AddPersistentListener(
                forgot.onClick,
                new UnityAction(loginUI.OnForgotPasswordButtonClicked));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = forgot.gameObject;
            Debug.Log(
                "[ForgotPassword] Added ForgotPasswordButton above the login button. " +
                "Adjust its position in the scene if needed.");
        }

        #endregion

        #region Helpers

        private static Button CreateTextButton(Button loginButton)
        {
            var loginRect = (RectTransform)loginButton.transform;
            var go = new GameObject(ButtonName, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(go, "Add Forgot Password Button");
            go.transform.SetParent(loginRect.parent, false);
            go.transform.SetSiblingIndex(loginRect.GetSiblingIndex());

            // Placed just above the login button, in the gap below the Google login button.
            var rect = (RectTransform)go.transform;
            rect.anchorMin = loginRect.anchorMin;
            rect.anchorMax = loginRect.anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = ButtonSize;
            float offset = (loginRect.rect.height + ButtonSize.y) * 0.5f + Gap;
            rect.anchoredPosition = loginRect.anchoredPosition + new Vector2(0f, offset);

            // Transparent hit area: the button reads as a text link.
            Image hitArea = go.GetComponent<Image>();
            hitArea.color = new Color(0f, 0f, 0f, 0f);

            Button button = go.GetComponent<Button>();
            button.targetGraphic = hitArea;

            var labelGo = new GameObject("Text (TMP)", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            TMP_Text template = loginButton.GetComponentInChildren<TMP_Text>(true);

            if (template != null)
            {
                label.font = template.font;
            }

            label.text = Label;
            label.fontSize = 28f;
            label.fontStyle = FontStyles.Underline;
            label.color = LabelColor;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return button;
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

        #endregion
    }
}
#endif
