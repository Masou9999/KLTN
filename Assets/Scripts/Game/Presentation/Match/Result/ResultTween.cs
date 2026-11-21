using System;
using System.Collections;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Small unscaled-time tween helpers for the result screen. Unscaled time keeps the
    /// screen animating when gameplay pauses time after the match.
    /// </summary>
    public static class ResultTween
    {
        public static IEnumerator Run(float duration, Action<float> step,
            Func<float, float> ease = null)
        {
            ease ??= EaseOutCubic;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                step(ease(Mathf.Clamp01(elapsed / duration)));
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            step(ease(1f));
        }

        public static IEnumerator Wait(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }

        public static IEnumerator Fade(CanvasGroup group, float to, float duration)
        {
            if (group == null) yield break;
            float from = group.alpha;
            yield return Run(duration, t => group.alpha = Mathf.Lerp(from, to, t), Linear);
        }

        public static float Linear(float t) => t;

        public static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

        public static float EaseInOutSine(float t) => -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;

        /// <summary>Overshoots slightly before settling, for pop-in scale.</summary>
        public static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public static void SetVisible(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }
    }
}
