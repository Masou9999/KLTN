using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>Reconstructs card artwork with a shared screen-space burn mask.</summary>
    public sealed class CardReviveFeedback : MonoBehaviour
    {
        private static readonly int RevealId = Shader.PropertyToID("_Reveal");
        private static readonly int CardCenterId = Shader.PropertyToID("_CardCenter");
        private static readonly int CardHalfSizeId = Shader.PropertyToID("_CardHalfSize");

        private Image[] images;
        private Material[] originalMaterials;
        private Material runtimeMaterial;
        private CanvasGroup group;
        private RectTransform root;
        private Vector3 originalScale;
        private float originalAlpha;
        private bool playing;
        private GameObject statPopup;

        public IEnumerator Play(
            NetworkCardVisual card,
            int damageBonus,
            int healthBonus,
            float duration,
            float restingAlpha
        )
        {
            if (card == null || card.CardRect == null)
            {
                yield break;
            }

            ResetImmediately();

            root = card.CardRect;
            group = card.GetComponent<CanvasGroup>();
            images = string.IsNullOrEmpty(card.DefinitionId)
                ? card.GetComponentsInChildren<Image>(true)
                : card.ArtworkMount == null
                ? new Image[0]
                : card.ArtworkMount.GetComponentsInChildren<Image>(true);
            originalMaterials = new Material[images.Length];
            originalScale = root.localScale;
            // The card is hidden during lightning, but after the reveal it must
            // return to the availability shade dictated by the latest snapshot.
            originalAlpha = Mathf.Clamp01(restingAlpha);

            Shader shader = Resources.Load<Shader>("Shaders/CardReviveDissolve");

            if (shader == null)
            {
                shader = Shader.Find("UI/KLTN/CardReviveDissolve");
            }

            if (shader != null)
            {
                runtimeMaterial = new Material(shader);
                runtimeMaterial.SetFloat(RevealId, 0f);
                ConfigureScreenMask(runtimeMaterial);

                for (int i = 0; i < images.Length; i++)
                {
                    originalMaterials[i] = images[i].material;
                    images[i].material = runtimeMaterial;
                }
            }

            playing = true;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                float eased = t * t * (3f - 2f * t);

                if (runtimeMaterial != null)
                {
                    runtimeMaterial.SetFloat(RevealId, eased);
                }

                if (group != null)
                {
                    // Never overshoot the resting alpha (e.g. a translucent companion).
                    group.alpha = eased * originalAlpha;
                }

                float scale = Mathf.Lerp(0.84f, 1.06f, eased);
                root.localScale = originalScale * scale;
                yield return null;
            }

            root.localScale = originalScale;
            RestoreMaterials();

            if (damageBonus != 0 || healthBonus != 0)
            {
                yield return PlayStatGain(damageBonus, healthBonus);
            }

            ResetImmediately();
        }

        public void ResetImmediately()
        {
            if (!playing)
            {
                return;
            }

            RestoreMaterials();

            if (root != null)
            {
                root.localScale = originalScale;
            }

            if (group != null)
            {
                group.alpha = originalAlpha;
            }

            playing = false;

            if (statPopup != null)
            {
                Destroy(statPopup);
                statPopup = null;
            }
        }

        private void OnDisable()
        {
            ResetImmediately();
        }

        private void RestoreMaterials()
        {
            if (images != null && originalMaterials != null)
            {
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i] != null)
                    {
                        images[i].material = originalMaterials[i];
                    }
                }
            }

            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
                runtimeMaterial = null;
            }
        }

        private void ConfigureScreenMask(Material material)
        {
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>()?.rootCanvas;
            Camera camera = canvas != null
                && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;

            var corners = new Vector3[4];
            root.GetWorldCorners(corners);

            Vector2 lower = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 upper = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            Vector2 center = (lower + upper) * 0.5f;
            Vector2 halfSize = new Vector2(
                Mathf.Abs(upper.x - lower.x) * 0.5f,
                Mathf.Abs(upper.y - lower.y) * 0.5f
            );

            material.SetVector(
                CardCenterId,
                new Vector4(center.x / Screen.width, center.y / Screen.height, 0f, 0f)
            );
            material.SetVector(
                CardHalfSizeId,
                new Vector4(
                    Mathf.Max(0.001f, halfSize.x / Screen.width),
                    Mathf.Max(0.001f, halfSize.y / Screen.height),
                    0f,
                    0f
                )
            );
        }

        private IEnumerator PlayStatGain(int damageBonus, int healthBonus)
        {
            var popupObject = new GameObject(
                "ReviveStatGain",
                typeof(RectTransform),
                typeof(TextMeshProUGUI)
            );
            statPopup = popupObject;

            var rect = (RectTransform)popupObject.transform;
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(190f, 48f);

            TextMeshProUGUI label = popupObject.GetComponent<TextMeshProUGUI>();
            label.text = $"+{damageBonus}/+{healthBonus}";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 32f;
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;

            float elapsed = 0f;
            const float duration = 0.42f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                rect.anchoredPosition = Vector2.up * Mathf.Lerp(0f, 44f, t);
                rect.localScale = Vector3.one * Mathf.Lerp(0.65f, 1.18f, t);
                label.color = new Color(0.45f, 1f, 0.52f, 1f - t * t);
                yield return null;
            }

            Destroy(popupObject);
            statPopup = null;
        }
    }
}
