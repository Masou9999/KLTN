using UnityEngine;
using UnityEngine.EventSystems;

namespace KLTN.Game.Presentation
{
    public sealed class BoardSlotDropZone : MonoBehaviour, IDropHandler
    {
        [SerializeField]
        private int slotIndex;

        [SerializeField]
        private RectTransform contentRoot;

        [SerializeField]
        private MatchDebugControls controls;

        [Tooltip("Optional. When empty, a comet is created as the first child at runtime.")]
        [SerializeField]
        private BoardSlotCometGraphic dropHint;

        public int SlotIndex => slotIndex;

        private void Awake()
        {
            EnsureDropHint();
        }

        private void OnEnable()
        {
            if (controls != null)
            {
                controls.RegisterBoardSlot(this);
            }
        }

        private void OnDisable()
        {
            if (controls != null)
            {
                controls.UnregisterBoardSlot(this);
            }

            if (dropHint != null)
            {
                dropHint.ResetHidden();
            }
        }

        public void SetDropHint(bool visible)
        {
            if (dropHint == null)
            {
                return;
            }

            if (visible)
            {
                dropHint.Play();
            }
            else
            {
                dropHint.Stop();
            }
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (eventData.pointerDrag == null || contentRoot == null || controls == null)
            {
                return;
            }

            DraggableHandCard card =
                eventData.pointerDrag.GetComponent<DraggableHandCard>();

            if (card == null)
            {
                return;
            }

            controls.TryStageBoardCard(card, slotIndex, contentRoot);
        }

        private void EnsureDropHint()
        {
            if (dropHint == null)
            {
                dropHint = GetComponentInChildren<BoardSlotCometGraphic>(true);
            }

            if (dropHint == null)
            {
                dropHint = CreateDropHint(transform);
            }

            dropHint.ResetHidden();
        }

        /// <summary>
        /// Builds the comet child stretched over the slot, behind the slot content.
        /// Shared by the runtime fallback and the editor installer.
        /// </summary>
        public static BoardSlotCometGraphic CreateDropHint(Transform slot)
        {
            var hintObject = new GameObject(
                "DropHintComet",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(BoardSlotCometGraphic)
            );
            hintObject.layer = slot.gameObject.layer;

            var hintRect = hintObject.GetComponent<RectTransform>();
            hintRect.SetParent(slot, false);
            hintRect.SetAsFirstSibling();
            hintRect.anchorMin = Vector2.zero;
            hintRect.anchorMax = Vector2.one;
            hintRect.pivot = new Vector2(0.5f, 0.5f);
            hintRect.offsetMin = Vector2.zero;
            hintRect.offsetMax = Vector2.zero;

            return hintObject.GetComponent<BoardSlotCometGraphic>();
        }
    }
}
