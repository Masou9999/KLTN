using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasGroup))]
    /// <summary>
    /// Runs the local lethal-highlight, procedural crack and shard animation while the
    /// authoritative card view is deliberately retained for combat presentation.
    /// </summary>
    public sealed class CardDeathFeedback : MonoBehaviour
    {
        #region Serialized Fields

        [Header("References")]
        [SerializeField]
        private RectTransform visualRoot;

        [SerializeField]
        private Image purpleHighlight;

        [SerializeField]
        private CardCrackGraphic crackOverlay;

        [SerializeField]
        private CanvasGroup cardCanvasGroup;

        [Header("Highlight")]
        [SerializeField]
        private Color highlightColor = new Color(0.58f, 0.16f, 1f, 1f);

        [Range(0f, 1f)]
        [SerializeField]
        private float highlightPeakAlpha = 0.72f;

        [Min(0f)]
        [SerializeField]
        private float highlightScaleBoost = 0.05f;

        [Min(0f)]
        [SerializeField]
        private float highlightDuration = 0.22f;

        [Header("Crack")]
        [Min(0f)]
        [SerializeField]
        private float crackDuration = 0.3f;

        [Min(0f)]
        [SerializeField]
        private float crackHoldDuration = 0.12f;

        [Min(0f)]
        [SerializeField]
        private float shakeDistance = 5f;

        [Min(0f)]
        [SerializeField]
        private float shakeRotation = 2.5f;

        [Header("Shatter Grid")]
        [Range(2, 5)]
        [SerializeField]
        private int shardColumns = 3;

        [Range(2, 6)]
        [SerializeField]
        private int shardRows = 4;

        [Header("Shatter Motion")]
        [Min(0f)]
        [SerializeField]
        private float shatterDuration = 0.65f;

        [Min(0f)]
        [SerializeField]
        private float minimumShardDistance = 70f;

        [Min(0f)]
        [SerializeField]
        private float maximumShardDistance = 180f;

        [Min(0f)]
        [SerializeField]
        private float shardGravity = 90f;

        [Min(0f)]
        [SerializeField]
        private float maximumShardDelay = 0.12f;

        [Range(0.1f, 1f)]
        [SerializeField]
        private float minimumEndScale = 0.45f;

        [Range(0.1f, 1f)]
        [SerializeField]
        private float maximumEndScale = 0.75f;

        #endregion

        #region Runtime State

        private readonly List<ShardState> shards = new List<ShardState>();

        private RectTransform shardLayer;
        private Vector2 visualHomePosition;
        private Vector3 visualHomeScale;
        private Quaternion visualHomeRotation;
        private float canvasBaseAlpha;
        private bool canvasBaseInteractable;
        private bool canvasBaseBlocksRaycasts;
        private bool initialized;
        private int lethalSeed;
        private bool lethalPrepared;
        private bool previewCrackPrepared;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Initialize();
            ResetImmediately();
        }

        private void OnDisable()
        {
            if (initialized)
            {
                ResetImmediately();
            }
        }

        #endregion

        #region Public API

        public RectTransform VisualRoot => visualRoot;

        /// <summary>
        /// Shows a reversible crack while the local player has selected a lethal target.
        /// No shard or gameplay state is created until the host resolves the effect.
        /// </summary>
        public void ShowTargetPreviewCrack(string instanceId)
        {
            Initialize();

            if (visualRoot == null || previewCrackPrepared)
            {
                return;
            }

            if (lethalPrepared)
            {
                ResetImmediately();
            }

            CaptureVisualState();

            lethalSeed = StableSeed(instanceId);
            lethalPrepared = true;
            previewCrackPrepared = true;

            PrepareHighlight();
            SetHighlightAlpha(highlightPeakAlpha * 0.45f);
            PrepareCrack(lethalSeed);
            crackOverlay?.SetReveal(1f);
        }

        public void ClearTargetPreviewCrack()
        {
            if (previewCrackPrepared)
            {
                ResetImmediately();
            }
        }

        /// <summary>
        /// Fades in the lethal highlight and reveals the deterministic crack pattern.
        /// </summary>
        public IEnumerator ShowLethalHighlight(string instanceId)
        {
            Initialize();

            if (visualRoot == null || lethalPrepared)
            {
                yield break;
            }

            CaptureVisualState();

            lethalSeed = StableSeed(instanceId);
            lethalPrepared = true;

            if (cardCanvasGroup != null)
            {
                cardCanvasGroup.alpha = 1f;
                cardCanvasGroup.interactable = false;
                cardCanvasGroup.blocksRaycasts = false;
            }

            PrepareHighlight();

            yield return AnimateHighlight();
        }

        /// <summary>
        /// Replaces the retained card image with generated shards, animates them and
        /// optionally emits death dust at the card center.
        /// </summary>
        public IEnumerator PlayShatter(
            string instanceId,
            Action<Vector3> shatterStarted = null
        )
        {
            Initialize();

            if (visualRoot == null)
            {
                yield break;
            }

            if (!lethalPrepared)
            {
                yield return ShowLethalHighlight(instanceId);
            }

            if (previewCrackPrepared)
            {
                previewCrackPrepared = false;
            }
            else
            {
                PrepareCrack(lethalSeed);
                yield return AnimateCrack(lethalSeed);
            }

            BuildShards(lethalSeed);
            Canvas.ForceUpdateCanvases();

            yield return null;

            shatterStarted?.Invoke(GetWorldCenter());
            visualRoot.gameObject.SetActive(false);

            yield return AnimateShards();

            if (cardCanvasGroup != null)
            {
                cardCanvasGroup.alpha = 0f;
            }
        }

        public IEnumerator PlayDeath(
            string instanceId,
            Action<Vector3> shatterStarted = null
        )
        {
            yield return ShowLethalHighlight(instanceId);
            yield return PlayShatter(instanceId, shatterStarted);
        }

        public void ResetImmediately()
        {
            if (!initialized)
            {
                return;
            }

            bool restoreVisual = lethalPrepared || shardLayer != null;
            DestroyShardLayer();

            if (cardCanvasGroup != null)
            {
                cardCanvasGroup.alpha = canvasBaseAlpha;
                cardCanvasGroup.interactable = canvasBaseInteractable;
                cardCanvasGroup.blocksRaycasts = canvasBaseBlocksRaycasts;
            }

            if (visualRoot != null && restoreVisual)
            {
                visualRoot.gameObject.SetActive(true);
                visualRoot.anchoredPosition = visualHomePosition;
                visualRoot.localScale = visualHomeScale;
                visualRoot.localRotation = visualHomeRotation;
            }

            if (purpleHighlight != null)
            {
                SetHighlightAlpha(0f);
                purpleHighlight.gameObject.SetActive(false);
            }

            if (crackOverlay != null)
            {
                crackOverlay.SetReveal(0f);
                crackOverlay.gameObject.SetActive(false);
            }

            lethalSeed = 0;
            lethalPrepared = false;
            previewCrackPrepared = false;
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (cardCanvasGroup == null)
            {
                cardCanvasGroup = GetComponent<CanvasGroup>();
            }

            canvasBaseAlpha = cardCanvasGroup != null ? cardCanvasGroup.alpha : 1f;

            canvasBaseInteractable =
                cardCanvasGroup != null && cardCanvasGroup.interactable;

            canvasBaseBlocksRaycasts =
                cardCanvasGroup != null && cardCanvasGroup.blocksRaycasts;

            CaptureVisualState();
            initialized = true;
        }

        private void CaptureVisualState()
        {
            if (visualRoot == null)
            {
                return;
            }

            visualHomePosition = visualRoot.anchoredPosition;
            visualHomeScale = visualRoot.localScale;
            visualHomeRotation = visualRoot.localRotation;
        }

        #endregion

        #region Anticipation

        private void PrepareHighlight()
        {
            if (purpleHighlight != null)
            {
                purpleHighlight.gameObject.SetActive(true);
                purpleHighlight.raycastTarget = false;
                SetHighlightAlpha(0f);
            }

            if (crackOverlay != null)
            {
                crackOverlay.SetReveal(0f);
                crackOverlay.gameObject.SetActive(false);
            }
        }

        private void PrepareCrack(int seed)
        {
            if (crackOverlay == null)
            {
                return;
            }

            crackOverlay.gameObject.SetActive(true);
            crackOverlay.Configure(seed);
            crackOverlay.SetReveal(0f);
        }

        private IEnumerator AnimateHighlight()
        {
            float elapsed = 0f;

            while (elapsed < highlightDuration)
            {
                elapsed += Time.unscaledDeltaTime;

                float progress = NormalizedProgress(elapsed, highlightDuration);
                float eased = progress * progress * (3f - 2f * progress);
                float scalePulse = Mathf.Sin(progress * Mathf.PI);

                SetHighlightAlpha(highlightPeakAlpha * eased);

                visualRoot.localScale =
                    visualHomeScale * (1f + highlightScaleBoost * scalePulse);

                yield return null;
            }

            SetHighlightAlpha(highlightPeakAlpha);
            visualRoot.localScale = visualHomeScale;
        }

        private IEnumerator AnimateCrack(int seed)
        {
            float phase = (seed & 1023) * 0.017f;
            float elapsed = 0f;

            while (elapsed < crackDuration)
            {
                elapsed += Time.unscaledDeltaTime;

                float progress = NormalizedProgress(elapsed, crackDuration);
                float shakeWeight = Mathf.Sin(progress * Mathf.PI);

                Vector2 shakeOffset =
                    new Vector2(
                        Mathf.Sin(progress * 42f + phase),
                        Mathf.Cos(progress * 37f + phase)
                    )
                    * shakeDistance
                    * shakeWeight;

                float rotation =
                    Mathf.Sin(progress * 47f + phase) * shakeRotation * shakeWeight;

                visualRoot.anchoredPosition = visualHomePosition + shakeOffset;

                visualRoot.localRotation =
                    visualHomeRotation * Quaternion.Euler(0f, 0f, rotation);

                visualRoot.localScale =
                    visualHomeScale * (1f + highlightScaleBoost * shakeWeight);

                SetHighlightAlpha(
                    Mathf.Lerp(highlightPeakAlpha, highlightPeakAlpha * 0.4f, progress)
                );

                if (crackOverlay != null)
                {
                    crackOverlay.SetReveal(progress);
                }

                yield return null;
            }

            visualRoot.anchoredPosition = visualHomePosition;
            visualRoot.localScale = visualHomeScale;
            visualRoot.localRotation = visualHomeRotation;

            if (crackOverlay != null)
            {
                crackOverlay.SetReveal(1f);
            }

            yield return WaitUnscaled(crackHoldDuration);
        }

        #endregion

        #region Shatter Construction

        private void BuildShards(int seed)
        {
            DestroyShardLayer();

            RectTransform cardRoot = transform as RectTransform;

            if (cardRoot == null || visualRoot == null)
            {
                return;
            }

            var layerObject = new GameObject("DeathShardLayer", typeof(RectTransform));

            layerObject.layer = gameObject.layer;

            shardLayer = layerObject.GetComponent<RectTransform>();

            shardLayer.SetParent(transform, false);
            shardLayer.anchorMin = new Vector2(0.5f, 0.5f);
            shardLayer.anchorMax = new Vector2(0.5f, 0.5f);
            shardLayer.pivot = new Vector2(0.5f, 0.5f);
            shardLayer.anchoredPosition = Vector2.zero;
            shardLayer.sizeDelta = cardRoot.rect.size;
            shardLayer.SetAsLastSibling();

            Vector2 renderedSize = new Vector2(
                visualRoot.rect.width * Mathf.Abs(visualRoot.localScale.x),
                visualRoot.rect.height * Mathf.Abs(visualRoot.localScale.y)
            );

            float shardWidth = renderedSize.x / shardColumns;

            float shardHeight = renderedSize.y / shardRows;

            var random = new System.Random(seed);

            for (int row = 0; row < shardRows; row++)
            {
                for (int column = 0; column < shardColumns; column++)
                {
                    Vector2 center = new Vector2(
                        -renderedSize.x * 0.5f + shardWidth * (column + 0.5f),
                        -renderedSize.y * 0.5f + shardHeight * (row + 0.5f)
                    );

                    CreateShard(
                        random,
                        center,
                        new Vector2(shardWidth + 1f, shardHeight + 1f)
                    );
                }
            }
        }

        private void CreateShard(System.Random random, Vector2 center, Vector2 size)
        {
            var shardObject = new GameObject(
                $"Shard_{shards.Count}",
                typeof(RectTransform),
                typeof(RectMask2D),
                typeof(CanvasGroup)
            );

            shardObject.layer = gameObject.layer;

            RectTransform shardRect = shardObject.GetComponent<RectTransform>();

            shardRect.SetParent(shardLayer, false);
            shardRect.anchorMin = new Vector2(0.5f, 0.5f);
            shardRect.anchorMax = new Vector2(0.5f, 0.5f);
            shardRect.pivot = new Vector2(0.5f, 0.5f);
            shardRect.anchoredPosition = center;
            shardRect.sizeDelta = size;

            CanvasGroup shardCanvas = shardObject.GetComponent<CanvasGroup>();

            shardCanvas.interactable = false;
            shardCanvas.blocksRaycasts = false;

            GameObject visualClone = Instantiate(visualRoot.gameObject, shardRect, false);

            visualClone.name = "ShardVisual";

            RectTransform cloneRect = visualClone.GetComponent<RectTransform>();

            cloneRect.anchorMin = new Vector2(0.5f, 0.5f);
            cloneRect.anchorMax = new Vector2(0.5f, 0.5f);
            cloneRect.pivot = new Vector2(0.5f, 0.5f);
            cloneRect.sizeDelta = visualRoot.rect.size;
            cloneRect.localScale = visualRoot.localScale;
            cloneRect.localRotation = visualRoot.localRotation;
            cloneRect.anchoredPosition = -center;

            Vector2 radialDirection =
                center.sqrMagnitude > 0.001f
                    ? center.normalized
                    : RandomDirection(random);

            radialDirection = Rotate(radialDirection, RandomRange(random, -35f, 35f));

            float distance = RandomRange(
                random,
                minimumShardDistance,
                maximumShardDistance
            );

            Vector2 target = center + radialDirection * distance;

            shards.Add(
                new ShardState
                {
                    RectTransform = shardRect,
                    CanvasGroup = shardCanvas,
                    StartPosition = center,
                    TargetPosition = target,
                    TargetRotation = RandomRange(random, -100f, 100f),
                    Delay = RandomRange(random, 0f, maximumShardDelay),
                    EndScale = RandomRange(random, minimumEndScale, maximumEndScale),
                }
            );
        }

        #endregion

        #region Shatter Animation

        private IEnumerator AnimateShards()
        {
            if (shards.Count == 0)
            {
                yield break;
            }

            if (shatterDuration <= 0f)
            {
                HideAllShards();
                yield break;
            }

            float elapsed = 0f;
            float totalDuration = shatterDuration + maximumShardDelay;

            while (elapsed < totalDuration)
            {
                elapsed += Time.unscaledDeltaTime;

                for (int i = 0; i < shards.Count; i++)
                {
                    UpdateShard(shards[i], elapsed);
                }

                yield return null;
            }

            HideAllShards();
        }

        private void UpdateShard(ShardState shard, float elapsed)
        {
            float localTime = elapsed - shard.Delay;

            if (localTime <= 0f)
            {
                shard.CanvasGroup.alpha = 1f;
                return;
            }

            float progress = Mathf.Clamp01(localTime / shatterDuration);

            float eased = 1f - Mathf.Pow(1f - progress, 3f);

            Vector2 gravityOffset = Vector2.down * shardGravity * progress * progress;

            shard.RectTransform.anchoredPosition =
                Vector2.LerpUnclamped(shard.StartPosition, shard.TargetPosition, eased)
                + gravityOffset;

            shard.RectTransform.localRotation = Quaternion.Euler(
                0f,
                0f,
                shard.TargetRotation * eased
            );

            float scale = Mathf.Lerp(1f, shard.EndScale, eased);

            shard.RectTransform.localScale = new Vector3(scale, scale, 1f);

            float fadeProgress = Mathf.InverseLerp(0.25f, 1f, progress);

            shard.CanvasGroup.alpha = 1f - fadeProgress;
        }

        private void HideAllShards()
        {
            for (int i = 0; i < shards.Count; i++)
            {
                if (shards[i].CanvasGroup != null)
                {
                    shards[i].CanvasGroup.alpha = 0f;
                }
            }
        }

        #endregion

        #region Cleanup

        private void DestroyShardLayer()
        {
            shards.Clear();

            if (shardLayer == null)
            {
                return;
            }

            shardLayer.gameObject.SetActive(false);
            Destroy(shardLayer.gameObject);
            shardLayer = null;
        }

        private void SetHighlightAlpha(float alpha)
        {
            if (purpleHighlight == null)
            {
                return;
            }

            Color color = highlightColor;
            color.a = Mathf.Clamp01(alpha);
            purpleHighlight.color = color;
        }

        #endregion

        #region Helpers

        private static int StableSeed(string value)
        {
            unchecked
            {
                int hash = 17;

                if (string.IsNullOrEmpty(value))
                {
                    return hash;
                }

                for (int i = 0; i < value.Length; i++)
                {
                    hash = hash * 31 + value[i];
                }

                return hash;
            }
        }

        private Vector3 GetWorldCenter()
        {
            RectTransform cardRect = transform as RectTransform;

            return cardRect != null
                ? cardRect.TransformPoint(cardRect.rect.center)
                : transform.position;
        }

        private static Vector2 RandomDirection(System.Random random)
        {
            float angle = RandomRange(random, 0f, Mathf.PI * 2f);

            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private static Vector2 Rotate(Vector2 vector, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);

            return new Vector2(
                vector.x * cosine - vector.y * sine,
                vector.x * sine + vector.y * cosine
            );
        }

        private static float RandomRange(
            System.Random random,
            float minimum,
            float maximum
        )
        {
            return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
        }

        private static float NormalizedProgress(float elapsed, float duration)
        {
            if (duration <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01(elapsed / duration);
        }

        private static IEnumerator WaitUnscaled(float duration)
        {
            if (duration <= 0f)
            {
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        #endregion

        private sealed class ShardState
        {
            public RectTransform RectTransform;
            public CanvasGroup CanvasGroup;
            public Vector2 StartPosition;
            public Vector2 TargetPosition;
            public float TargetRotation;
            public float Delay;
            public float EndScale;
        }
    }
}
