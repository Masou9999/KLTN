using KLTN.Game.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KLTN.UI
{
    /// <summary>Opens collection details without creating a network card or gameplay input.</summary>
    public sealed class OwnedCardItem : MonoBehaviour, IPointerClickHandler
    {
        private CardDetailData card;
        private CardDetailModal modal;

        public void Bind(CardDetailData data, CardDetailModal detailModal)
        {
            card = data;
            modal = detailModal;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right && modal != null)
                modal.Show(card);
        }
    }
}
