using System.Collections;
using KLTN.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>A target-local club swing; presentation only, no gameplay randomness.</summary>
    public sealed class BonkEffectView : MonoBehaviour
    {
        private GameObject graphicsRoot;
        private AudioSource audioSource;
        private RectTransform shakenTarget;
        private Quaternion originalRotation;
        private System.Action refreshFrame;
        private float gain;
        public int LastVariant { get; private set; }
        private void OnEnable() => AudioVolumeSettings.Changed += RefreshVolume;
        private void LateUpdate() => RefreshLayout();
        public void RefreshLayout() => refreshFrame?.Invoke();
        private void OnDisable()
        {
            AudioVolumeSettings.Changed -= RefreshVolume;
            ClearVisuals();
        }
        private void RefreshVolume()
        {
            if (audioSource != null) audioSource.volume = gain * AudioVolumeSettings.SfxVolume;
        }
        public void ClearVisuals()
        {
            refreshFrame = null;
            if (audioSource != null) audioSource.Stop();
            if (shakenTarget != null) shakenTarget.localRotation = originalRotation;
            shakenTarget = null;
            if (graphicsRoot == null) return;
            graphicsRoot.SetActive(false);
            Destroy(graphicsRoot);
            graphicsRoot = null;
        }

        public IEnumerator Play(BonkEffectProfile profile, RectTransform layer,
            RectTransform target, AudioSource sharedSfx)
        {
            ClearVisuals();
            if (target == null || layer == null || profile == null || !profile.IsUsable) yield break;
            LastVariant = Random.Range(0, 3);
            int variant = LastVariant;
            graphicsRoot = new GameObject("BonkGraphics", typeof(RectTransform));
            var root = (RectTransform)graphicsRoot.transform;
            root.SetParent(layer, false);
            root.sizeDelta = Vector2.zero;
            RawImage stick = CreateImage(root, "BonkStick", profile.atlas, profile.sticks[variant]);
            RawImage bubble = CreateImage(root, "BonkBubble", profile.atlas, profile.bubble);
            stick.rectTransform.pivot = profile.grips[variant];
            shakenTarget = target;
            originalRotation = target.localRotation;
            float elapsed = 0f;
            PlaySound(profile, sharedSfx);
            try
            {
                void RefreshFrame()
                {
                    if (root == null || target == null) return;
                    // Measure the resting card, so the impact shake cannot move the swing pivot.
                    target.localRotation = originalRotation;
                    Vector2 left = layer.InverseTransformPoint(target.TransformPoint(new Vector2(target.rect.xMin, target.rect.yMax)));
                    Vector2 right = layer.InverseTransformPoint(target.TransformPoint(new Vector2(target.rect.xMax, target.rect.yMax)));
                    Vector2 down = layer.InverseTransformVector(target.TransformVector(Vector3.down * target.rect.height));
                    Vector2 across = right - left;
                    float width = across.magnitude;
                    // Keep the handle inside the animation canvas at the right edge.
                    bool mirror = right.x + width * .35f > layer.rect.xMax;
                    float side = mirror ? -1f : 1f;
                    Vector2 pivot = (mirror ? left : right) + across * (.22f * side) + down * .32f;
                    Vector2 contact = (left + right) * .5f + across * (.16f * side);
                    Vector2 localTip = profile.tips[variant] - profile.grips[variant];
                    localTip.x *= side;
                    float size = Vector2.Distance(pivot, contact) / Mathf.Max(.01f, localTip.magnitude);
                    float impactAngle = Mathf.Atan2(contact.y - pivot.y, contact.x - pivot.x) * Mathf.Rad2Deg
                        - Mathf.Atan2(localTip.y, localTip.x) * Mathf.Rad2Deg;
                    float raiseT = Mathf.Clamp01((elapsed - profile.appearDuration) / profile.raiseDuration);
                    float swingStart = profile.ImpactTime - profile.swingDuration;
                    float swingT = Mathf.Clamp01((elapsed - swingStart) / profile.swingDuration);
                    float recoilT = Mathf.Clamp01((elapsed - profile.ImpactTime - profile.contactHold) / profile.recoilDuration);
                    float offset = elapsed < swingStart
                        ? Mathf.Lerp(-34f, -62f, Mathf.SmoothStep(0, 1, raiseT))
                        : Mathf.Lerp(-62f, 0, swingT * swingT * swingT);
                    offset -= 16f * Mathf.Sin(recoilT * Mathf.PI * .5f);
                    stick.rectTransform.anchoredPosition = pivot;
                    stick.rectTransform.sizeDelta = Vector2.one * size;
                    stick.rectTransform.localScale = new Vector3(side, 1, 1);
                    stick.rectTransform.localRotation = Quaternion.Euler(0, 0, impactAngle + offset * side);
                    stick.color = new Color(1, 1, 1, Mathf.Clamp01(elapsed / profile.appearDuration) * (1f - recoilT));
                    float age = elapsed - profile.ImpactTime;
                    bubble.enabled = age >= 0;
                    if (bubble.enabled)
                    {
                        float scale = age < .06f ? Mathf.Lerp(.65f, 1.15f, age / .06f)
                            : age < .13f ? Mathf.Lerp(1.15f, 1f, (age - .06f) / .07f) : 1f;
                        float fade = Mathf.Clamp01((age - (profile.bubbleDuration - .14f)) / .14f);
                        float bubbleSize = width * profile.bubbleWidth;
                        Vector2 position = contact - down.normalized * (bubbleSize * .38f + fade * width * .12f);
                        float half = bubbleSize * scale * .5f;
                        position.x = Mathf.Clamp(position.x, layer.rect.xMin + half, layer.rect.xMax - half);
                        position.y = Mathf.Clamp(position.y, layer.rect.yMin + half, layer.rect.yMax - half);
                        bubble.rectTransform.anchoredPosition = position;
                        bubble.rectTransform.sizeDelta = Vector2.one * bubbleSize;
                        bubble.rectTransform.localScale = Vector3.one * scale;
                        bubble.color = new Color(1, 1, 1, 1f - fade);
                        float shake = age < .09f ? Mathf.Sin(age / .09f * Mathf.PI * 4) * 2f * (1f - age / .09f) : 0;
                        target.localRotation = originalRotation * Quaternion.Euler(0, 0, shake);
                    }
                }
                refreshFrame = RefreshFrame;
                while (elapsed < profile.Duration && target != null)
                {
                    RefreshFrame();
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            finally { ClearVisuals(); }
        }

        private static RawImage CreateImage(Transform parent, string name, Texture atlas, Rect uv)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<RawImage>();
            image.texture = atlas;
            image.uvRect = uv;
            image.raycastTarget = false;
            image.maskable = false;
            return image;
        }
        private void PlaySound(BonkEffectProfile profile, AudioSource sharedSfx)
        {
            if (profile.hitSfx == null) return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0;
            }
            audioSource.outputAudioMixerGroup = sharedSfx != null ? sharedSfx.outputAudioMixerGroup : null;
            gain = profile.sfxVolume;
            RefreshVolume();
            audioSource.clip = profile.hitSfx;
            audioSource.time = 0;
            audioSource.Play();
        }
    }
}
