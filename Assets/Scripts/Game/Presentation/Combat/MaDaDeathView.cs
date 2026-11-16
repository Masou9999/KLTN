using System.Collections;
using KLTN.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Replaces the retained card with the authored atlas at the jump apex.
    /// Each death owns its audio so simultaneous splashes and cancellation are isolated.
    /// </summary>
    public sealed class MaDaDeathView : MonoBehaviour
    {
        #region Runtime State

        private RectTransform target;
        private Vector3 homePosition;
        private Vector3 homeScale;
        private Quaternion homeRotation;
        private bool homeActive;
        private CanvasGroup cardGroup;
        private bool homeInteractable;
        private bool homeBlocksRaycasts;
        private RawImage splash;
        private AudioSource audioSource;
        private float gain;
        private bool completed;
        private int generation;
        public string Phase { get; private set; }
        public int Frame { get; private set; } = -1;

        #endregion

        #region Unity Lifecycle

        private void OnEnable() => AudioVolumeSettings.Changed += RefreshVolume;

        private void Update()
        {
            if (completed && (audioSource == null || !audioSource.isPlaying))
            {
                target = null;
                cardGroup = null;
                Destroy(gameObject);
            }
        }

        private void OnDisable()
        {
            AudioVolumeSettings.Changed -= RefreshVolume;
            ResetImmediately();
        }

        #endregion

        #region Public API

        public IEnumerator Play(MaDaDeathProfile profile, RectTransform layer,
            RectTransform visual, AudioSource sharedSfx)
        {
            if (visual == null || layer == null || profile == null || !profile.IsUsable)
            {
                Destroy(gameObject);
                yield break;
            }

            target = visual;
            int playback = ++generation;
            completed = false;
            homePosition = target.localPosition;
            homeScale = target.localScale;
            homeRotation = target.localRotation;
            homeActive = target.gameObject.activeSelf;
            cardGroup = target.GetComponentInParent<CanvasGroup>();
            if (cardGroup != null)
            {
                homeInteractable = cardGroup.interactable;
                homeBlocksRaycasts = cardGroup.blocksRaycasts;
                cardGroup.interactable = false;
                cardGroup.blocksRaycasts = false;
            }
            float height = target.rect.height * Mathf.Abs(homeScale.y);
            Phase = "Jump";
            float elapsed = 0f;
            while (elapsed < profile.jumpDuration && target != null)
            {
                float t = Mathf.Clamp01(elapsed / profile.jumpDuration);
                float eased = 1f - (1f - t) * (1f - t);
                PoseJump(profile, height, eased);
                yield return null;
                if (playback != generation) yield break;
                elapsed += Time.unscaledDeltaTime;
            }
            if (target == null)
            {
                Destroy(gameObject);
                yield break;
            }
            PoseJump(profile, height, 1f);

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            float renderedHeight = Vector3.Distance(
                layer.InverseTransformPoint(corners[0]),
                layer.InverseTransformPoint(corners[1])
            );
            float size = renderedHeight / Mathf.Max(0.1f, profile.firstCardHeight);
            Vector2 center = layer.InverseTransformPoint(
                target.TransformPoint(target.rect.center)
            );
            var go = new GameObject("MaDaWaterSplash", typeof(RectTransform),
                typeof(RawImage));
            go.layer = layer.gameObject.layer;
            go.transform.SetParent(layer, false);
            splash = go.GetComponent<RawImage>();
            splash.texture = profile.sheet;
            splash.raycastTarget = false;
            splash.maskable = false;
            splash.rectTransform.sizeDelta = Vector2.one * size;
            splash.rectTransform.anchoredPosition = center
                - (profile.firstCardCenter - Vector2.one * 0.5f) * size;

            // Both changes occur before rendering: never show two copies of the card.
            SetFrame(0);
            target.gameObject.SetActive(false);
            Phase = "Splash";
            for (int i = 0; i < 12; i++)
            {
                SetFrame(i);
                if (i == profile.sfxFrame)
                {
                    PlaySound(profile, sharedSfx);
                }
                elapsed = 0f;
                float duration = Mathf.Max(1f, profile.frameMilliseconds[i]) / 1000f;
                while (elapsed < duration)
                {
                    yield return null;
                    if (playback != generation) yield break;
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            Phase = "Fade";
            elapsed = 0f;
            while (elapsed < profile.fadeDuration)
            {
                splash.color = new Color(1f, 1f, 1f,
                    1f - Mathf.Clamp01(elapsed / profile.fadeDuration));
                yield return null;
                if (playback != generation) yield break;
                elapsed += Time.unscaledDeltaTime;
            }
            Destroy(splash.gameObject);
            splash = null;
            RestoreTransform();
            // The old card stays hidden until snapshot application destroys its view.
            // Natural audio expiry detaches it; explicit reset may still restore it.
            Phase = "Complete";
            completed = true;
        }

        public void ResetImmediately()
        {
            generation++;
            completed = false;
            StopAllCoroutines();
            if (audioSource != null) audioSource.Stop();
            if (splash != null)
            {
                splash.gameObject.SetActive(false);
                Destroy(splash.gameObject);
                splash = null;
            }
            RestoreTransform();
            if (target != null) target.gameObject.SetActive(homeActive);
            if (cardGroup != null)
            {
                cardGroup.interactable = homeInteractable;
                cardGroup.blocksRaycasts = homeBlocksRaycasts;
            }
            cardGroup = null;
            target = null;
            Phase = "Reset";
        }

        #endregion

        #region Helpers

        private void PoseJump(MaDaDeathProfile profile, float height, float t)
        {
            target.localPosition = homePosition
                + Vector3.up * (height * profile.jumpHeightRelativeToCard * t);
            target.localScale = homeScale * Mathf.Lerp(1f, profile.jumpEndScale, t);
            target.localRotation = homeRotation
                * Quaternion.Euler(0f, 0f, profile.jumpEndRotation * t);
        }

        private void RestoreTransform()
        {
            if (target == null) return;
            target.localPosition = homePosition;
            target.localScale = homeScale;
            target.localRotation = homeRotation;
        }

        private void SetFrame(int index)
        {
            Frame = index;
            // Half-pixel inset avoids bleeding from neighbouring cells with filtering.
            float w = splash.texture.width / 4f;
            float h = splash.texture.height / 3f;
            splash.uvRect = new Rect(
                (index % 4 * w + 0.5f) / splash.texture.width,
                ((2 - index / 4) * h + 0.5f) / splash.texture.height,
                (w - 1f) / splash.texture.width,
                (h - 1f) / splash.texture.height
            );
        }

        private void PlaySound(MaDaDeathProfile profile, AudioSource shared)
        {
            if (profile.splashSfx == null) return;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.outputAudioMixerGroup = shared != null
                ? shared.outputAudioMixerGroup : null;
            gain = profile.sfxVolume;
            RefreshVolume();
            audioSource.clip = profile.splashSfx;
            audioSource.time = Mathf.Clamp(profile.sfxStartTime, 0f,
                Mathf.Max(0f, profile.splashSfx.length - 0.01f));
            audioSource.Play();
        }

        private void RefreshVolume()
        {
            if (audioSource != null)
                audioSource.volume = AudioVolumeSettings.SfxVolume * gain;
        }

        #endregion
    }
}
