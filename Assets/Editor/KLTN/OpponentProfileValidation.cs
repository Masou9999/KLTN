using System;
using System.Reflection;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KLTN.Game.Editor
{
    /// <summary>Checks the authored profile and fixed text geometry without saving the scene.</summary>
    public static class OpponentProfileValidation
    {
        [MenuItem("Tools/KLTN/Validate Opponent Profile")]
        public static void RunBatch()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/GameScene.unity");
            try
            {
                OpponentProfileView view = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var candidate = root.GetComponentInChildren<OpponentProfileView>(true);
                    if (candidate != null) view = candidate;
                }
                Require(view != null, "Missing opponent profile view");
                var properties = new SerializedObject(view);
                Require(properties.FindProperty("icon").objectReferenceValue != null,
                    "Missing profile sprite");
                Require(properties.FindProperty("font").objectReferenceValue != null,
                    "Missing Vietnamese font");
                view.EnsureVisuals();
                var clip = (RectTransform)view.transform.Find("ProfileReveal");
                var name = clip.Find("FixedContent/Username").GetComponent<TMP_Text>();
                var standing = clip.Find("FixedContent/Standing").GetComponent<TMP_Text>();
                Invoke(view, "Render", new MatchSnapshotDto
                {
                    matchId = "profile-validation",
                    opponent = new PlayerViewDto
                    {
                        profile = new PlayerProfileDto
                        {
                            username = "Người chơi <b>Đối thủ</b>", points = 420, seasonId = 1,
                        },
                    },
                });
                Require(standing.text == "Kim Cương - 420", "Rank must follow current policy");
                Require(!name.richText, "Usernames cannot inject TMP markup");
                Require(!name.enableAutoSizing && !standing.enableAutoSizing,
                    "Text must not resize");
                var position = name.rectTransform.position;
                var size = name.rectTransform.rect.size;
                float fontSize = name.fontSize;
                foreach (float progress in new[] { 0.01f, 0.25f, 0.5f, 0.75f, 1f, 0.5f, 0f })
                {
                    Field("progress").SetValue(view, progress);
                    Invoke(view, "ApplyReveal");
                    Canvas.ForceUpdateCanvases();
                    Require(Vector3.Distance(position, name.rectTransform.position) < 0.01f,
                        "Text moved during reveal");
                    Require(name.rectTransform.rect.size == size && name.fontSize == fontSize,
                        "Text resized during reveal");
                }
                Field("progress").SetValue(view, 0.4f);
                view.OnPointerEnter(new PointerEventData(null));
                view.OnPointerExit(new PointerEventData(null));
                Require((float)Field("progress").GetValue(view) == 0.4f,
                    "Direction changes must preserve current progress");
                Require(!(bool)Field("hovered").GetValue(view), "Exit must close the panel");
                Invoke(view, "Render", new MatchSnapshotDto { matchId = "next-match" });
                Require(name.text == "Đối thủ" && standing.text.Contains("Chưa có"),
                    "New matches must not retain old profiles");
                Require((float)Field("progress").GetValue(view) == 0,
                    "New matches start closed");
                Debug.Log("Opponent profile validation passed: assets, fixed text, reversal, reset.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static FieldInfo Field(string name) => typeof(OpponentProfileView)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Invoke(OpponentProfileView view, string name, params object[] args)
        {
            typeof(OpponentProfileView).GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Instance).Invoke(view, args);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
