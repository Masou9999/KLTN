using System.Collections;
using System.Collections.Generic;
using KLTN.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    public sealed class FeastEffectView : MonoBehaviour
    {
        private struct HiddenCard
        {
            public CanvasGroup group;
            public float alpha;
            public bool added;
        }
        private readonly List<HiddenCard> hidden = new List<HiddenCard>();
        private GameObject graphics;
        private RectTransform activeTarget, activeSource;
        private Vector3 targetScale;
        private Quaternion sourceRotation;
        private System.Action refresh;
        private AudioSource sound;
        private AudioSource chewSound;
        private Coroutine chewing;
        private float gain;
        public string Phase { get; private set; }
        private void OnEnable() => AudioVolumeSettings.Changed += RefreshVolume;
        private void LateUpdate() => refresh?.Invoke();
        public void RefreshLayout() => refresh?.Invoke();
        private void OnDisable()
        {
            AudioVolumeSettings.Changed -= RefreshVolume;
            if (chewing != null) StopCoroutine(chewing);
            chewing = null;
            if (chewSound != null) chewSound.Stop();
            ClearVisuals();
        }
        private void RefreshVolume()
        {
            if (sound != null) sound.volume = gain * AudioVolumeSettings.SfxVolume;
            if (chewSound != null) chewSound.volume = gain * AudioVolumeSettings.SfxVolume;
        }
        // Called after the authoritative snapshot, or immediately on reset/disable.
        public void ClearVisuals()
        {
            ClearMotion();
            foreach (var card in hidden)
            {
                if (card.group == null) continue;
                card.group.alpha = card.alpha;
                if (card.added) Destroy(card.group);
            }
            hidden.Clear();
        }
        private void ClearMotion()
        {
            refresh = null;
            if (sound != null) { sound.Stop(); sound.pitch = 1; }
            if (activeTarget != null) activeTarget.localScale = targetScale;
            if (activeSource != null) activeSource.localRotation = sourceRotation;
            activeSource = activeTarget = null;
            if (graphics != null) { graphics.SetActive(false); Destroy(graphics); }
            graphics = null;
            Phase = "Idle";
        }

        public IEnumerator Play(FeastEffectProfile p, RectTransform layer,
            RectTransform source, RectTransform target, AudioSource shared)
        {
            ClearMotion();
            if (p == null || !p.IsUsable || layer == null || source == null || target == null) yield break;
            activeTarget = target;
            activeSource = source;
            targetScale = target.localScale;
            sourceRotation = source.localRotation;
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            bool added = group == null;
            if (added) group = target.gameObject.AddComponent<CanvasGroup>();
            hidden.Add(new HiddenCard { group = group, alpha = group.alpha, added = added });
            graphics = new GameObject("FeastGraphics", typeof(RectTransform));
            var root = (RectTransform)graphics.transform;
            root.SetParent(layer, false);
            root.sizeDelta = Vector2.zero;
            // Sibling order is deliberate: smoke always covers the chicken.
            RawImage chicken = CreateImage(root, "FeastChicken", p.chicken);
            RawImage smoke = CreateImage(root, "FeastSmoke", p.smoke);
            Vector2 origin = layer.InverseTransformPoint(target.TransformPoint(target.rect.center));
            float width = Vector3.Distance(layer.InverseTransformPoint(target.TransformPoint(new Vector2(target.rect.xMin, 0))),
                layer.InverseTransformPoint(target.TransformPoint(new Vector2(target.rect.xMax, 0))));
            chicken.rectTransform.sizeDelta = Vector2.one * width * p.chickenWidth;
            // Atlas cells are landscape, not square: preserve the original aspect ratio.
            smoke.rectTransform.sizeDelta = new Vector2(width * p.smokeWidth,
                width * p.smokeWidth * (p.smoke.height / 4f) / (p.smoke.width / 6f));
            smoke.rectTransform.anchoredPosition = origin;
            float elapsed = 0;
            bool launched = false, finished = false;
            PlaySound(p.pop, p.popOffset, 1, p, shared);
            try
            {
                void Refresh()
                {
                    if (source == null || target == null || graphics == null) return;
                    float shrink = Mathf.Clamp01((elapsed - p.smokeDelay) / p.shrinkDuration);
                    target.localScale = targetScale * (1 - shrink * shrink);
                    if (shrink >= 1) group.alpha = 0;
                    float smokeAge = elapsed - p.smokeDelay;
                    smoke.enabled = smokeAge >= 0 && smokeAge < p.SmokeDuration;
                    int frame = p.SmokeFrame(smokeAge);
                    // Inset half a texel to keep neighbouring frames out of bilinear sampling.
                    float ux = .5f / p.smoke.width, uy = .5f / p.smoke.height;
                    smoke.uvRect = new Rect((frame % 6) / 6f + ux, (3 - frame / 6) / 4f + uy,
                        1f / 6f - 2 * ux, .25f - 2 * uy);
                    smoke.color = new Color(1, 1, 1, 1 - Mathf.Clamp01((smokeAge - .52f) / .09f));
                    float windup = Mathf.SmoothStep(0, 1, Mathf.Clamp01((elapsed - p.WindupStart) / p.windupDuration));
                    float landed = Mathf.Clamp01((elapsed - p.ImpactTime) / p.returnDuration);
                    source.localRotation = sourceRotation * Quaternion.Euler(0, 0, -p.leanDegrees * windup * (1 - landed));
                    Vector2 destination = layer.InverseTransformPoint(source.TransformPoint(source.rect.center));
                    Vector2 direction = (destination - origin).normalized;
                    Vector2 start = origin - direction * (width * .14f);
                    float flight = Mathf.Clamp01((elapsed - p.FlightStart) / p.flightDuration);
                    float t = flight * flight;
                    Vector2 control = (start + destination) * .5f + Vector2.up * Mathf.Min(width * .8f, Vector2.Distance(start, destination) * .35f);
                    chicken.rectTransform.anchoredPosition = elapsed < p.FlightStart
                        ? Vector2.Lerp(origin, start, windup)
                        : (1-t)*(1-t)*start + 2*(1-t)*t*control + t*t*destination;
                    float grow = Mathf.Clamp01((elapsed - p.chickenDelay) / p.chickenGrowDuration);
                    float scale = grow < .7f ? Mathf.Lerp(0, 1.08f, grow / .7f) : Mathf.Lerp(1.08f, 1, (grow-.7f)/.3f);
                    chicken.rectTransform.localScale = Vector3.one * scale;
                    chicken.rectTransform.localRotation = Quaternion.Euler(0, 0, -p.spinDegrees * flight);
                    chicken.enabled = elapsed >= p.chickenDelay && elapsed < p.ImpactTime;
                    Phase = elapsed < p.WindupStart ? "Transform" : elapsed < p.FlightStart ? "Windup"
                        : elapsed < p.ImpactTime ? "Flight" : elapsed < p.ChewStart ? "Caught" : "Chew";
                }
                refresh = Refresh;
                while (elapsed < p.Duration && target != null && source != null)
                {
                    if (!launched && elapsed >= p.FlightStart)
                    {
                        PlaySound(p.swish, p.swishOffset, 1, p, shared);
                        launched = true;
                    }
                    if (sound != null && sound.isPlaying && ((!launched && sound.time >= p.popEnd)
                        || (launched && sound.time >= p.swishEnd))) sound.Stop();
                    Refresh();
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }
                finished = target != null && source != null;
            }
            finally
            {
                ClearMotion();
                // Keep successful sacrifices hidden until projection removes their view.
                if (!finished) ClearVisuals();
            }
            if (finished)
            {
                if (chewing != null) StopCoroutine(chewing);
                chewing = StartCoroutine(PlayChewing(p, shared, Mathf.Max(0, p.ChewStart - elapsed)));
            }
        }
        // Owned by this view, not the director's blocking resolution coroutine.
        // Ordinary snapshot cleanup keeps it alive; disable/reset cancels it.
        private IEnumerator PlayChewing(FeastEffectProfile p, AudioSource shared, float delay)
        {
            if (chewSound == null)
            {
                chewSound = gameObject.AddComponent<AudioSource>();
                chewSound.playOnAwake = false;
                chewSound.spatialBlend = 0;
            }
            chewSound.Stop();
            if (delay > 0) yield return new WaitForSecondsRealtime(delay);
            chewSound.outputAudioMixerGroup = shared != null ? shared.outputAudioMixerGroup : null;
            chewSound.clip = p.chew;
            chewSound.pitch = p.chewPitch;
            RefreshVolume();
            if (p.chew != null)
            {
                chewSound.Play();
                float duration = Mathf.Min(p.chewEnd, p.chew.length) / Mathf.Max(.1f, p.chewPitch);
                yield return new WaitForSecondsRealtime(duration);
                chewSound.Stop();
            }
            chewing = null;
        }
        private static RawImage CreateImage(Transform parent, string name, Texture texture)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            image.maskable = false;
            return image;
        }
        private void PlaySound(AudioClip clip, float offset, float pitch, FeastEffectProfile p, AudioSource shared)
        {
            if (sound == null)
            {
                sound = gameObject.AddComponent<AudioSource>();
                sound.playOnAwake = false;
                sound.spatialBlend = 0;
            }
            sound.Stop();
            sound.outputAudioMixerGroup = shared != null ? shared.outputAudioMixerGroup : null;
            sound.pitch = pitch;
            gain = p.volume;
            RefreshVolume();
            sound.clip = clip;
            if (clip == null) return;
            sound.time = Mathf.Clamp(offset, 0, Mathf.Max(0, clip.length - .01f));
            sound.Play();
        }
    }
}
