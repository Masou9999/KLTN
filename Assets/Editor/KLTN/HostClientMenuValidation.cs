using System;
using System.IO;
using System.Linq;
using System.Reflection;
using KLTN.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KLTN.Editor
{
    /// <summary>Validates the authored menu in an isolated preview scene, without UGS.</summary>
    public static class HostClientMenuValidation
    {
        #region Configuration

        private const string Output = "Logs/HostClient";
        private const string ScenePath = "Assets/Scenes/HostClientMenu.unity";
        private const string FontPath =
            "Assets/Ui/HostClient/Fonts/PatrickHand-Regular SDF.asset";

        #endregion

        #region Editor Entry Points

        [MenuItem("Tools/KLTN/Validate Host Client Menu")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            Scene scene = default;
            try
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                Require(font != null && font.sourceFontFile != null, "Patrick Hand source");
                string characters = new string(Enumerable.Range(32, 95)
                    .Concat(Enumerable.Range(0x1EA0, 90))
                    .Concat(new[] { 0x0102, 0x0103, 0x0110, 0x0111, 0x01A0,
                        0x01A1, 0x01AF, 0x01B0, 0x0128, 0x0129, 0x0168, 0x0169 })
                    .Select(c => (char)c).ToArray());
                font.TryAddCharacters(characters, out string missing);
                Require(string.IsNullOrEmpty(missing), "Vietnamese glyph coverage: " + missing);
                // Bake only the new font; never save unrelated dirty assets or open scenes.
                EditorUtility.SetDirty(font);
                foreach (var atlas in font.atlasTextures) EditorUtility.SetDirty(atlas);
                AssetDatabase.SaveAssetIfDirty(font);

                scene = EditorSceneManager.OpenPreviewScene(ScenePath);
                var view = Find<HostClientMenuView>(scene);
                Require(view != null, "Menu view");
                var serialized = new SerializedObject(view);
                var prop = serialized.GetIterator();
                while (prop.NextVisible(true))
                    if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        Require(prop.objectReferenceValue != null, "Reference: " + prop.name);
                var code = Field<TMP_InputField>(view, "roomCode");
                var join = Field<TMP_InputField>(view, "joinInput");
                var modal = Field<GameObject>(view, "joinModal");
                var client = Field<Button>(view, "clientButton");
                var host = Field<Button>(view, "hostButton");
                var back = Field<Button>(view, "backButton");
                Require(code.readOnly, "Room code is selectable/read-only");
                Require(!modal.activeSelf, "Modal initially hidden");
                Require(!Field<GameObject>(view, "roomCodeGroup").activeSelf,
                    "Room code initially hidden");
                Require(back.GetComponentsInChildren<TMP_Text>(true).Length == 0,
                    "Back is icon only");
                Require(modal.GetComponentsInChildren<Button>(true).Length == 2,
                    "Modal has Cancel and Confirm only");
                Require(modal.GetComponent<Image>().raycastTarget, "Overlay blocks raycasts");

                Invoke(view, "Awake");
                Invoke(view, "OnEnable");
                client.onClick.Invoke();
                Require(modal.activeSelf && !host.interactable && !back.interactable,
                    "Client opens modal and blocks menu navigation");
                Field<Button>(view, "cancelButton").onClick.Invoke();
                Require(!modal.activeSelf && host.interactable, "Cancel closes modal");
                client.onClick.Invoke();
                join.text = "  ";
                Field<Button>(view, "confirmButton").onClick.Invoke();
                Require(!modal.activeSelf, "Confirm closes modal even with empty input");
                Require(Field<TMP_Text>(view, "statusLabel").text == "Vui lòng nhập mã phòng.",
                    "Empty code handled locally without UGS");
                view.SetBusy(true);
                Require(!host.interactable && !client.interactable && back.interactable,
                    "Busy state blocks duplicates but permits leaving");
                view.SetBusy(false);
                Field<TMP_Text>(view, "statusLabel").text = string.Empty;
                Render(scene, "idle");

                var visual = host.GetComponent<PaperButtonVisual>();
                visual.OnPointerEnter(null);
                Require(Field<Image>(visual, "hoverArt").enabled
                    && !Field<Image>(visual, "normalArt").enabled, "Hover swaps art");
                Render(scene, "hover");
                visual.OnPointerExit(null);
                view.ShowRoomCode("7CPJN6");
                Field<TMP_Text>(view, "statusLabel").text =
                    "Đã tạo phòng. Đang chờ người chơi thứ hai.";
                Require(code.text == "7CPJN6" && code.readOnly && !host.interactable,
                    "Host completion exposes read-only code and blocks duplicate host");
                Render(scene, "host-ready");
                view.HideRoomCode();
                Field<TMP_Text>(view, "statusLabel").text = string.Empty;
                client.onClick.Invoke();
                Render(scene, "join-modal");
                Render(scene, "join-modal-4x3", 1024, 768);
                Render(scene, "join-modal-ultrawide", 2560, 1080);
                Invoke(view, "OnDisable");
                File.WriteAllText(Output + "/result.txt",
                    "PASS: font import/Vietnamese, scene references, initial state, modal "
                    + "open/cancel/confirm, overlay, read-only code, busy/leave and hover.\n"
                    + "Preview renders created. No live UGS, clipboard or Play Mode test.\n");
                Debug.Log("[HostClientCheck] PASS (preview scene, no live session)");
            }
            catch (Exception exception)
            {
                File.WriteAllText(Output + "/result.txt", "FAIL\n" + exception);
                Debug.LogException(exception);
            }
            finally
            {
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        #endregion

        #region Preview And Assertions

        private static void Render(Scene scene, string name, int width = 1920,
            int height = 1080)
        {
            var camera = Find<Camera>(scene);
            var canvas = Find<Canvas>(scene);
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.orthographicSize = 540;
            camera.aspect = (float)width / height;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            var target = new RenderTexture(width, height, 24);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true))
                    text.ForceMeshUpdate(true);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static T Find<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true)).FirstOrDefault();
        }

        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void Invoke(object target, string name)
        {
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, null);
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        #endregion
    }
}
