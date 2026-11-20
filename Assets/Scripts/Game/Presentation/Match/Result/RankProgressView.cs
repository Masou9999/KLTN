using System;
using System.Collections;
using System.Collections.Generic;
using KLTN.Game.Content;
using KLTN.Game.Domain.Ranking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Stage 2 of the result screen: animates season points along the rank bar.
    /// Gains fill left to right; losses shrink right to left over a lighter backdrop
    /// that keeps the starting fill. Crossing a threshold plays the promotion
    /// (icon to centre, light rays, two sparkle layers) or the demotion drift.
    /// The top tier has no bar: icon, counted points and a caption, centred together.
    /// </summary>
    public sealed class RankProgressView : MonoBehaviour
    {
        #region Constants

        private const float DemotionDrift = 48f;
        private const float PromotionScale = 1.55f;

        #endregion

        #region Serialized Fields

        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform body;
        [SerializeField] private CanvasGroup bodyGroup;

        [Header("Progress Layout")]
        [SerializeField] private CanvasGroup barGroup;
        [SerializeField] private Image currentIcon;
        [SerializeField] private Image nextIcon;
        [SerializeField] private TMP_Text pointsText;
        [SerializeField] private RectTransform barArea;
        [SerializeField] private Image backdrop;
        [SerializeField] private Image fill;
        [SerializeField] private Image tipGlow;

        [Header("Top Tier Layout")]
        [SerializeField] private CanvasGroup maxGroup;
        [SerializeField] private Image maxIcon;
        [SerializeField] private TMP_Text maxPoints;

        [Header("Promotion")]
        [SerializeField] private RectTransform promoIcon;
        [SerializeField] private Image promoIconImage;
        [SerializeField] private Image rays;
        [SerializeField] private Image glow;
        [SerializeField] private UiSparkleEmitter backSparkles;
        [SerializeField] private UiSparkleEmitter frontSparkles;

        [Header("Exit")]
        [SerializeField] private CanvasGroup exitGroup;
        [SerializeField] private Button exitButton;

        #endregion

        #region Runtime State

        private RankCatalog catalog;
        private Coroutine playRoutine;
        private bool raysSpinning;

        #endregion

        #region Events

        public event Action ExitRequested;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (exitButton != null) exitButton.onClick.AddListener(OnExitClicked);
        }

        private void OnDestroy()
        {
            if (exitButton != null) exitButton.onClick.RemoveListener(OnExitClicked);
        }

        private void Update()
        {
            if (raysSpinning && rays != null)
                rays.rectTransform.localRotation *=
                    Quaternion.Euler(0f, 0f, -25f * Time.unscaledDeltaTime);
        }

        #endregion

        #region Public API

        public void Play(int pointsBefore, int pointsAfter)
        {
            gameObject.SetActive(true);
            if (playRoutine != null) StopCoroutine(playRoutine);
            playRoutine = StartCoroutine(PlayRoutine(pointsBefore, pointsAfter));
        }

        public void Hide()
        {
            if (playRoutine != null) StopCoroutine(playRoutine);
            playRoutine = null;
            ResultTween.SetVisible(group, false);
            gameObject.SetActive(false);
        }

        #endregion

        #region Sequence

        private IEnumerator PlayRoutine(int pointsBefore, int pointsAfter)
        {
            catalog ??= RankCatalog.Load();
            IReadOnlyList<RankProgressSegment> path =
                RankProgressPath.Build(pointsBefore, pointsAfter);

            ResetPromotion();
            ResultTween.SetVisible(exitGroup, false);
            body.anchoredPosition = Vector2.zero;
            bodyGroup.alpha = 1f;
            ApplyLayout(path[0]);
            ResultTween.SetVisible(group, true);
            group.alpha = 0f;
            yield return ResultTween.Fade(group, 1f, 0.3f);
            yield return ResultTween.Wait(0.25f);

            for (int i = 0; i < path.Count; i++)
            {
                RankProgressSegment segment = path[i];
                yield return AnimateSegment(segment);
                RankProgressSegment next = i + 1 < path.Count ? path[i + 1] : null;
                if (next == null) break;
                if (segment.TransitionAfter == RankTransition.Promotion)
                    yield return Promote(next);
                else if (segment.TransitionAfter == RankTransition.Demotion)
                    yield return Demote(next);
            }

            yield return ResultTween.Wait(0.2f);
            exitGroup.alpha = 0f;
            exitGroup.blocksRaycasts = true;
            yield return ResultTween.Fade(exitGroup, 1f, 0.4f);
            exitGroup.interactable = true;
            playRoutine = null;
        }

        private IEnumerator AnimateSegment(RankProgressSegment segment)
        {
            if (segment.FromPoints == segment.ToPoints)
            {
                yield return ResultTween.Wait(0.15f);
                yield break;
            }

            if (segment.IsMaxTier)
            {
                yield return ResultTween.Run(1.1f, t => maxPoints.text =
                    Mathf.RoundToInt(Mathf.Lerp(segment.FromPoints, segment.ToPoints, t))
                        .ToString());
                yield break;
            }

            float from = segment.FromFill;
            float to = segment.ToFill;
            float duration = Mathf.Clamp(0.5f + 1.4f * Mathf.Abs(to - from), 0.5f, 1.6f);
            bool gain = !segment.IsLoss;
            tipGlow.gameObject.SetActive(gain);
            yield return ResultTween.Run(duration, t =>
            {
                fill.fillAmount = Mathf.Lerp(from, to, t);
                int points = Mathf.RoundToInt(
                    Mathf.Lerp(segment.FromPoints, segment.ToPoints, t));
                pointsText.text = FormatProgress(points, segment.Cap);
                if (gain) PlaceTipGlow(fill.fillAmount, 0.55f + 0.45f * Mathf.Sin(t * 20f));
            }, ResultTween.EaseInOutSine);

            if (gain && to >= 1f)
            {
                // The bar reached the threshold: flash its tip before the promotion.
                yield return ResultTween.Run(0.3f,
                    t => PlaceTipGlow(1f, Mathf.Lerp(1f, 1.8f, t)), ResultTween.Linear);
            }
            tipGlow.gameObject.SetActive(false);
        }

        private IEnumerator Promote(RankProgressSegment next)
        {
            // The next-tier icon (or the bar's end at the top tier) flies to the centre.
            Image source = nextIcon;
            promoIconImage.sprite = IconOf(next.Tier);
            promoIcon.gameObject.SetActive(true);
            promoIcon.position = source.rectTransform.position;
            float baseSize = source.rectTransform.rect.width;
            promoIcon.sizeDelta = Vector2.one * baseSize;
            promoIcon.localScale = Vector3.one;
            source.enabled = false;

            Vector3 start = promoIcon.localPosition;
            yield return ResultTween.Run(0.55f, t =>
            {
                barGroup.alpha = 1f - t;
                promoIcon.localPosition = Vector3.Lerp(start, Vector3.zero, t);
                promoIcon.localScale = Vector3.one * Mathf.Lerp(1f, PromotionScale, t);
            }, ResultTween.EaseInOutSine);

            rays.gameObject.SetActive(true);
            glow.gameObject.SetActive(true);
            raysSpinning = true;
            backSparkles.Burst(26);
            frontSparkles.Burst(14);
            frontSparkles.Emit(16f, 1.0f);
            yield return ResultTween.Run(1.3f, t =>
            {
                float pulse = Mathf.Sin(Mathf.Min(1f, t * 2.2f) * Mathf.PI);
                promoIcon.localScale = Vector3.one * PromotionScale * (1f + 0.1f * pulse);
                float light = t < 0.25f ? t / 0.25f : 1f;
                SetAlpha(rays, 0.85f * light);
                SetAlpha(glow, light * (0.75f + 0.25f * Mathf.Sin(t * 12f)));
                glow.rectTransform.localScale = Vector3.one * (0.9f + 0.2f * light);
            }, ResultTween.Linear);

            // Settle into the next layout: the icon becomes the new current tier.
            ApplyLayout(next);
            barGroup.alpha = 0f;
            maxGroup.alpha = 0f;
            Image target = next.IsMaxTier ? maxIcon : currentIcon;
            target.enabled = false;
            Canvas.ForceUpdateCanvases();
            if (next.IsMaxTier)
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)maxGroup.transform);
            Vector3 end = promoIcon.parent.InverseTransformPoint(target.rectTransform.position);
            float endScale = target.rectTransform.rect.width / baseSize;
            Vector3 from = promoIcon.localPosition;
            float fromScale = promoIcon.localScale.x;
            yield return ResultTween.Run(0.5f, t =>
            {
                promoIcon.localPosition = Vector3.Lerp(from, end, t);
                promoIcon.localScale = Vector3.one * Mathf.Lerp(fromScale, endScale, t);
                SetAlpha(rays, 0.85f * (1f - t));
                SetAlpha(glow, 1f - t);
            }, ResultTween.EaseInOutSine);

            target.enabled = true;
            nextIcon.enabled = true;
            ResetPromotion();
            CanvasGroup layout = next.IsMaxTier ? maxGroup : barGroup;
            yield return ResultTween.Fade(layout, 1f, 0.35f);
        }

        private IEnumerator Demote(RankProgressSegment next)
        {
            yield return ResultTween.Run(0.4f, t =>
            {
                body.anchoredPosition = new Vector2(0f, -DemotionDrift * t);
                bodyGroup.alpha = 1f - t;
            }, ResultTween.EaseInOutSine);

            ApplyLayout(next);
            yield return ResultTween.Run(0.45f, t =>
            {
                body.anchoredPosition = new Vector2(0f, -DemotionDrift * (1f - t));
                bodyGroup.alpha = t;
            }, ResultTween.EaseOutCubic);
            yield return ResultTween.Wait(0.15f);
        }

        #endregion

        #region Helpers

        private void ApplyLayout(RankProgressSegment segment)
        {
            bool max = segment.IsMaxTier;
            barGroup.alpha = max ? 0f : 1f;
            maxGroup.alpha = max ? 1f : 0f;
            barGroup.gameObject.SetActive(!max);
            maxGroup.gameObject.SetActive(max);
            if (max)
            {
                maxIcon.sprite = IconOf(segment.Tier);
                maxPoints.text = segment.FromPoints.ToString();
                return;
            }

            currentIcon.sprite = IconOf(segment.Tier);
            nextIcon.sprite = IconOf((RankTier)((int)segment.Tier + 1));
            fill.fillAmount = segment.FromFill;
            backdrop.fillAmount = segment.BackdropFill;
            backdrop.gameObject.SetActive(segment.IsLoss);
            tipGlow.gameObject.SetActive(false);
            pointsText.text = FormatProgress(segment.FromPoints, segment.Cap);
        }

        private void ResetPromotion()
        {
            raysSpinning = false;
            promoIcon.gameObject.SetActive(false);
            rays.gameObject.SetActive(false);
            glow.gameObject.SetActive(false);
            backSparkles.Clear();
            frontSparkles.Clear();
        }

        private void PlaceTipGlow(float amount, float scale)
        {
            float width = barArea.rect.width;
            tipGlow.rectTransform.anchoredPosition = new Vector2(width * (amount - 0.5f), 0f);
            tipGlow.rectTransform.localScale = Vector3.one * scale;
        }

        private Sprite IconOf(RankTier tier)
        {
            return catalog != null ? catalog.IconOf(tier) : null;
        }

        private static string FormatProgress(int points, int cap)
        {
            return points + "/" + cap;
        }

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            Color color = graphic.color;
            color.a = Mathf.Clamp01(alpha);
            graphic.color = color;
        }

        private void OnExitClicked()
        {
            if (exitButton != null) exitButton.interactable = false;
            ExitRequested?.Invoke();
        }

        #endregion
    }
}
