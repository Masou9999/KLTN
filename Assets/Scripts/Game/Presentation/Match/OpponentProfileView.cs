using System.Globalization;
using KLTN.Game.Content;
using KLTN.Game.Domain.Ranking;
using KLTN.Game.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>Reveals fixed profile content by resizing only its clipping rectangle.</summary>
    public sealed class OpponentProfileView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ICanvasRaycastFilter
    {
        #region Serialized Fields

        [SerializeField] private Sprite icon;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField, Min(0.05f)] private float duration = 0.3f;
        [SerializeField] private Vector2 expandedSize = new Vector2(420, 116);

        #endregion

        #region Runtime State

        private RectTransform clip;
        private TMP_Text username;
        private TMP_Text standing;
        private MatchClientProjection projection;
        private float progress;
        private bool hovered;
        private string matchId;
        private static readonly Color Accent = new Color32(161, 149, 148, 255);

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
#if UNITY_SERVER
            return;
#else
            EnsureVisuals();
            projection = MatchProjectionRegistry.Current;
            projection.SnapshotChanged += Render;
            CloseImmediately();
            Render(projection.Current);
#endif
        }

        private void OnDisable()
        {
            if (projection != null) projection.SnapshotChanged -= Render;
            projection = null;
            matchId = null;
            CloseImmediately();
        }

        private void Update()
        {
            if (clip == null) return;
            // Reset() does not publish SnapshotChanged; clear stale data on session teardown.
            if (projection != null && projection.Current == null && matchId != null)
                Render(null);
            progress = Mathf.MoveTowards(progress, hovered ? 1f : 0f,
                Time.unscaledDeltaTime / Mathf.Max(0.05f, duration));
            ApplyReveal();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) CloseImmediately();
        }

        #endregion

        #region Hover And Projection

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;
        public void OnPointerExit(PointerEventData eventData) => hovered = false;

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect, screenPoint, eventCamera, out Vector2 local)) return false;
            float radius = Mathf.Min(rect.rect.width, rect.rect.height) * 0.5f;
            return (local - rect.rect.center).sqrMagnitude <= radius * radius;
        }

        private void Render(MatchSnapshotDto snapshot)
        {
            if (username == null) return;
            if (matchId != snapshot?.matchId)
            {
                matchId = snapshot?.matchId;
                CloseImmediately();
            }
            PlayerProfileDto profile = snapshot?.opponent?.profile;
            username.text = string.IsNullOrWhiteSpace(profile?.username)
                ? "Đối thủ" : profile.username;
            standing.text = profile == null || profile.points < 0 || profile.seasonId <= 0
                ? "Chưa có thông tin xếp hạng"
                : RankCatalog.FallbackName(RankPolicy.TierFor(profile.points)) + " - "
                    + profile.points.ToString(CultureInfo.InvariantCulture);
        }

        private void CloseImmediately()
        {
            hovered = false;
            progress = 0;
            ApplyReveal();
        }

        private void ApplyReveal()
        {
            if (clip == null) return;
            clip.sizeDelta = expandedSize * progress;
            clip.gameObject.SetActive(progress > 0);
        }

        #endregion

        #region Visual Construction

        /// <summary>Builds the same fixed layout for runtime and the Editor preview.</summary>
        public void EnsureVisuals()
        {
            if (clip != null) return;
            clip = Node("ProfileReveal", transform);
            clip.anchorMin = clip.anchorMax = new Vector2(0.5f, 0.5f);
            clip.pivot = new Vector2(0, 1);
            clip.anchoredPosition = new Vector2(0, 30);
            Fill(clip, Accent);
            clip.gameObject.AddComponent<RectMask2D>();

            var background = Node("Background", clip);
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.offsetMin = new Vector2(2, 2);
            background.offsetMax = new Vector2(-2, -2);
            Fill(background, new Color32(8, 8, 8, 255));

            // Top-left anchors deliberately do not stretch with the clipping viewport.
            var content = Node("FixedContent", clip);
            content.anchorMin = content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, 1);
            content.anchoredPosition = new Vector2(58, -16);
            content.sizeDelta = new Vector2(344, 84);
            username = Label("Username", content, 0, 28);
            standing = Label("Standing", content, -46, 25);

            var disc = Node("IconBackdrop", transform);
            disc.sizeDelta = new Vector2(91, 91);
            var graphic = disc.gameObject.AddComponent<ProfileDiscGraphic>();
            graphic.color = new Color32(8, 8, 8, 255);
            graphic.raycastTarget = false;
            var symbol = Node("ProfileIcon", transform);
            symbol.sizeDelta = new Vector2(91, 91);
            var image = Fill(symbol, Color.white);
            image.sprite = icon;
            image.preserveAspect = true;
            CloseImmediately();
        }

        private TMP_Text Label(string name, Transform parent, float y, float size)
        {
            var rect = Node(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(0, y);
            rect.sizeDelta = new Vector2(344, 38);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font != null ? font : TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            text.color = Color.white;
            return text;
        }

        private static RectTransform Node(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.gameObject.layer = parent.gameObject.layer;
            return rect;
        }

        private static Image Fill(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        #endregion
    }
}
