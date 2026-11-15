using System.Collections;
using KLTN.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>Stationary, target-local bite. Never launches a source-to-target projectile.</summary>
    public sealed class BiteEffectView : MonoBehaviour
    {
        private GameObject graphicsRoot;
        private RectTransform shakenTarget;
        private Quaternion originalRotation;
        private AudioSource audioSource;
        private float gain;
        private System.Action refreshFrame;

        private void OnEnable() => AudioVolumeSettings.Changed += RefreshVolume;
        private void LateUpdate() => RefreshLayout();
        public void RefreshLayout() => refreshFrame?.Invoke();

        private void OnDisable()
        {
            AudioVolumeSettings.Changed -= RefreshVolume;
            ClearVisuals();
            if (audioSource != null) audioSource.Stop();
        }

        private void RefreshVolume()
        {
            if (audioSource != null) audioSource.volume = AudioVolumeSettings.SfxVolume * gain;
        }

        public void ClearVisuals()
        {
            refreshFrame = null;
            if (shakenTarget != null) shakenTarget.localRotation = originalRotation;
            shakenTarget = null;
            if (graphicsRoot == null) return;
            graphicsRoot.SetActive(false);
            Destroy(graphicsRoot);
            graphicsRoot = null;
        }

        public IEnumerator Play(BiteEffectProfile profile, RectTransform layer,
            RectTransform target, AudioSource sharedSfx)
        {
            ClearVisuals();
            if (target == null || profile == null || !profile.IsUsable) yield break;
            graphicsRoot = new GameObject("BiteGraphics", typeof(RectTransform));
            RectTransform root = (RectTransform)graphicsRoot.transform;
            root.SetParent(layer, false);
            root.sizeDelta = Vector2.zero;
            RawImage echo = CreateImage(root, "BiteEcho", profile.sheet);
            RawImage jaw = CreateImage(root, "BiteJaw", profile.sheet);
            shakenTarget = target;
            originalRotation = target.localRotation;
            float impactTime = profile.TimeBeforeFrame(6);
            float echoStart = profile.TimeBeforeFrame(8);
            float holdEnd = profile.TimeBeforeFrame(9);
            float total = Mathf.Max(holdEnd + profile.fadeDuration, echoStart + profile.echoDuration);
            float elapsed = 0f;
            bool hit = false;

            try
            {
                void RefreshFrame()
                {
                    if (target == null || root == null) return;
                    float shakeTime = elapsed - impactTime;
                    float shake = shakeTime >= 0f && shakeTime < profile.shakeDuration
                        ? Mathf.Sin(shakeTime / profile.shakeDuration * Mathf.PI * 4f)
                            * profile.shakeDegrees * (1f - shakeTime / profile.shakeDuration) : 0f;
                    target.localRotation = originalRotation * Quaternion.Euler(0f, 0f, shake);
                    Vector2 center = layer.InverseTransformPoint(target.TransformPoint(target.rect.center));
                    root.anchoredPosition = center;
                    Vector3 left = layer.InverseTransformPoint(target.TransformPoint(
                        new Vector3(target.rect.xMin, target.rect.center.y)));
                    Vector3 right = layer.InverseTransformPoint(target.TransformPoint(
                        new Vector3(target.rect.xMax, target.rect.center.y)));
                    float pixelScale = Vector3.Distance(left, right) * profile.widthRelativeToCard
                        / profile.frames[0].width;
                    SetFrame(jaw, profile, profile.FrameAt(elapsed), pixelScale);
                    jaw.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01(
                        (elapsed - holdEnd) / Mathf.Max(0.01f, profile.fadeDuration)));
                    echo.enabled = elapsed >= echoStart;
                    if (echo.enabled)
                    {
                        SetFrame(echo, profile, 8, pixelScale);
                        float t = Mathf.Clamp01((elapsed - echoStart) / Mathf.Max(0.01f, profile.echoDuration));
                        float ease = 1f - (1f - t) * (1f - t);
                        echo.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, profile.echoScale, ease);
                        echo.color = new Color(1f, 1f, 1f, profile.echoOpacity * (1f - t));
                    }
                }

                refreshFrame = RefreshFrame;
                while (elapsed < total && target != null)
                {
                    if (!hit && elapsed >= impactTime)
                    {
                        PlaySound(profile, sharedSfx);
                        hit = true;
                    }
                    RefreshFrame();
                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }
                if (!hit && target != null) PlaySound(profile, sharedSfx);
            }
            finally { ClearVisuals(); }
        }

        private static RawImage CreateImage(Transform parent, string name, Texture texture)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            RawImage image = go.GetComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            image.maskable = false;
            return image;
        }

        private static void SetFrame(RawImage image, BiteEffectProfile profile, int index, float scale)
        {
            Rect frame = profile.frames[index];
            image.uvRect = new Rect(frame.x / profile.sheet.width, frame.y / profile.sheet.height,
                frame.width / profile.sheet.width, frame.height / profile.sheet.height);
            image.rectTransform.sizeDelta = frame.size * scale;
        }

        private void PlaySound(BiteEffectProfile profile, AudioSource sharedSfx)
        {
            if (profile.biteSfx == null) return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            audioSource.outputAudioMixerGroup = sharedSfx != null ? sharedSfx.outputAudioMixerGroup : null;
            gain = profile.sfxVolume;
            RefreshVolume();
            audioSource.clip = profile.biteSfx;
            audioSource.time = Mathf.Clamp(profile.sfxStartTime, 0f, Mathf.Max(0f, profile.biteSfx.length - 0.01f));
            audioSource.Play();
        }
    }
}
