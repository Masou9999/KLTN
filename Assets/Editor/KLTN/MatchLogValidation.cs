#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using KLTN.Game.Networking;
using KLTN.Game.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.Game.Editor
{
    /// <summary>Repeatable checks against the installed scene, without a backend or saved test data.</summary>
    public static class MatchLogValidation
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity", OpenSceneMode.Single);
            var modal = UnityEngine.Object.FindAnyObjectByType<MatchLogModal>(FindObjectsInactive.Include);
            Require(modal != null, "GameScene must contain its serialized log UI.");
            var scroll = Field<MatchLogScrollRect>(modal, "scroll");
            var latest = Field<Button>(modal, "latestButton");
            Require(Field<Button>(modal, "openButton").GetComponent<CanvasRenderer>() != null,
                "Log launcher must have a CanvasRenderer.");
            Require(latest.GetComponent<CanvasRenderer>() != null, "Latest arrow must have a CanvasRenderer.");
            var journal = MatchUpdateInboxRegistry.Current.Journal;
            journal.Reset();
            Call(modal, "Awake"); Call(modal, "OnEnable");
            modal.Show();
            Require(!scroll.vertical, "Empty log must not scroll.");
            Require(!scroll.verticalScrollbar.gameObject.activeSelf, "Empty log must hide scrollbar.");
            Require(!latest.gameObject.activeSelf, "Latest arrow must start hidden.");
            var snapshot = new MatchSnapshotDto { matchId = "log-validation", viewerSeat = 0, roundNumber = 5 };
            var samples = new MatchLogDto[40];
            string[] texts = {
                "Ma Trành của đối thủ khi được triệu hồi (đã có đồng minh chết trong vòng này): tiêu diệt Quỷ Cẩu của bạn, chuyển vào nghĩa địa.",
                "Ma Lai của bạn (ô 1) gây 3 sát thương lên Ma Mặt Mâm của đối thủ: 3 → 0. Lá bài bị tiêu diệt, chuyển vào nghĩa địa.",
                "Ma Mặt Mâm của đối thủ kích hoạt Trăn trối: hồi 2 máu cho đối thủ: 10 → 12.",
                "Ma Lai của bạn kích hoạt Hút máu: hồi 3 máu cho bạn: 14 → 17."
            };
            for (int i = 0; i < samples.Length; i++) samples[i] = new MatchLogDto {
                sequence = i + 1, round = i / 8 + 1, category = i % 4 == 1 ? 1 : 0, text = texts[i % 4] };
            journal.Accept(new MatchUpdateDto { snapshot = snapshot, logs = samples });
            Call(modal, "Render"); Canvas.ForceUpdateCanvases();
            Require(scroll.vertical, "Long history must enable scrolling.");
            Require(scroll.verticalNormalizedPosition < .01f, "Opening follows latest entries.");
            scroll.verticalNormalizedPosition = .7f;
            float offset = scroll.content.anchoredPosition.y;
            journal.Accept(new MatchUpdateDto { snapshot = snapshot, logs = new[] {
                new MatchLogDto { sequence = 41, round = 6, category = 0, text = texts[0] } } });
            Call(modal, "Render"); Canvas.ForceUpdateCanvases();
            Require(Mathf.Abs(offset - scroll.content.anchoredPosition.y) < 1, "New logs must preserve reading position.");
            Require(latest.gameObject.activeSelf, "New logs while reading must reveal the circular arrow.");
            var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(200, 200), delta = new Vector2(0, 100) };
            scroll.OnBeginDrag(pointer); scroll.OnDrag(pointer); scroll.OnEndDrag(pointer);
            Require(Mathf.Abs(offset - scroll.content.anchoredPosition.y) < 1, "Dragging the list must do nothing.");
            journal.Accept(new MatchUpdateDto { snapshot = snapshot, logs = samples });
            Require(journal.Entries.Count == 41, "Reconnect replay must not duplicate entries.");
            modal.JumpToLatest();
            Require(!latest.gameObject.activeSelf && scroll.verticalNormalizedPosition < .01f, "Arrow must jump to latest and hide.");
            pointer.scrollDelta = new Vector2(0, -10000);
            scroll.OnScroll(pointer);
            Require(scroll.verticalNormalizedPosition >= -.001f, "Bottom must clamp.");
            pointer.scrollDelta = new Vector2(0, 10000);
            scroll.OnScroll(pointer);
            Require(scroll.verticalNormalizedPosition <= 1.001f, "Top must clamp.");
            Field<Button[]>(modal, "filters")[2].onClick.Invoke(); Call(modal, "Render");
            Require(scroll.content.GetComponentsInChildren<TMPro.TMP_Text>().Length == 10, "Combat filter must show combat entries only.");
            Field<Button[]>(modal, "filters")[0].onClick.Invoke(); Call(modal, "Render");
            Capture(modal, "Logs/match-log-preview.png");
            scroll.verticalNormalizedPosition = .6f;
            journal.Accept(new MatchUpdateDto { snapshot = snapshot, logs = new[] {
                new MatchLogDto { sequence = 42, round = 6, category = 0, text = texts[2] } } });
            Call(modal, "Render");
            Capture(modal, "Logs/match-log-new-entry-preview.png");
            Require(scroll.verticalScrollbar.size > 0 && scroll.verticalScrollbar.size < .5f, "Scrollbar thumb must reflect overflow.");
            modal.Hide(); Require(!modal.IsOpen, "Close must hide overlay.");
            // Result/selection panels are active in the authored scene and normally hidden by runtime Awake.
            // Hide those only in this unsaved preview so the launcher can be inspected unobstructed.
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (canvas.sortingOrder == 100 || canvas.sortingOrder == 20) canvas.gameObject.SetActive(false);
            Capture(modal, "Logs/match-log-button-preview.png");
            modal.Show(); Require(scroll.verticalNormalizedPosition < .01f, "Reopening must show latest.");
            journal.Accept(new MatchUpdateDto { snapshot = new MatchSnapshotDto { matchId = "next-match", viewerSeat = 0 } });
            Call(modal, "Render"); Require(journal.Entries.Count == 0 && !scroll.vertical, "New match must clear history and disable scroll.");
            Call(modal, "OnDisable"); journal.Reset();
            Debug.Log("MATCH_LOG_VALIDATION_PASSED: scene, filters, privacy-ready projection, replay, scroll limits, no drag, follow-latest and reset.");
        }

        private static void Capture(MatchLogModal modal, string path)
        {
            var cameraObject = new GameObject("LogPreviewCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .035f, .045f);
            camera.orthographic = true; camera.orthographicSize = 540;
            camera.cullingMask = 1 << LayerMask.NameToLayer("UI");
            var target = new RenderTexture(1600, 900, 24);
            camera.targetTexture = target;
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (!canvas.isRootCanvas) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 10;
            }
            Canvas.ForceUpdateCanvases(); Call(modal, "Render"); Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG()); RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
#endif
