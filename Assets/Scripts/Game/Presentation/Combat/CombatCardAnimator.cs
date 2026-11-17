using System.Collections;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class CombatCardAnimator : MonoBehaviour
    {
        
        #region Component References

        private RectTransform rectTransform;
        private CardShadowVFX cardShadowVFX;

        #endregion


        #region Runtime State

        private Transform homeParent;
        private int homeSiblingIndex;
        private Vector3 homeWorldPosition;
        private Vector3 retreatWorldPosition;
        private bool prepared;
        private bool isSelfCard;

        #endregion

        #region Properties

        public bool IsPrepared => prepared;
        public bool IsSelfCard => isSelfCard;
        public Vector3 CurrentWorldPosition => rectTransform.position;
        public Vector3 HomeWorldPosition => homeWorldPosition;

        public float WorldHeight =>
            rectTransform.rect.height * Mathf.Abs(rectTransform.lossyScale.y);

        #endregion

        #region Unity Lifecycle

        
private void Awake()
{
    rectTransform = GetComponent<RectTransform>();
    cardShadowVFX = GetComponent<CardShadowVFX>();
}


        #endregion

        #region Combat Preparation

        public void Prepare(
            RectTransform animationLayer,
            bool isSelfCard,
            float retreatDistance
        )
        {
            if (prepared || animationLayer == null)
            {
                return;
            }

            homeParent = rectTransform.parent;
            homeSiblingIndex = rectTransform.GetSiblingIndex();
            homeWorldPosition = rectTransform.position;
            this.isSelfCard = isSelfCard;

            Vector3 retreatDirection = isSelfCard ? Vector3.down : Vector3.up;

            retreatWorldPosition = homeWorldPosition + retreatDirection * retreatDistance;

            
rectTransform.SetParent(animationLayer, true);
rectTransform.SetAsLastSibling();
prepared = true;

// Bật bóng khi lá bài bắt đầu hoạt ảnh chiến đấu.
cardShadowVFX?.ShowFixedShadow();

        }

        #endregion

        #region Animation

        public IEnumerator MoveToRetreat(float duration)
        {
            yield return MoveTo(retreatWorldPosition, duration);
        }

        public IEnumerator Strike(Vector3 targetWorldPosition, float duration)
        {
            yield return MoveTo(targetWorldPosition, duration);
        }

        public IEnumerator ReturnHome(float duration)
        {
            yield return MoveTo(homeWorldPosition, duration);
            RestoreParent();
        }

        private IEnumerator MoveTo(Vector3 targetWorldPosition, float duration)
        {
            if (!prepared)
            {
                yield break;
            }

            if (duration <= 0f)
            {
                rectTransform.position = targetWorldPosition;
                yield break;
            }

            Vector3 startPosition = rectTransform.position;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                float progress = Mathf.Clamp01(elapsed / duration);
                float easedProgress = progress * progress * (3f - 2f * progress);

                rectTransform.position = Vector3.LerpUnclamped(
                    startPosition,
                    targetWorldPosition,
                    easedProgress
                );

                yield return null;
            }

            rectTransform.position = targetWorldPosition;
        }

        #endregion

        #region Restoration

        public void RestoreImmediately()
        {
            if (!prepared)
            {
                return;
            }

            rectTransform.position = homeWorldPosition;
            RestoreParent();
        }

        private void RestoreParent()
        {
            if (homeParent != null)
            {
                rectTransform.SetParent(homeParent, true);

                int siblingIndex = Mathf.Clamp(
                    homeSiblingIndex,
                    0,
                    homeParent.childCount - 1
                );

                rectTransform.SetSiblingIndex(siblingIndex);
                rectTransform.position = homeWorldPosition;
            }

            
prepared = false;
isSelfCard = false;

// Tắt bóng khi hoạt ảnh chiến đấu kết thúc.
cardShadowVFX?.HideShadow();

        }

        #endregion
    }
}
