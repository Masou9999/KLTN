using UnityEngine;
using UnityEngine.UI;

namespace KLTN.UI.Credits
{
    /// <summary>Covers the viewport with a gently drifting image without exposing edges.</summary>
    [RequireComponent(typeof(Image))]
    public sealed class CreditsBackgroundMotion : MonoBehaviour
    {
        #region Configuration

        [SerializeField] private bool motionEnabled = true;
        [SerializeField, Min(0f)] private float amplitude = 10f;
        [SerializeField, Min(1f)] private float period = 26f;
        [SerializeField, Range(1f, 1.2f)] private float overscan = 1.04f;
        private RectTransform imageRect;
        private RectTransform viewport;
        private float aspect;
        private Vector2 lastSize;
        private float elapsed;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            imageRect = (RectTransform)transform;
            viewport = (RectTransform)transform.parent;
            Sprite sprite = GetComponent<Image>().sprite;
            aspect = sprite.rect.width / sprite.rect.height;
        }

        private void LateUpdate()
        {
            Vector2 size = viewport.rect.size;
            if (size != lastSize || imageRect.sizeDelta == Vector2.zero)
            {
                lastSize = size;
                float height = Mathf.Max(size.y, size.x / aspect);
                imageRect.sizeDelta = new Vector2(height * aspect, height) * overscan
                    + Vector2.one * (amplitude * 2f);
            }
            elapsed += Time.unscaledDeltaTime;
            float phase = elapsed * Mathf.PI * 2f / Mathf.Max(1f, period);
            imageRect.anchoredPosition = motionEnabled
                ? new Vector2(Mathf.Sin(phase), Mathf.Sin(phase * .7f)) * amplitude
                : Vector2.zero;
        }

        #endregion
    }
}
