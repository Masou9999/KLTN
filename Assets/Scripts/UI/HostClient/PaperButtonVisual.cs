using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.UI
{
    /// <summary>Swaps independent art layers without moving the button or its label.</summary>
    public sealed class PaperButtonVisual : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        #region Serialized Fields

        [SerializeField] private Button button;
        [SerializeField] private Image normalArt;
        [SerializeField] private Image hoverArt;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Color normalText = new Color(.16f, .09f, .05f);
        [SerializeField] private Color hoverText = new Color(1f, .91f, .72f);

        #endregion

        #region Runtime State

        private bool hovered;
        private bool selected;
        private bool lastHighlight;
        private bool lastInteractable;

        #endregion

        #region Unity Lifecycle

        private void OnEnable() { Apply(); }
        private void OnDisable() { hovered = selected = false; }

        private void LateUpdate()
        {
            if (lastHighlight != Highlighted || lastInteractable != button.interactable)
                Apply();
        }

        #endregion

        #region Pointer And Selection

        public void OnPointerEnter(PointerEventData data) { hovered = true; Apply(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Apply(); }
        public void OnSelect(BaseEventData data) { selected = true; Apply(); }
        public void OnDeselect(BaseEventData data) { selected = false; Apply(); }

        #endregion

        #region Rendering

        private bool Highlighted => button.interactable && (hovered || selected);

        private void Apply()
        {
            lastHighlight = Highlighted;
            lastInteractable = button.interactable;
            normalArt.enabled = !lastHighlight;
            hoverArt.enabled = lastHighlight;
            label.color = lastHighlight ? hoverText : normalText;
        }

        #endregion
    }
}
