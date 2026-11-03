#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KLTN.Sandbox.Editor
{
    /// <summary>
    /// Creates an isolated Editor scene without changing GameScene or builds.
    /// </summary>
    public static class AbilitySandboxBuilder
    {
        #region Configuration

        public const string ScenePath =
            "Assets/_Temp/AbilityAnimationSandbox/AbilityAnimationSandbox.unity";
        private static TMP_FontAsset font;
        private static readonly Color Cream = new Color(0.96f, 0.9f, 0.76f);
        private static readonly Color Red = new Color(0.38f, 0.12f, 0.14f);

        #endregion

        #region Scene Creation

        [MenuItem("Tools/KLTN/Temporary/Build Ability Animation Sandbox")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            if (!Application.isBatchMode
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            EditorSceneManager.SaveScene(scene, ScenePath);
            font = FindAll<TMP_Text>().First(text => text.font != null).font;
            Canvas canvas = FindAll<Canvas>().First(value => value.isRootCanvas);
            MatchBoardPresenter board = FindAll<MatchBoardPresenter>().Single();
            board.mulliganPresenter = null;
            foreach (MulliganPresenter mulligan in FindAll<MulliganPresenter>())
            {
                mulligan.gameObject.SetActive(false);
                Object.DestroyImmediate(mulligan);
            }
            foreach (NetworkMatchBridge bridge in FindAll<NetworkMatchBridge>())
            {
                Object.DestroyImmediate(bridge.gameObject);
            }
            foreach (MatchResultPresenter result in FindAll<MatchResultPresenter>())
            {
                Get<GameObject>(result, "resultPanel")?.SetActive(false);
                Object.DestroyImmediate(result);
            }
            foreach (SurrenderUI surrender in FindAll<SurrenderUI>())
            {
                Get<GameObject>(surrender, "surrenderConfirmPanel")?.SetActive(false);
                Object.DestroyImmediate(surrender);
            }
            foreach (TurnBannerCoordinator banner in FindAll<TurnBannerCoordinator>())
            {
                banner.gameObject.SetActive(false);
            }
            foreach (PovDebugPresenter debug in FindAll<PovDebugPresenter>())
            {
                debug.gameObject.SetActive(false);
            }
            foreach (Button button in FindAll<Button>())
            {
                if (button.name == "QuitButton" || button.name == "ReturnLobbyButton")
                {
                    button.gameObject.SetActive(false);
                }
            }
            // Do not subtract a staged cost from the sandbox's permanent 10-mana HUD.
            Set(FindAll<MatchHudPresenter>().Single(), "actionControls", null);
            var runner = new GameObject("Ability Sandbox (Editor Only)")
                .AddComponent<AbilitySandboxRunner>();
            runner.controls = FindAll<MatchDebugControls>().Single();
            runner.board = board;
            runner.director = FindAll<CombatAnimationDirector>().Single();
            runner.settingsPanel = Get<GameObject>(FindAll<GameSettingsUI>().Single(),
                "settingsPanel");
            Button settings = FindAll<Button>().Single(b => b.name == "SettingsButton");
            RectTransform settingsRect = (RectTransform)settings.transform;
            runner.addButton = Button("SandboxAddCards", settings.transform.parent,
                "THÊM THẺ", new Vector2(570, 429), new Vector2(220, 70));
            runner.resetButton = Button("SandboxReset", settings.transform.parent,
                "RESET", new Vector2(360, 429), new Vector2(160, 70));
            // Match the existing top-bar anchor convention.
            foreach (Button button in new[] { runner.addButton, runner.resetButton })
            {
                var rect = (RectTransform)button.transform;
                rect.anchorMin = settingsRect.anchorMin;
                rect.anchorMax = settingsRect.anchorMax;
            }
            runner.modal = BuildModal(canvas.transform);
            runner.modal.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateScene();
            Debug.Log("[AbilitySandbox] Scene created: " + ScenePath);
        }

        private static AbilitySandboxModal BuildModal(Transform canvas)
        {
            RectTransform overlay = Rect("SandboxCardModal", canvas,
                Vector2.zero, Vector2.zero);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            overlay.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.78f);
            var modal = overlay.gameObject.AddComponent<AbilitySandboxModal>();
            RectTransform panel = Rect("Window", overlay, Vector2.zero,
                new Vector2(1360, 850));
            panel.gameObject.AddComponent<Image>().color =
                new Color(0.10f, 0.075f, 0.09f);
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.65f, 0.48f, 0.26f);
            outline.effectDistance = new Vector2(2, -2);
            Text("Title", panel, "THÊM / XÓA THẺ TEST", new Vector2(0, 360),
                new Vector2(1200, 65), 42);
            Text("Subtitle", panel, "Chỉnh số lượng phía bạn · Xác nhận để áp dụng",
                new Vector2(0, 300), new Vector2(1200, 45), 26);
            modal.reserveTitle = Text("ReserveTitle", panel, "RESERVE ZONE  3/4",
                new Vector2(-330, 235), new Vector2(600, 50), 30);
            modal.handTitle = Text("HandTitle", panel, "DRAW HAND  0/10",
                new Vector2(330, 235), new Vector2(600, 50), 30);
            var rows = new List<AbilitySandboxModal.Row>();
            AddRows(panel, AbilitySandboxModel.ReserveIds, true, -330, rows);
            AddRows(panel, AbilitySandboxModel.HandIds, false, 330, rows);
            modal.rows = rows.ToArray();
            Text("ReserveHint", panel,
                "Lá kỹ năng vào Reserve qua triệu hồi.\n"
                    + "Nút − chỉ xóa, không kích hoạt chết.",
                new Vector2(330, -180), new Vector2(590, 100), 23);
            modal.errorLabel = Text("Error", panel, string.Empty,
                new Vector2(0, -300), new Vector2(1240, 45), 24);
            modal.cancelButton = Button("Cancel", panel, "HỦY",
                new Vector2(-175, -365), new Vector2(280, 70));
            modal.confirmButton = Button("Confirm", panel, "XÁC NHẬN",
                new Vector2(175, -365), new Vector2(280, 70));
            return modal;
        }

        private static void AddRows(Transform parent, string[] ids, bool reserve,
            float x, List<AbilitySandboxModal.Row> rows)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var asset = AssetDatabase.LoadAssetAtPath<CMCMProductions.Card>(
                    "Assets/Resources/Cards/" + id + ".asset");
                RectTransform row = Rect((reserve ? "Reserve_" : "Hand_") + id,
                    parent, new Vector2(x, 170 - i * 64), new Vector2(600, 58));
                row.gameObject.AddComponent<Image>().color =
                    new Color(0.18f, 0.135f, 0.15f);
                TMP_Text label = Text("Name", row, asset.cardName,
                    new Vector2(-125, 0), new Vector2(300, 55), 28);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                var entry = new AbilitySandboxModal.Row
                {
                    id = id,
                    reserve = reserve,
                    minus = Button("Minus", row, "−", new Vector2(75, 0),
                        new Vector2(55, 48)),
                    count = Text("Count", row,
                        reserve && i < 2 ? (i == 0 ? "2" : "1") : "0",
                        new Vector2(155, 0), new Vector2(70, 48), 30),
                    plus = Button("Plus", row, "+", new Vector2(235, 0),
                        new Vector2(55, 48))
                };
                rows.Add(entry);
            }
        }

        #endregion

        #region Validation And Helpers

        public static void ValidateScene()
        {
            if (FindAll<NetworkMatchBridge>().Any()
                || FindAll<Unity.Netcode.NetworkManager>().Any()
                || FindAll<AuthManager>().Any()
                || EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
            {
                throw new InvalidOperationException(
                    "Sandbox must be isolated from online/builds.");
            }
            foreach (Transform transform in FindAll<Transform>())
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                    transform.gameObject) != 0)
                {
                    throw new InvalidOperationException(
                        "Missing script: " + transform.name);
                }
            }
        }

        private static RectTransform Rect(string name, Transform parent,
            Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name,
                typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static TMP_Text Text(string name, Transform parent, string value,
            Vector2 position, Vector2 size, float fontSize)
        {
            var text = Rect(name, parent, position, size).gameObject
                .AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.color = Cream;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static Button Button(string name, Transform parent, string label,
            Vector2 position, Vector2 size)
        {
            RectTransform rect = Rect(name, parent, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Red;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.2f, 1.1f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            button.colors = colors;
            Text("Label", rect, label, Vector2.zero, size, 28);
            return button;
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

        private static T[] FindAll<T>() where T : Component
        {
            return SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        }

        #endregion
    }
}
#endif
