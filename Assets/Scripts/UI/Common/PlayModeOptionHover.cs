using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>
    /// Enlarges a play-mode card and strengthens its border while hovered or selected.
    /// </summary>
    /// <remarks>
    /// Only the visual child is scaled, so the button's raycast area stays fixed and
    /// the pointer cannot flicker between enter and exit at the enlarged edge.
    /// </remarks>
    public sealed class PlayModeOptionHover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        #region Serialized Fields

        [SerializeField] private RectTransform visual;
        [SerializeField] private Graphic border;
        [SerializeField] private Color normalBorder = new Color(0.89f, 0.47f, 0.43f);
        [SerializeField] private Color highlightBorder = new Color(1f, 0.62f, 0.55f);
        [SerializeField] private Graphic glow;
        [SerializeField] private float glowAlpha = 0.55f;
        [SerializeField] private Graphic label;
        [SerializeField] private Color normalLabel = new Color(0.23f, 0.13f, 0.19f);
        [SerializeField] private Color highlightLabel = new Color(0.65f, 0.17f, 0.2f);
        [SerializeField] private float highlightScale = 1.1f;
        [SerializeField] private float duration = 0.15f;

        #endregion

        #region Runtime State

        private bool hovered;
        private bool selected;
        private Sequence tween;

        #endregion

        #region Unity Lifecycle

        private void OnDisable()
        {
            // The modal is hidden while a card may still be hovered; reopen it idle.
            hovered = false;
            selected = false;
            tween?.Kill();
            tween = null;
            visual.localScale = Vector3.one;
            border.color = normalBorder;
            glow.color = WithAlpha(glow.color, 0f);
            label.color = normalLabel;
        }

        #endregion

        #region Event Handlers

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            // Move selection with the pointer so only one card is highlighted at once.
            EventSystem.current?.SetSelectedGameObject(gameObject);
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            Refresh();
        }

        public void OnSelect(BaseEventData eventData)
        {
            // Only keyboard/gamepad navigation highlights; the selection made by
            // PlayModeModal.Open must not enlarge a card before the player acts.
            selected = eventData is AxisEventData;
            Refresh();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            selected = false;
            Refresh();
        }

        #endregion

        #region Helpers

        private void Refresh()
        {
            bool active = hovered || selected;
            tween?.Kill();
            tween = DOTween.Sequence()
                .Join(visual.DOScale(active ? highlightScale : 1f, duration))
                .Join(border.DOColor(active ? highlightBorder : normalBorder, duration))
                .Join(glow.DOFade(active ? glowAlpha : 0f, duration))
                .Join(label.DOColor(active ? highlightLabel : normalLabel, duration))
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        #endregion
    }
}
