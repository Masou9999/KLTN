#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Game.Editor
{
    public static class LoginStateCheckingSceneBuilder
    {
        #region Configuration

        public const string ScenePath = "Assets/Scenes/LoginStateChecking.unity";

        #endregion

        #region Public Commands

        [MenuItem("Tools/KLTN/Build LoginStateChecking")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                throw new InvalidOperationException("LoginStateChecking already exists; edit the existing scene.");

            Scene source = EditorSceneManager.OpenScene("Assets/Scenes/LoginScene.unity", OpenSceneMode.Single);
            var background = Object.FindObjectsByType<Image>(FindObjectsInactive.Include)
                .Single(x => x.name == "Background");
            var sourceCanvas = background.GetComponentInParent<Canvas>();
            var sourceScaler = sourceCanvas.GetComponent<CanvasScaler>();
            var font = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include)
                .Single(x => x.name == "LoadingLabel").font;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            var sourceCamera = source.GetRootGameObjects().Single(x => x.CompareTag("MainCamera"));
            var cameraObject = Object.Instantiate(sourceCamera);
            cameraObject.name = sourceCamera.name;
            SceneManager.MoveGameObjectToScene(cameraObject, scene);

            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            EditorUtility.CopySerialized(sourceScaler, canvasObject.GetComponent<CanvasScaler>());
            var bg = Object.Instantiate(background.gameObject, canvasObject.transform);
            bg.name = "Background";
            // Preserve the login artwork's centered design surface and scaling.
            var overlay = Image("BlackOverlay", canvasObject.transform, null, new Color(0, 0, 0, .55f));
            Stretch(overlay.rectTransform);
            var spinnerAsset = AssetDatabase.LoadAllAssetsAtPath("Assets/Ui/loading-spinner.png").OfType<Sprite>().Single();
            var spinner = Image("LoadingSpinner", canvasObject.transform, spinnerAsset, Color.white);
            spinner.rectTransform.sizeDelta = new Vector2(96, 96);
            spinner.preserveAspect = true;
            var labelObject = new GameObject("StatusLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(canvasObject.transform, false);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.text = "Đang kiểm tra trạng thái đăng nhập...";
            label.fontSize = 30;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(1000, 70);
            label.rectTransform.anchoredPosition = new Vector2(0, -110);

            foreach (var root in source.GetRootGameObjects().Where(x =>
                         x.GetComponent<AuthManager>() != null || x.GetComponent<AppFlowManager>() != null))
            {
                var clone = Object.Instantiate(root);
                clone.name = root.name;
                SceneManager.MoveGameObjectToScene(clone, scene);
            }
            var controller = canvasObject.AddComponent<LoginStateCheckingController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("spinner").objectReferenceValue = spinner.rectTransform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.CloseScene(source, true);
            SceneManager.SetActiveScene(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(x => x.path != ScenePath)).ToArray();
            AssetDatabase.SaveAssets();
            Validate();
        }

        [MenuItem("Tools/KLTN/Validate LoginStateChecking")]
        public static void Validate()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (Camera.main == null || !Camera.main.isActiveAndEnabled)
                throw new InvalidOperationException("Startup scene needs an active Main Camera.");
            if (EditorBuildSettings.scenes.First(x => x.enabled).path != ScenePath ||
                Object.FindObjectsByType<AuthManager>().Length != 1 ||
                Object.FindObjectsByType<GoogleAuthService>().Length != 1 ||
                Object.FindObjectsByType<AppFlowManager>().Length != 1)
                throw new InvalidOperationException("Invalid startup scene or service wiring.");
            var controller = Object.FindAnyObjectByType<LoginStateCheckingController>();
            if (controller == null || new SerializedObject(controller).FindProperty("spinner").objectReferenceValue == null)
                throw new InvalidOperationException("Missing loading controller/spinner.");
            var label = Object.FindAnyObjectByType<TextMeshProUGUI>();
            if (label.text != "Đang kiểm tra trạng thái đăng nhập..." || label.font == null)
                throw new InvalidOperationException("Invalid status label.");
            Debug.Log("[LoginStateChecking] Scene and startup wiring validated.");
        }

        #endregion

        #region Helpers

        private static Image Image(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        #endregion
    }
}
#endif
