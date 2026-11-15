using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    public sealed class DamageFeedbackView : MonoBehaviour
    {
        #region Serialized Fields

        [Header("References")]
        [SerializeField]
        private TMP_Text healthText;

        [SerializeField]
        private TMP_Text receivedDamageText;

        [SerializeField]
        private Image impactFlash;

        [Header("Anticipation")]
        [Range(0f, 1f)]
        [SerializeField]
        private float anticipationAlpha = 0.15f;

        [Header("Damage Popup")]
        [SerializeField]
        private Vector2 popupTravelOffset = new Vector2(80f, 0f);

        [Min(0f)]
        [SerializeField]
        private float popupStartScale = 1f;

        [Min(0f)]
        [SerializeField]
        private float popupEndScale = 1.6f;

        [Min(0f)]
        [SerializeField]
        private float popupDuration = 1f;

        [Header("Health Fade")]
        [Min(0f)]
        [SerializeField]
        private float healthFadeDuration = 0.5f;

        [Header("Flash")]
        [SerializeField]
        private Color flashColor = new Color(1f, 0.15f, 0.35f, 1f);

        [Range(0f, 1f)]
        [SerializeField]
        private float flashStrength = 0.75f;

        [Min(0f)]
        [SerializeField]
        private float flashDuration = 0.25f;

        #endregion

        #region Runtime State

        private Color healthBaseColor;
        private Color damageBaseColor;
        private Color flashBaseColor;
        private Vector2 popupHomePosition;
        private Vector3 popupBaseScale;
        private bool initialized;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Initialize();
            ResetImmediately();
        }

        private void OnDisable()
        {
            ResetImmediately();
        }

        #endregion

        #region Feedback API

        public void BeginAnticipation()
        {
            Initialize();
            SetHealthAlpha(anticipationAlpha);
        }

        public void PresentHealth(int health)
        {
            Initialize();
            if (healthText != null) healthText.text = Mathf.Max(0, health).ToString();
        }

        public IEnumerator PlayDamage(int damage, int resultingHealth)
        {
            Initialize();

            damage = Mathf.Max(0, damage);
            resultingHealth = Mathf.Max(0, resultingHealth);

            SetHealthAtImpact(resultingHealth);

            bool showDamagePopup = damage > 0 && receivedDamageText != null;

            if (showDamagePopup)
            {
                PrepareDamagePopup(damage);

                Debug.Log(
                    $"Damage feedback '{name}': "
                        + $"Damage={damage}, HealthAfter={resultingHealth}, "
                        + $"Popup={receivedDamageText.name}."
                );

                // Bảo đảm standalone build render popup ít nhất một frame.
                yield return null;
            }

            float totalDuration = Mathf.Max(
                popupDuration,
                Mathf.Max(healthFadeDuration, flashDuration)
            );

            if (totalDuration <= 0f)
            {
                ResetPopupAndFlash();
                RestoreHealthColor();
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < totalDuration)
            {
                const float maximumAnimationDelta = 1f / 30f;
                float deltaTime = Mathf.Min(
                    Time.unscaledDeltaTime,
                    maximumAnimationDelta
                );

                elapsed += deltaTime;

                if (showDamagePopup)
                {
                    UpdateDamagePopup(NormalizedProgress(elapsed, popupDuration));
                }

                UpdateHealthFade(NormalizedProgress(elapsed, healthFadeDuration));

                if (damage > 0)
                {
                    UpdateFlash(NormalizedProgress(elapsed, flashDuration));
                }

                yield return null;
            }

            ResetPopupAndFlash();
            RestoreHealthColor();
        }

        public void ResetImmediately()
        {
            if (!initialized)
            {
                return;
            }

            RestoreHealthColor();
            ResetPopupAndFlash();
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            healthBaseColor = healthText != null ? healthText.color : Color.white;

            damageBaseColor =
                receivedDamageText != null ? receivedDamageText.color : Color.white;

            // The popup is hidden by its GameObject, so its base color must
            // remain visible even when the scene stores the TMP alpha as zero.
            damageBaseColor.a = 1f;

            flashBaseColor = impactFlash != null ? impactFlash.color : Color.clear;

            if (receivedDamageText != null)
            {
                popupHomePosition = receivedDamageText.rectTransform.anchoredPosition;

                popupBaseScale = receivedDamageText.rectTransform.localScale;
            }

            initialized = true;
        }

        #endregion

        #region Impact Setup

        private void SetHealthAtImpact(int resultingHealth)
        {
            if (healthText == null)
            {
                return;
            }

            healthText.text = resultingHealth.ToString();
            SetHealthAlpha(0f);
        }

        private void PrepareDamagePopup(int damage)
        {
            receivedDamageText.text = $"-{damage}";
            receivedDamageText.gameObject.SetActive(true);

            receivedDamageText.rectTransform.anchoredPosition = popupHomePosition;

            receivedDamageText.rectTransform.localScale =
                popupBaseScale * popupStartScale;

            receivedDamageText.color = damageBaseColor;
        }

        #endregion

        #region Animation Updates

        private void UpdateDamagePopup(float progress)
        {
            float movementProgress = 1f - Mathf.Pow(1f - progress, 3f);

            receivedDamageText.rectTransform.anchoredPosition = Vector2.LerpUnclamped(
                popupHomePosition,
                popupHomePosition + popupTravelOffset,
                movementProgress
            );

            receivedDamageText.rectTransform.localScale = Vector3.LerpUnclamped(
                popupBaseScale * popupStartScale,
                popupBaseScale * popupEndScale,
                movementProgress
            );

            Color color = damageBaseColor;
            color.a *= 1f - progress;
            receivedDamageText.color = color;
        }

        private void UpdateHealthFade(float progress)
        {
            float easedProgress = progress * progress * (3f - 2f * progress);

            SetHealthAlpha(easedProgress);
        }

        private void UpdateFlash(float progress)
        {
            if (impactFlash == null)
            {
                return;
            }

            float pulse = Mathf.Sin(progress * Mathf.PI) * flashStrength;

            impactFlash.color = Color.Lerp(flashBaseColor, flashColor, pulse);
        }

        #endregion

        #region Reset

        private void RestoreHealthColor()
        {
            if (healthText != null)
            {
                healthText.color = healthBaseColor;
            }
        }

        private void SetHealthAlpha(float normalizedAlpha)
        {
            if (healthText == null)
            {
                return;
            }

            Color color = healthBaseColor;
            color.a *= Mathf.Clamp01(normalizedAlpha);
            healthText.color = color;
        }

        private void ResetPopupAndFlash()
        {
            if (receivedDamageText != null)
            {
                receivedDamageText.rectTransform.anchoredPosition = popupHomePosition;

                receivedDamageText.rectTransform.localScale = popupBaseScale;

                receivedDamageText.color = damageBaseColor;
                receivedDamageText.gameObject.SetActive(false);
            }

            if (impactFlash != null)
            {
                impactFlash.color = flashBaseColor;
            }
        }

        #endregion

        #region Helpers

        private static float NormalizedProgress(float elapsed, float duration)
        {
            if (duration <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01(elapsed / duration);
        }

        #endregion
    }
}
