using System;
using System.Collections;
using KLTN.Game.Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// GameScene timer at Right–Middle, driven by fresh server clock data, never by animation
    /// time. Shows one countdown only: the local player's while they must decide, otherwise
    /// the opponent's. During Mulligan it sits above the Mulligan backdrop.
    /// </summary>
    public sealed class MatchTurnTimerPresenter : MonoBehaviour
    {
        // Trên backdrop Mulligan (Phase 620), cùng nhóm với TopBar được nâng lên (630).
        private const int AboveMulliganOffset = 31;

        private GameObject canvasRoot;
        private Canvas timerCanvas;
        private bool aboveMulligan;
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
            MatchSorting.Apply(canvas, MatchSorting.Hud);
            timerCanvas = canvas;
            CanvasScaler scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var panel = new GameObject("TimerRightMiddle", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasRoot.transform, false);
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0.5f);
            rect.anchoredPosition = new Vector2(-24, 0);
            rect.sizeDelta = new Vector2(220, 120);
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
            UpdateLayer();
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
            // Chỉ hiện một đồng hồ: của mình khi đang phải quyết định, nếu không thì của đối thủ.
            bool showSelf = clock.self != null;
            label.text = Format(showSelf ? clock.self : clock.opponent, now, showSelf ? "Bạn" : "Đối thủ");
        }

        private void UpdateLayer()
        {
            bool inMulligan = MatchProjectionRegistry.Current.Current?.roundNumber == 0;
            if (inMulligan == aboveMulligan || timerCanvas == null) return;
            if (inMulligan) MatchSorting.Apply(timerCanvas, MatchSorting.Phase, AboveMulliganOffset);
            else MatchSorting.Apply(timerCanvas, MatchSorting.Hud);
            aboveMulligan = inMulligan;
        }

        private static string Format(TurnWindowDto window, double now, string owner)
        {
            double remaining = window.awaitingPresentation && now < window.startsAt
                ? 20
                : Math.Max(0, Math.Min(20, window.deadline - now));
            int seconds = (int)Math.Ceiling(remaining);
            string value = $"{seconds / 60:00}:{seconds % 60:00}";
            if (seconds <= 5) value = $"<color=#FF7979>{value}</color>";
            return owner + "\n<size=40>" + value + "</size>";
        }
    }
}
