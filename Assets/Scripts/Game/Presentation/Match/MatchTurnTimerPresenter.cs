using System;
using System.Collections;
using KLTN.Game.Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>GameScene timer at Right–Middle, driven by fresh server clock data, never by animation time.</summary>
    public sealed class MatchTurnTimerPresenter : MonoBehaviour
    {
        private GameObject canvasRoot;
        private TMP_Text label;
        private Coroutine readiness;

        private void OnEnable()
        {
            MatchProjectionRegistry.Current.SnapshotChanged += OnSnapshot;
            OnSnapshot(MatchProjectionRegistry.Current.Current);
        }

        private void OnSnapshot(MatchSnapshotDto snapshot)
        {
            if (readiness != null) StopCoroutine(readiness);
            readiness = null;
            if (snapshot?.turnClock?.self?.awaitingPresentation == true && !snapshot.isPaused)
                readiness = StartCoroutine(ReportWhenPresented(snapshot));
        }

        private IEnumerator ReportWhenPresented(MatchSnapshotDto snapshot)
        {
            // Let every SnapshotChanged subscriber start its banners before checking readiness.
            yield return null;
            MatchTransitionBanner[] banners = FindObjectsByType<MatchTransitionBanner>();
            while (Array.Exists(banners, banner => banner != null && banner.IsPlaying)) yield return null;
            NetworkMatchBridge bridge = FindAnyObjectByType<NetworkMatchBridge>();
            if (bridge != null) bridge.RequestDecisionPresented(snapshot);
            readiness = null;
        }

        private void Awake()
        {
            canvasRoot = new GameObject("MatchTurnTimerCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            // A root canvas is required: the HUD component itself is nested under the
            // board canvas, where another nested canvas would inherit its rectangle.
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasRoot, gameObject.scene);
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("TimerRightMiddle", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRoot.transform, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0.5f);
            rect.anchoredPosition = new Vector2(-24, 0);
            rect.sizeDelta = new Vector2(220, 190);
            Image background = panel.GetComponent<Image>();
            background.color = new Color(0.035f, 0.075f, 0.085f, 0.92f);
            background.raycastTarget = false;

            var text = new GameObject("Countdown", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(panel.transform, false);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 10);
            textRect.offsetMax = new Vector2(-10, -10);
            label = text.GetComponent<TextMeshProUGUI>();
            label.fontSize = 23;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.88f, 0.98f, 0.96f);
            label.raycastTarget = false;
        }

        private void OnDisable()
        {
            MatchProjectionRegistry.Current.SnapshotChanged -= OnSnapshot;
            if (readiness != null) StopCoroutine(readiness);
            readiness = null;
            if (canvasRoot != null) canvasRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (canvasRoot != null) Destroy(canvasRoot);
        }

        private void Update()
        {
            MatchTurnClockDto clock = MatchTurnClockRegistry.Current;
            bool visible = clock != null && !string.IsNullOrEmpty(clock.matchId)
                && (clock.self != null || clock.opponent != null || clock.timedOutSeat >= 0);
            canvasRoot.SetActive(visible);
            if (!visible) return;
            if (clock.timedOutSeat >= 0)
            {
                label.text = clock.timedOutSeat == clock.viewerSeat
                    ? "<color=#FF7979>THUA DO HẾT GIỜ</color>\n2 lượt liên tiếp"
                    : "<color=#68E6CF>CHIẾN THẮNG</color>\nĐối thủ hết giờ\n2 lượt liên tiếp";
                return;
            }
            // LocalTime estimates when an outgoing RPC reaches the server. This makes
            // the visible deadline conservative; the server alone grants the fixed 300 ms.
            double now = clock.paused ? clock.serverNow
                : NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalTime.Time : clock.serverNow;
            string self = Format(clock.self, now, "BẠN", clock.selfTimeouts, clock.paused, true);
            string opponent = Format(clock.opponent, now, "ĐỐI THỦ", clock.opponentTimeouts, clock.paused, false);
            label.text = self + (self.Length > 0 && opponent.Length > 0 ? "\n" : "") + opponent;
            if (clock.self == null && clock.selfTimeouts > 0)
                label.text += "\n<color=#FFBA70><size=16>Bạn đã hết giờ 1/2 lượt</size></color>";
        }

        private static string Format(TurnWindowDto window, double now, string owner, int strikes, bool paused, bool self)
        {
            if (window == null) return "";
            string value;
            if (self && MatchTurnClockRegistry.PendingDecisionId == window.decisionId)
                value = "ĐANG XỬ LÝ";
            else if (paused) value = $"TẠM DỪNG · {Math.Ceiling(Math.Max(0, Math.Min(20, window.deadline - now))):0}s";
            else if (window.awaitingPresentation && now < window.startsAt) value = "CHUẨN BỊ · 20s";
            else
            {
                int seconds = (int)Math.Ceiling(Math.Max(0, Math.Min(20, window.deadline - now)));
                value = seconds <= 5 ? $"<color=#FF7979><size=40>{seconds}s</size></color>" : $"<size=40>{seconds}s</size>";
            }
            return owner + "\n" + value + (strikes > 0 ? "\n<color=#FFBA70><size=16>Đã hết giờ 1/2 lượt</size></color>" : "");
        }
    }
}
