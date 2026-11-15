#if UNITY_EDITOR
using System;
using KLTN.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Editor
{
    public static class MatchLogInstaller
    {
        [MenuItem("Tools/KLTN/Install Match Log UI")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity", OpenSceneMode.Single);
            if (UnityEngine.Object.FindAnyObjectByType<MatchLogModal>(FindObjectsInactive.Include) != null) return;
            var settings = GameObject.Find("SettingsButton")?.GetComponent<Button>();
            if (settings == null) throw new InvalidOperationException("GameScene SettingsButton was not found.");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Font/XTypewriter-Regular SDF.asset");
            if (font == null) throw new InvalidOperationException("Vietnamese UI font was not found.");
            MatchLogModal.Build(settings, font);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Match log UI installed in GameScene.");
        }
    }
}
#endif
