using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Lightweight particle emitter made of pooled UI images. GameScene renders through a
    /// Screen Space Overlay canvas, where ParticleSystem renderers cannot be layered
    /// between UI images; these particles follow the canvas hierarchy instead.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiSparkleEmitter : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private Sprite sprite;
        [SerializeField] private Color tint = new Color(1f, 0.93f, 0.7f, 1f);
        [SerializeField] private Vector2 sizeRange = new Vector2(18f, 46f);
        [SerializeField] private Vector2 speedRange = new Vector2(60f, 260f);
        [SerializeField] private Vector2 lifetimeRange = new Vector2(0.5f, 1.2f);
        [SerializeField] private float spawnRadius = 60f;
        [SerializeField] private float spinSpeed = 180f;
        [SerializeField] private float drag = 1.6f;
        [SerializeField] private int poolSize = 48;

        #endregion

        #region Runtime State

        private sealed class Particle
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Velocity;
            public float Age;
            public float Lifetime;
            public float Size;
            public float Spin;
            public bool Alive;
        }

        private readonly List<Particle> particles = new List<Particle>();
        private float emitRate;
        private float emitTimeLeft;
        private float emitBudget;

        #endregion

        #region Unity Lifecycle

        private void OnDisable()
        {
            Clear();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (emitTimeLeft > 0f)
            {
                emitTimeLeft -= dt;
                emitBudget += emitRate * dt;
                while (emitBudget >= 1f)
                {
                    emitBudget -= 1f;
                    Spawn();
                }
            }

            foreach (Particle p in particles)
            {
                if (!p.Alive) continue;
                p.Age += dt;
                if (p.Age >= p.Lifetime)
                {
                    p.Alive = false;
                    p.Rect.gameObject.SetActive(false);
                    continue;
                }
                p.Velocity *= Mathf.Max(0f, 1f - drag * dt);
                p.Rect.anchoredPosition += p.Velocity * dt;
                p.Rect.localRotation *= Quaternion.Euler(0f, 0f, p.Spin * dt);
                float life = p.Age / p.Lifetime;
                // Quick twinkle in, slow fade out.
                float alpha = life < 0.2f ? life / 0.2f : 1f - (life - 0.2f) / 0.8f;
                float scale = Mathf.Lerp(0.6f, 1f, Mathf.Min(1f, life * 4f))
                    * (1f + 0.15f * Mathf.Sin(p.Age * 18f));
                p.Rect.sizeDelta = Vector2.one * p.Size * scale;
                Color color = tint;
                color.a *= Mathf.Clamp01(alpha);
                p.Image.color = color;
            }
        }

        #endregion

        #region Public API

        public void Burst(int count)
        {
            for (int i = 0; i < count; i++) Spawn();
        }

        /// <summary>Emits <paramref name="rate"/> particles per second for a while.</summary>
        public void Emit(float rate, float duration)
        {
            emitRate = rate;
            emitTimeLeft = duration;
        }

        public void Clear()
        {
            emitTimeLeft = 0f;
            emitBudget = 0f;
            foreach (Particle p in particles)
            {
                p.Alive = false;
                if (p.Rect != null) p.Rect.gameObject.SetActive(false);
            }
        }

        #endregion

        #region Helpers

        private void Spawn()
        {
            Particle p = Acquire();
            if (p == null) return;
            Vector2 direction = Random.insideUnitCircle.normalized;
            if (direction == Vector2.zero) direction = Vector2.up;
            p.Rect.anchoredPosition = direction * Random.Range(0f, spawnRadius);
            p.Velocity = direction * Random.Range(speedRange.x, speedRange.y);
            p.Lifetime = Random.Range(lifetimeRange.x, lifetimeRange.y);
            p.Size = Random.Range(sizeRange.x, sizeRange.y);
            p.Spin = Random.Range(-spinSpeed, spinSpeed);
            p.Age = 0f;
            p.Alive = true;
            p.Rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            p.Rect.sizeDelta = Vector2.one * p.Size * 0.6f;
            p.Image.color = new Color(tint.r, tint.g, tint.b, 0f);
            p.Rect.gameObject.SetActive(true);
        }

        private Particle Acquire()
        {
            foreach (Particle p in particles)
            {
                if (!p.Alive) return p;
            }
            if (particles.Count >= poolSize) return null;

            var go = new GameObject("Sparkle", typeof(RectTransform), typeof(Image));
            go.layer = gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            var particle = new Particle { Rect = rect, Image = image };
            particles.Add(particle);
            return particle;
        }

        #endregion
    }
}
