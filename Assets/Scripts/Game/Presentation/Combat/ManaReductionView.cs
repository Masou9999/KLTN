using System;
using System.Collections;
using KLTN.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>Plays a stationary mana-badge effect and optional local card pulse.</summary>
    public sealed class ManaReductionView : MonoBehaviour
    {
        #region Runtime State

        private GameObject graphics;
        private NetworkCardVisual target;
        private Vector3 originalScale;
        private int originalEnergy;
        private AudioSource audioSource;
        private float gain;
        private Action refreshFrame;
        public int CurrentFrame { get; private set; }

        #endregion

        #region Lifecycle And Cleanup

        private void OnEnable() => AudioVolumeSettings.Changed += RefreshVolume;
        private void LateUpdate() => refreshFrame?.Invoke();
        private void OnDisable()
        {
            AudioVolumeSettings.Changed -= RefreshVolume;
            ClearVisuals(true);
        }

        private void RefreshVolume()
        {
            if (audioSource != null)
                audioSource.volume = AudioVolumeSettings.SfxVolume * gain;
        }

        public void ClearVisuals(bool stopAudio = false)
        {
            refreshFrame = null;
            if (target != null)
            {
                target.CardRect.localScale = originalScale;
                target.SetDisplayedEnergy(originalEnergy);
            }
            target = null;
            if (graphics != null)
            {
                graphics.SetActive(false);
                Destroy(graphics);
                graphics = null;
            }
            if (stopAudio && audioSource != null) audioSource.Stop();
        }

        #endregion

        #region Playback

        public IEnumerator Play(ManaReductionProfile profile, RectTransform layer,
            NetworkCardVisual card, int before, int after, bool pulse, AudioSource sharedSfx)
        {
            ClearVisuals();
            if (card == null || profile == null || !profile.IsUsable) yield break;
            target = card;
            originalScale = card.CardRect.localScale;
            originalEnergy = before;
            card.SetDisplayedEnergy(before);
            graphics = new GameObject("ManaReductionSwirl", typeof(RectTransform), typeof(RawImage));
            var image = graphics.GetComponent<RawImage>();
            image.transform.SetParent(layer, false);
            image.texture = profile.sheet;
            image.raycastTarget = false;
            image.maskable = false;
            float elapsed = 0f;
            PlaySound(profile, sharedSfx);
            try
            {
                void Refresh()
                {
                    if (target == null || graphics == null) return;
                    float t = Mathf.Clamp01(elapsed / profile.pulseSeconds);
                    float pulseAmount = t < .5f
                        ? Mathf.SmoothStep(0f, 1f, t * 2f)
                        : Mathf.SmoothStep(1f, 0f, (t - .5f) * 2f);
                    target.CardRect.localScale = originalScale
                        * (pulse ? Mathf.Lerp(1f, profile.pulseScale, pulseAmount) : 1f);
                    CurrentFrame = Mathf.Clamp((int)(elapsed / profile.frameSeconds), 0, 23);
                    image.uvRect = new Rect(CurrentFrame % 6 / 6f,
                        (3 - CurrentFrame / 6) / 4f, 1f / 6f, 1f / 4f);
                    var anchor = card.ManaAnchor;
                    image.rectTransform.position = anchor.TransformPoint(anchor.rect.center);
                    Vector3 left = layer.InverseTransformPoint(card.CardRect.TransformPoint(
                        new Vector3(card.CardRect.rect.xMin, 0f)));
                    Vector3 right = layer.InverseTransformPoint(card.CardRect.TransformPoint(
                        new Vector3(card.CardRect.rect.xMax, 0f)));
                    image.rectTransform.sizeDelta = Vector2.one
                        * Vector3.Distance(left, right) * profile.widthRelativeToCard;
                    card.SetDisplayedEnergy(CurrentFrame >= profile.costChangeFrame ? after : before);
                }
                refreshFrame = Refresh;
                while (elapsed < profile.frameSeconds * 24 && target != null)
                {
                    Refresh();
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }
                originalEnergy = after;
            }
            finally { ClearVisuals(); }
        }

        private void PlaySound(ManaReductionProfile profile, AudioSource sharedSfx)
        {
            if (profile.sound == null) return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            audioSource.outputAudioMixerGroup = sharedSfx != null
                ? sharedSfx.outputAudioMixerGroup : null;
            gain = profile.soundGain;
            RefreshVolume();
            if (profile.soundStartSeconds <= 0f)
                audioSource.PlayOneShot(profile.sound);
            else
            {
                audioSource.clip = profile.sound;
                audioSource.time = Mathf.Clamp(profile.soundStartSeconds, 0f,
                    Mathf.Max(0f, profile.sound.length - .01f));
                audioSource.Play();
            }
        }

        #endregion
    }
}
