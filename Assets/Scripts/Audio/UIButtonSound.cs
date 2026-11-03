using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KLTN.Audio
{
    /// <summary>Plays feedback only for active, interactable UI buttons.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class UIButtonSound : MonoBehaviour,
        IPointerEnterHandler
    {
        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (button.IsActive() && button.IsInteractable())
            {
                UIClickManager.Instance?.PlayHover();
            }
        }

        public void PlayClick()
        {
            if (button.IsActive() && button.IsInteractable())
            {
                UIClickManager.Instance?.PlayClick();
            }
        }
    }
}
