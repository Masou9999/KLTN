using System;
using System.Collections;
using KLTN.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    public sealed class BatSwarmView : MonoBehaviour
    {
        private GameObject graphics;
        private AudioSource wings, chirp;
        private BatSwarmProfile profile;
        private float envelope;
        private Action refresh;
        public string Phase { get; private set; }
        private void OnEnable() => AudioVolumeSettings.Changed += RefreshVolume;
        private void LateUpdate() => refresh?.Invoke();
        private void OnDisable() { AudioVolumeSettings.Changed -= RefreshVolume; ClearVisuals(); }
        private void RefreshVolume()
        {
            if (profile == null) return;
            if (wings != null) wings.volume = profile.wingsVolume * envelope * AudioVolumeSettings.SfxVolume;
            if (chirp != null) chirp.volume = profile.chirpVolume * envelope * AudioVolumeSettings.SfxVolume;
        }
        public void ClearVisuals()
        {
            refresh = null;
            if (wings != null) wings.Stop();
            if (chirp != null) chirp.Stop();
            if (graphics != null) { graphics.SetActive(false); Destroy(graphics); }
            graphics = null;
            Phase = "Idle";
        }
        public IEnumerator Play(BatSwarmProfile p, RectTransform layer, RectTransform source,
            RectTransform enemy, RectTransform ally, int damage, int heal, Action onDamage, Action onHeal, AudioSource shared)
        {
            ClearVisuals();
            if (source == null || enemy == null || ally == null || !p.IsUsable) yield break;
            profile = p;
            graphics = new GameObject("BatSwarmGraphics", typeof(RectTransform));
            var root = (RectTransform)graphics.transform;
            root.SetParent(layer, false); root.sizeDelta = Vector2.zero;
            var bats = new RawImage[12];
            for (int i = 0; i < bats.Length; i++)
            {
                var go = new GameObject("SwarmBat" + i, typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(root, false);
                bats[i] = go.GetComponent<RawImage>(); bats[i].texture = p.sheet;
                bats[i].raycastTarget = false; bats[i].maskable = false;
                bats[i].rectTransform.sizeDelta = new Vector2(p.batWidth, p.batWidth * .75f);
            }
            var minus = Popup(root, "SwarmDamage", "-" + damage, new Color(1, .25f, .3f));
            var plus = Popup(root, "SwarmHeal", "+" + heal, new Color(.3f, 1, .55f));
            wings = PrepareAudio(wings, p.wings, shared, true);
            wings.pitch = p.wingsPitch;
            chirp = PrepareAudio(chirp, p.chirp, shared, false);
            envelope = 0; RefreshVolume(); if (p.wings != null) wings.Play();
            float elapsed = 0;
            bool bit = false, drained = false, healed = false;
            Vector2 Center(RectTransform r) => layer.InverseTransformPoint(r.TransformPoint(r.rect.center));
            try
            {
                void Refresh()
                {
                    if (graphics == null || enemy == null || ally == null || source == null) return;
                    Vector2 start = Center(source), end = Center(enemy), home = Center(ally);
                    float hudWidth = Vector3.Distance(layer.InverseTransformPoint(enemy.TransformPoint(new Vector2(enemy.rect.xMin, 0))),
                        layer.InverseTransformPoint(enemy.TransformPoint(new Vector2(enemy.rect.xMax, 0))));
                    float radius = Mathf.Clamp(hudWidth * .45f, 35, 95);
                    for (int i = 0; i < 12; i++)
                    {
                        // Three simultaneous groups: 6 + 4 + 2, never twelve single launches.
                        float launch = i < 6 ? 0 : i < 10 ? .1f : .2f;
                        float angle = i * Mathf.PI * 2 / 12;
                        Vector2 ring = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * .5f);
                        float depart = p.disperseTime + i * .009f;
                        float arrive = p.arrivalTime + i * .015f;
                        RawImage bat = bats[i];
                        bat.enabled = elapsed >= launch && elapsed < arrive;
                        if (!bat.enabled) continue;
                        Vector2 point;
                        float scale = 1;
                        if (elapsed < p.surroundTime)
                        {
                            float t = Mathf.Clamp01((elapsed-launch)/(p.surroundTime-launch));
                            point = Arc(start, end + ring, t, (i % 2 == 0 ? 1 : -1) * (75 + i*4));
                            scale = Mathf.Clamp01((elapsed-launch)/.07f);
                        }
                        else if (elapsed < depart)
                        {
                            // Small displacement, high vertical velocity gives a biting tremor.
                            point = end + ring + new Vector2(Mathf.Sin(elapsed*43+i)*3,
                                Mathf.Sin(elapsed*83+i*1.7f)*9);
                        }
                        else
                        {
                            float t = Mathf.Clamp01((elapsed-depart)/(arrive-depart));
                            point = Arc(end+ring, home, t*t, (i % 2 == 0 ? 1 : -1) * (65+i*3));
                            scale = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((t-.65f)/.35f));
                        }
                        bat.rectTransform.anchoredPosition = point;
                        bat.rectTransform.localScale = Vector3.one * scale;
                        bat.rectTransform.localRotation = Quaternion.Euler(0,0,Mathf.Sin(elapsed*9+i)*12);
                        int frame = ((int)(elapsed*16)+i)%4;
                        // Body-aligned regions in the supplied four-pose sheet.
                        float x = frame % 2 == 0 ? 0 : 627;
                        float top = frame < 2 ? 55 : 615;
                        bat.uvRect = new Rect(x/1254f, (1254-top-500)/1254f, 627/1254f, 500/1254f);
                    }
                    AnimatePopup(minus, end, elapsed-p.disperseTime, damage > 0);
                    AnimatePopup(plus, home, elapsed-(p.disperseTime+.16f), heal > 0);
                    Phase = elapsed < p.surroundTime ? "Outbound" : elapsed < p.disperseTime ? "Biting" : "Returning";
                }
                refresh = Refresh;
                while (elapsed < p.duration && source != null && enemy != null && ally != null)
                {
                    envelope = Mathf.Clamp01(elapsed/.15f) * Mathf.Clamp01((p.duration-elapsed)/.3f);
                    RefreshVolume();
                    if (!bit && elapsed >= p.surroundTime)
                    {
                        if (p.chirp != null) { chirp.time = Mathf.Min(.16f, p.chirp.length*.5f); chirp.Play(); }
                        bit = true;
                    }
                    if (!drained && elapsed >= p.disperseTime) { onDamage?.Invoke(); drained = true; }
                    if (!healed && elapsed >= p.disperseTime+.16f) { onHeal?.Invoke(); healed = true; }
                    Refresh(); yield return null; elapsed += Time.unscaledDeltaTime;
                }
                if (!drained) onDamage?.Invoke();
                if (!healed) onHeal?.Invoke();
            }
            finally { ClearVisuals(); }
        }
        private static Vector2 Arc(Vector2 a, Vector2 b, float t, float bend)
        {
            Vector2 d = b-a, normal = new Vector2(-d.y,d.x).normalized;
            Vector2 c = (a+b)*.5f + normal*bend;
            return (1-t)*(1-t)*a + 2*(1-t)*t*c + t*t*b;
        }
        private AudioSource PrepareAudio(AudioSource audio, AudioClip clip, AudioSource shared, bool loop)
        {
            if (audio == null) audio = gameObject.AddComponent<AudioSource>();
            audio.playOnAwake = false; audio.spatialBlend = 0; audio.loop = loop; audio.clip = clip;
            audio.outputAudioMixerGroup = shared != null ? shared.outputAudioMixerGroup : null;
            return audio;
        }
        private static TMP_Text Popup(Transform root, string name, string text, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(root, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text; label.fontSize = 42; label.fontStyle = FontStyles.Bold;
            label.color = color; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(180,80);
            label.enabled = false;
            return label;
        }
        private static void AnimatePopup(TMP_Text text, Vector2 origin, float age, bool show)
        {
            text.enabled = show && age >= 0 && age < .65f;
            text.rectTransform.anchoredPosition = origin + new Vector2(55, 15+Mathf.Clamp01(age/.65f)*45);
            text.alpha = 1-Mathf.Clamp01((age-.35f)/.3f);
        }
    }
}
