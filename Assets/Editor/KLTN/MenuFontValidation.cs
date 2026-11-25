#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Ensures MainMenu, Shop and Leaderboard text uses the Be Vietnam Pro font family.
    /// </summary>
    public static class MenuFontValidation
    {
        private const string FontFolder = "Assets/Ui/ImproveLogin/Fonts/";
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/MainMenu.unity",
            "Assets/Scenes/ShopScene.unity",
            "Assets/Scenes/LeaderboardScene.unity",
            "Assets/Scenes/GameScene.unity",
        };
        private static readonly string[] Prefabs =
        {
            "Assets/Prefabs/UI/ShopCardItem.prefab",
            "Assets/Prefabs/Networking/NetworkCardView.prefab",
        };
        // Components that build TMP text at runtime from a serialized "font" field.
        private static readonly string[] RuntimeFontOwners =
        {
            "KLTN.Game.Presentation.OpponentProfileView",
            "KLTN.Game.Presentation.MatchLogModal",
        };

        [MenuItem("Tools/KLTN/Validate Menu Fonts")]
        public static void Validate()
        {
            if (!Application.isBatchMode &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var errors = new List<string>();
            foreach (string scenePath in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects())
                {
                    Check(scenePath, root, errors);
                    CheckRuntimeFonts(scenePath, root, errors);
                }
            }
            foreach (string prefabPath in Prefabs)
            {
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try { Check(prefabPath, root, errors); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            if (errors.Count > 0)
            {
                throw new System.InvalidOperationException(
                    "Menu font validation failed:\n" + string.Join("\n", errors));
            }
            Debug.Log("Menu font validation passed.");
        }

        private static void Check(string owner, GameObject root, List<string> errors)
        {
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string fontPath = AssetDatabase.GetAssetPath(text.font);
                bool family = text.font != null && fontPath.StartsWith(FontFolder + "BeVietnamPro-");
                bool material = text.font != null && text.fontSharedMaterial == text.font.material;
                if (!family || !material)
                {
                    errors.Add($"{owner}: {Path(text.transform)} uses " +
                        $"'{(text.font != null ? text.font.name : "null")}'" +
                        (material ? "" : " with a mismatched material"));
                }
            }
        }

        private static void CheckRuntimeFonts(string owner, GameObject root, List<string> errors)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || !RuntimeFontOwners.Contains(behaviour.GetType().FullName))
                {
                    continue;
                }
                var font = new SerializedObject(behaviour).FindProperty("font")?.objectReferenceValue;
                if (font == null || !AssetDatabase.GetAssetPath(font).StartsWith(FontFolder + "BeVietnamPro-"))
                {
                    errors.Add($"{owner}: {Path(behaviour.transform)} {behaviour.GetType().Name}.font " +
                        $"is '{(font != null ? font.name : "null")}'");
                }
            }
        }

        private static string Path(Transform t) =>
            string.Join("/", t.GetComponentsInParent<Transform>(true).Reverse().Select(x => x.name));
    }
}
#endif
