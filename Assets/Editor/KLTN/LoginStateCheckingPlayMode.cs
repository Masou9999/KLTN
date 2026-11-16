#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KLTN.Game.Editor
{
    /// <summary>Starts interactive Play Mode at the login check without changing the editing scene.</summary>
    [InitializeOnLoad]
    public static class LoginStateCheckingPlayMode
    {
        static LoginStateCheckingPlayMode()
        {
            EditorApplication.delayCall += ConfigureStartScene;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) ConfigureStartScene();
        }

        private static void ConfigureStartScene()
        {
            // Leave automated scene validation and batch test runs in control of their own scenes.
            if (Application.isBatchMode) return;

            // Clear the previous override so the sandbox runs directly.
            if (SceneManager.GetActiveScene().name == "AbilityAnimationSandbox")
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(LoginStateCheckingSceneBuilder.ScenePath);
            if (scene == null)
            {
                Debug.LogError("Cannot start Play Mode at LoginStateChecking: the scene asset is missing.");
                return;
            }

            EditorSceneManager.playModeStartScene = scene;
        }
    }
}
#endif
