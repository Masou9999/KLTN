using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Plays layered round/priority banners while blocking only the gameplay interaction
    /// area; top-bar controls remain available.
    /// </summary>
    public sealed class MatchTransitionBanner : MonoBehaviour
    {
        #region Types

        private enum SlideOrigin
        {
            Left = -1,
            Right = 1,
        }

        #endregion

        #region Serialized Fields

        [Header("References")]
        [SerializeField]
        private RectTransform canvasRoot;

        [SerializeField]
        private RectTransform bannerRoot;

        [SerializeField]
        private RectTransform textRoot;

        [SerializeField]
        private CanvasGroup bannerCanvasGroup;

        [SerializeField]
        private Image backgroundImage;

        [SerializeField]
        private Image accentImage;

        [SerializeField]
        private TMP_Text label;

        [SerializeField]
        private GameObject gameplayInputBlocker;

        [Header("Colors")]
        [SerializeField]
        private Color yourTurnColor = new Color(0.05f, 0.30f, 0.85f, 0.95f);

        [FormerlySerializedAs("roundEndColor")]
        [SerializeField]
        private Color roundStartColor = new Color(0.75f, 0.05f, 0.10f, 0.95f);

        [Header("Movement")]
        [SerializeField, Min(0f)]
        private float backgroundTravelMultiplier = 1.05f;

        [SerializeField, Min(0f)]
        private float textTravelDistance = 120f;

        [Header("Timing")]
        [SerializeField, Min(0f)]
        private float enterDuration = 0.45f;

        [SerializeField, Min(0f)]
        private float holdDuration = 0.75f;

        [SerializeField, Min(0f)]
        private float exitDuration = 0.35f;

        [Header("Entry")]
        [SerializeField, Range(0.1f, 1f)]
        private float fullOpacityAt = 0.55f;

        [SerializeField, Range(0f, 0.9f)]
        private float textRevealStartNormalized = 0.3f;

        [SerializeField, Range(0.05f, 1f)]
        private float characterFadeSpanNormalized = 0.25f;

        [Header("Exit")]
        [SerializeField, Range(0.1f, 1f)]
        private float textExitEndNormalized = 0.72f;

        [SerializeField, Range(0f, 0.95f)]
        private float backgroundExitStartNormalized = 0.58f;

        [SerializeField, Range(0.05f, 1f)]
        private float characterExitFadeSpanNormalized = 0.3f;

        [SerializeField, Min(1)]
        private int exitCharacterGroupSize = 2;

        #endregion

        #region Runtime State

        private readonly List<int> visibleCharacterIndices = new List<int>();

        private Vector2 bannerRestPosition;
        private Vector2 textRestPosition;
        private byte[] baseCharacterAlphas = new byte[0];
        private bool initialized;

        #endregion

        #region Public State

        public bool IsPlaying { get; private set; }

        public event Action<bool> PlayingStateChanged;

private void SetPlaying(bool playing)
{
    if (IsPlaying == playing)
    {
        return;
    }

    IsPlaying = playing;
    PlayingStateChanged?.Invoke(playing);
}

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (!HasRequiredReferences())
            {
                return;
            }

            Initialize();
            HideVisualImmediate();
            SetGameplayBlocked(false);
        }

        private void OnDisable()
        {
            HideVisualImmediate();
            SetGameplayBlocked(false);
            SetPlaying(false);
        }

        #endregion

        #region Public Playback API

        public IEnumerator PlayYourTurn()
        {
            yield return Play("Lượt của bạn", yourTurnColor, SlideOrigin.Right);
        }

        public IEnumerator PlayRoundStart(int roundNumber)
        {
            yield return Play($"Vòng {roundNumber}", roundStartColor, SlideOrigin.Left);
        }

        #endregion

        #region Playback

        private IEnumerator Play(string message, Color color, SlideOrigin origin)
        {
            if (!HasRequiredReferences())
            {
                Debug.LogError(
                    $"{nameof(MatchTransitionBanner)} trên {name} chưa được gán đủ reference."
                );

                yield break;
            }

            while (IsPlaying)
            {
                yield return null;
            }

            SetPlaying(true);
            SetGameplayBlocked(true);

            try
            {
                Initialize();
                PrepareVisual(message, color, origin);

                float direction = (float)origin;
                float travelDistance = GetBackgroundTravelDistance();

                yield return AnimateEnter(direction, travelDistance);
                yield return WaitUnscaled(holdDuration);
                yield return AnimateExit(direction, travelDistance);
            }
            finally
            {
                HideVisualImmediate();
                SetGameplayBlocked(false);
                SetPlaying(false);
            }
        }

        private IEnumerator AnimateEnter(float direction, float travelDistance)
        {
            if (enterDuration <= 0f)
            {
                ApplyEnterFrame(1f, direction, travelDistance);
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < enterDuration)
            {
                ApplyEnterFrame(elapsed / enterDuration, direction, travelDistance);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            ApplyEnterFrame(1f, direction, travelDistance);
        }

        private IEnumerator AnimateExit(float direction, float travelDistance)
        {
            if (exitDuration <= 0f)
            {
                ApplyExitFrame(1f, direction, travelDistance);
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < exitDuration)
            {
                ApplyExitFrame(elapsed / exitDuration, direction, travelDistance);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            ApplyExitFrame(1f, direction, travelDistance);
        }

        #endregion

        #region Animation Frames

        private void ApplyEnterFrame(
            float normalizedTime,
            float direction,
            float travelDistance
        )
        {
            float backgroundProgress = EaseOutCubic(normalizedTime);
            float opacityProgress = Mathf.Clamp01(
                normalizedTime / Mathf.Max(0.01f, fullOpacityAt)
            );
            float textProgress = Mathf.InverseLerp(
                textRevealStartNormalized,
                1f,
                normalizedTime
            );

            Vector2 bannerStart =
                bannerRestPosition + Vector2.right * direction * travelDistance;
            Vector2 textStart =
                textRestPosition + Vector2.right * direction * textTravelDistance;

            bannerRoot.anchoredPosition = Vector2.LerpUnclamped(
                bannerStart,
                bannerRestPosition,
                backgroundProgress
            );

            textRoot.anchoredPosition = Vector2.LerpUnclamped(
                textStart,
                textRestPosition,
                EaseOutCubic(textProgress)
            );

            bannerCanvasGroup.alpha = Mathf.SmoothStep(0f, 1f, opacityProgress);
            ApplyCharacterReveal(textProgress);
        }

        private void ApplyExitFrame(
            float normalizedTime,
            float direction,
            float travelDistance
        )
        {
            float textProgress = Mathf.Clamp01(
                normalizedTime / Mathf.Max(0.01f, textExitEndNormalized)
            );

            float backgroundProgress = Mathf.InverseLerp(
                backgroundExitStartNormalized,
                1f,
                normalizedTime
            );

            Vector2 bannerEnd =
                bannerRestPosition + Vector2.right * direction * travelDistance;
            Vector2 textEnd =
                textRestPosition + Vector2.right * direction * textTravelDistance;

            textRoot.anchoredPosition = Vector2.LerpUnclamped(
                textRestPosition,
                textEnd,
                Mathf.SmoothStep(0f, 1f, textProgress)
            );

            bannerRoot.anchoredPosition = Vector2.LerpUnclamped(
                bannerRestPosition,
                bannerEnd,
                EaseInCubic(backgroundProgress)
            );

            ApplyCharacterExit(textProgress);
            bannerCanvasGroup.alpha = 1f - Mathf.SmoothStep(0f, 1f, backgroundProgress);
        }

        #endregion

        #region TMP Character Animation

        private void CacheVisibleCharacters()
        {
            label.ForceMeshUpdate();

            visibleCharacterIndices.Clear();
            baseCharacterAlphas = new byte[label.textInfo.characterCount];

            for (int i = 0; i < label.textInfo.characterCount; i++)
            {
                TMP_CharacterInfo characterInfo = label.textInfo.characterInfo[i];

                if (!characterInfo.isVisible)
                {
                    continue;
                }

                int materialIndex = characterInfo.materialReferenceIndex;
                int vertexIndex = characterInfo.vertexIndex;
                Color32[] colors = label.textInfo.meshInfo[materialIndex].colors32;

                visibleCharacterIndices.Add(i);
                baseCharacterAlphas[i] = colors[vertexIndex].a;
            }
        }

        private void ApplyCharacterReveal(float progress)
        {
            int characterCount = visibleCharacterIndices.Count;

            for (int order = 0; order < characterCount; order++)
            {
                float start = CharacterStart(
                    order,
                    characterCount,
                    characterFadeSpanNormalized
                );

                float localProgress = Mathf.InverseLerp(
                    start,
                    Mathf.Min(1f, start + characterFadeSpanNormalized),
                    progress
                );

                SetCharacterAlpha(
                    visibleCharacterIndices[order],
                    Mathf.SmoothStep(0f, 1f, localProgress)
                );
            }

            label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }

        private void ApplyCharacterExit(float progress)
        {
            int characterCount = visibleCharacterIndices.Count;
            int groupSize = Mathf.Max(1, exitCharacterGroupSize);
            int groupCount = Mathf.CeilToInt(characterCount / (float)groupSize);

            for (int order = 0; order < characterCount; order++)
            {
                int reverseOrder = characterCount - 1 - order;
                int groupOrder = reverseOrder / groupSize;

                float start = CharacterStart(
                    groupOrder,
                    groupCount,
                    characterExitFadeSpanNormalized
                );

                float localProgress = Mathf.InverseLerp(
                    start,
                    Mathf.Min(1f, start + characterExitFadeSpanNormalized),
                    progress
                );

                SetCharacterAlpha(
                    visibleCharacterIndices[order],
                    1f - Mathf.SmoothStep(0f, 1f, localProgress)
                );
            }

            label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }

        private void SetCharacterAlpha(int characterIndex, float normalizedAlpha)
        {
            TMP_CharacterInfo characterInfo = label.textInfo.characterInfo[
                characterIndex
            ];
            int materialIndex = characterInfo.materialReferenceIndex;
            int vertexIndex = characterInfo.vertexIndex;
            Color32[] colors = label.textInfo.meshInfo[materialIndex].colors32;

            byte baseAlpha = baseCharacterAlphas[characterIndex];
            byte alpha = (byte)
                Mathf.RoundToInt(baseAlpha * Mathf.Clamp01(normalizedAlpha));

            for (int i = 0; i < 4; i++)
            {
                Color32 color = colors[vertexIndex + i];
                color.a = alpha;
                colors[vertexIndex + i] = color;
            }
        }

        private static float CharacterStart(int order, int count, float fadeSpan)
        {
            if (count <= 1)
            {
                return 0f;
            }

            return (1f - fadeSpan) * order / (count - 1f);
        }

        #endregion

        #region Visual Setup

        private void PrepareVisual(string message, Color color, SlideOrigin origin)
        {
            label.text = message;
            backgroundImage.color = color;

            if (accentImage != null)
            {
                Color accentColor = Color.Lerp(color, Color.white, 0.4f);
                accentColor.a = color.a;
                accentImage.color = accentColor;
            }

            Canvas.ForceUpdateCanvases();
            CacheVisibleCharacters();

            float direction = (float)origin;
            float travelDistance = GetBackgroundTravelDistance();

            bannerRoot.anchoredPosition =
                bannerRestPosition + Vector2.right * direction * travelDistance;

            textRoot.anchoredPosition =
                textRestPosition + Vector2.right * direction * textTravelDistance;

            bannerCanvasGroup.alpha = 0f;
            ApplyCharacterReveal(0f);
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            bannerRestPosition = bannerRoot.anchoredPosition;
            textRestPosition = textRoot.anchoredPosition;
            initialized = true;
        }

        private void HideVisualImmediate()
        {
            if (!initialized)
            {
                return;
            }

            bannerRoot.anchoredPosition = bannerRestPosition;
            textRoot.anchoredPosition = textRestPosition;
            bannerCanvasGroup.alpha = 0f;
        }

        private void SetGameplayBlocked(bool blocked)
        {
            if (gameplayInputBlocker != null)
            {
                gameplayInputBlocker.SetActive(blocked);
            }
        }

        #endregion

        #region Helpers

        private float GetBackgroundTravelDistance()
        {
            float canvasWidth = Mathf.Max(canvasRoot.rect.width, Screen.width);
            return canvasWidth * backgroundTravelMultiplier;
        }

        private bool HasRequiredReferences()
        {
            return canvasRoot != null
                && bannerRoot != null
                && textRoot != null
                && bannerCanvasGroup != null
                && backgroundImage != null
                && label != null
                && gameplayInputBlocker != null;
        }

        private static float EaseOutCubic(float value)
        {
            value = Mathf.Clamp01(value);
            float inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
        }

        private static float EaseInCubic(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * value;
        }

        private static IEnumerator WaitUnscaled(float duration)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        #endregion
    }
}
