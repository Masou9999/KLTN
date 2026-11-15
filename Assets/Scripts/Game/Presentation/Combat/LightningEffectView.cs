using System.Collections;
using KLTN.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>Presentation only; the director still owns event order and death playback.</summary>
    public sealed class LightningEffectView : MonoBehaviour
    {
        private AudioSource audioSource;
        private GameObject graphicsRoot;
        private RectTransform pulsingSource;
        private Vector3 originalScale;
        private int variation;
        private float sfxGain = 1f;
        private System.Action refreshFrame;

        private void OnEnable() => AudioVolumeSettings.Changed += RefreshVolume;

        private void LateUpdate() => RefreshLayout();

        // Also used by camera previews after a canvas layout/viewport change.
        public void RefreshLayout() => refreshFrame?.Invoke();

        private void OnDisable()
        {
            AudioVolumeSettings.Changed -= RefreshVolume;
            ClearVisuals();
            if (audioSource != null) audioSource.Stop();
        }

        private void RefreshVolume()
        {
            if (audioSource != null) audioSource.volume = AudioVolumeSettings.SfxVolume * sfxGain;
        }

        public void ClearVisuals()
        {
            refreshFrame = null;
            if (pulsingSource != null) pulsingSource.localScale = originalScale;
            pulsingSource = null;
            if (graphicsRoot != null)
            {
                graphicsRoot.SetActive(false);
                Destroy(graphicsRoot);
                graphicsRoot = null;
            }
        }

        public IEnumerator Play(LightningEffectProfile profile, RectTransform layer,
            RectTransform source, RectTransform target, Sprite glow, AudioSource sharedSfx)
        {
            ClearVisuals();
            graphicsRoot = new GameObject("LightningGraphics", typeof(RectTransform));
            RectTransform root = (RectTransform)graphicsRoot.transform;
            root.SetParent(layer, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = Vector2.zero;
            Image origin = CreateGlow(root, "Charge", glow, 100f);
            Image impact = CreateGlow(root, "Impact", glow, 140f);
            impact.enabled = false;
            LightningBoltGraphic bolt = CreateBolt(root, "MainBolt");
            var sparks = new LightningBoltGraphic[5];
            for (int i = 0; i < sparks.Length; i++)
                sparks[i] = CreateBolt(root, "ImpactArc");

            pulsingSource = source;
            if (source != null) originalScale = source.localScale;
            int firstFrame = variation++ % (profile.endpointHeights.Length - 1);
            float charge = Mathf.Max(0.01f, profile.chargeDuration);
            float travel = Mathf.Max(0.01f, profile.travelDuration);
            float hold = Mathf.Max(0.01f, profile.holdDuration);
            float fade = Mathf.Max(0.01f, profile.fadeDuration);
            float hitTime = charge + travel;
            float total = hitTime + hold + fade;
            float elapsed = 0f;
            bool hit = false;

            try
            {
                void RefreshFrame()
                {
                    if (target == null || root == null) return;
                    Vector2 end = root.InverseTransformPoint(target.TransformPoint(target.rect.center));
                    Vector2 start = source != null
                        ? (Vector2)root.InverseTransformPoint(source.TransformPoint(source.rect.center))
                        : end + Vector2.down * 100f;
                    float chargeT = Mathf.Clamp01(elapsed / charge);
                    if (source != null)
                        source.localScale = originalScale * (1f + 0.07f * Mathf.Sin(chargeT * Mathf.PI));
                    origin.rectTransform.anchoredPosition = start;
                    origin.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.35f, 1f, chargeT);
                    origin.color = Color.Lerp(profile.glowColor, Color.white, chargeT * 0.65f);
                    if (elapsed > charge)
                        origin.color = new Color(1f, 0.08f, 0.1f, Mathf.Clamp01(1f - (elapsed - charge) / 0.16f));

                    float afterHit = elapsed - hitTime;
                    float fadeT = Mathf.Clamp01((afterHit - hold) / fade);
                    int frame = fadeT > 0.15f ? profile.endpointHeights.Length - 1
                        : (firstFrame + Mathf.FloorToInt(Mathf.Max(0f, elapsed - charge)
                            * profile.framesPerSecond)) % (profile.endpointHeights.Length - 1);
                    float width = Mathf.Clamp(Vector2.Distance(start, end) * 0.24f,
                        profile.minimumWidth, profile.maximumWidth);
                    bolt.SetFrame(profile, start, end, width * Mathf.Lerp(1f, 0.45f, fadeT),
                        frame, Mathf.Clamp01((elapsed - charge) / travel), 1f - fadeT);

                    if (afterHit >= 0f)
                    {
                        if (!hit)
                        {
                            PlayStrike(profile, sharedSfx);
                            hit = true;
                        }
                        impact.enabled = true;
                        impact.rectTransform.anchoredPosition = end;
                        impact.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.5f,
                            Mathf.Clamp01(afterHit / (hold + fade)));
                        Color tint = Color.Lerp(Color.white, profile.glowColor,
                            Mathf.Clamp01(afterHit / 0.09f));
                        tint.a = Mathf.Clamp01(1f - afterHit / (hold + fade));
                        impact.color = tint;
                        for (int i = 0; i < sparks.Length; i++)
                        {
                            float angle = (i * 72f + firstFrame * 31f) * Mathf.Deg2Rad;
                            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                            float reach = Mathf.Lerp(32f, 85f, Mathf.Clamp01(afterHit / (hold + fade)));
                            sparks[i].SetFrame(profile, end + direction * 8f,
                                end + direction * reach, 22f * (1f - fadeT * 0.5f),
                                (frame + i) % profile.endpointHeights.Length, 1f, (1f - fadeT) * 0.85f);
                        }
                    }
                }
                refreshFrame = RefreshFrame;
                while (elapsed < total && target != null)
                {
                    RefreshFrame();
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }
                // A long frame can skip the hit window; still emit exactly one strike.
                if (!hit && target != null) PlayStrike(profile, sharedSfx);
            }
            finally
            {
                ClearVisuals();
            }
        }

        private void PlayStrike(LightningEffectProfile profile, AudioSource sharedSfx)
        {
            if (profile.strikeSfx == null) return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            audioSource.outputAudioMixerGroup = sharedSfx != null ? sharedSfx.outputAudioMixerGroup : null;
            sfxGain = profile.sfxVolume;
            RefreshVolume();
            audioSource.clip = profile.strikeSfx;
            audioSource.time = Mathf.Clamp(profile.sfxStartTime, 0f,
                Mathf.Max(0f, profile.strikeSfx.length - 0.01f));
            audioSource.Play();
        }

        private static Image CreateGlow(Transform parent, string name, Sprite sprite, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image result = go.GetComponent<Image>();
            result.sprite = sprite;
            result.raycastTarget = false;
            result.maskable = false;
            result.rectTransform.sizeDelta = Vector2.one * size;
            return result;
        }

        private static LightningBoltGraphic CreateBolt(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LightningBoltGraphic));
            go.transform.SetParent(parent, false);
            return go.GetComponent<LightningBoltGraphic>();
        }
    }
}
