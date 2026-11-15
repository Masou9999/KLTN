using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>Wheel and scrollbar only. Never allow dragging the list or overscrolling.</summary>
    public sealed class MatchLogScrollRect : ScrollRect
    {
        public override void OnInitializePotentialDrag(PointerEventData eventData) { }
        public override void OnBeginDrag(PointerEventData eventData) { }
        public override void OnDrag(PointerEventData eventData) { }
        public override void OnEndDrag(PointerEventData eventData) { }
        public override void OnScroll(PointerEventData data)
        {
            if (!vertical || content.rect.height <= viewport.rect.height + 0.5f) return;
            base.OnScroll(data);
            verticalNormalizedPosition = UnityEngine.Mathf.Clamp01(verticalNormalizedPosition);
            StopMovement();
        }
    }
}
