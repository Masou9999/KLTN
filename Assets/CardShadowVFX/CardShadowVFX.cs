
using UnityEngine;

namespace KLTN.Game.Presentation
{
    public class CardShadowVFX : MonoBehaviour
    {
        [Header("Shadow Reference")]
        [SerializeField]
        private RectTransform cardShadow;

        [Header("Shadow Offsets")]
        [SerializeField]
        private Vector2 fixedOffset = new Vector2(12f, -16f);

        [SerializeField]
        private Vector2 dynamicPeakOffset = new Vector2(35f, -45f);

        private bool shadowActive;

        private void Awake()
        {
            HideShadow();
        }

        public void ShowFixedShadow()
        {
            shadowActive = true;
            SetShadowOffset(fixedOffset);
        }

        public void UpdateDynamicShadow(float progress)
        {
            shadowActive = true;

            progress = Mathf.Clamp01(progress);

            float lift = Mathf.Sin(progress * Mathf.PI);

            Vector2 offset = Vector2.Lerp(
                fixedOffset,
                dynamicPeakOffset,
                lift
            );

            SetShadowOffset(offset);
        }

        public void HideShadow()
        {
            shadowActive = false;

            if (cardShadow != null)
            {
                cardShadow.gameObject.SetActive(false);
            }
        }

        private void SetShadowOffset(Vector2 offset)
        {
            if (cardShadow == null)
            {
                return;
            }

            if (!cardShadow.gameObject.activeSelf)
            {
                cardShadow.gameObject.SetActive(true);
            }

            cardShadow.offsetMin = new Vector2(
                offset.x,
                offset.y
            );

            cardShadow.offsetMax = new Vector2(
                offset.x,
                offset.y
            );
        }

        private void OnDisable()
        {
            HideShadow();
        }
    }
}
