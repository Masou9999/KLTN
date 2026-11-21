using System;
using System.Collections;
using KLTN.Game.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// One banner artwork with its own reward slots. Each banner keeps its own layout
    /// so slot positions can follow that artwork independently.
    /// </summary>
    [Serializable]
    public sealed class MatchResultBannerLayout
    {
        public GameObject root;
        public TMP_Text coinsValue;
        public TMP_Text silverValue;
        public TMP_Text pointsValue;
    }

    /// <summary>
    /// Stage 1 of the result screen: the victory/defeat/draw banner with the three
    /// reward slots (coins, silver, rank points) laid over it and one Continue button.
    /// </summary>
    public sealed class MatchResultBannerView : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform content;
        [SerializeField] private MatchResultBannerLayout victoryLayout;
        [SerializeField] private MatchResultBannerLayout defeatLayout;
        [SerializeField] private MatchResultBannerLayout drawLayout;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button continueButton;

        #endregion

        #region Runtime State

        private Vector3 contentScale = Vector3.one;
        private Coroutine showRoutine;

        #endregion

        #region Events

        public event Action ContinueRequested;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (content != null) contentScale = content.localScale;
            if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
        }

        private void OnDestroy()
        {
            if (continueButton != null) continueButton.onClick.RemoveListener(OnContinueClicked);
        }

        #endregion

        #region Public API

        public void Show(MatchResultBanner kind, MatchResultRewards rewards)
        {
            SetActive(victoryLayout, kind == MatchResultBanner.Victory);
            SetActive(drawLayout, kind == MatchResultBanner.Draw);
            SetActive(defeatLayout,
                kind != MatchResultBanner.Victory && kind != MatchResultBanner.Draw);
            UpdateRewards(rewards);

            gameObject.SetActive(true);
            if (showRoutine != null) StopCoroutine(showRoutine);
            showRoutine = StartCoroutine(AnimateIn());
        }

        /// <summary>Refreshes slot text without replaying the entrance.</summary>
        public void UpdateRewards(MatchResultRewards rewards)
        {
            foreach (MatchResultBannerLayout layout in
                     new[] { victoryLayout, defeatLayout, drawLayout })
            {
                if (layout == null) continue;
                SetText(layout.coinsValue, rewards.Coins);
                SetText(layout.silverValue, rewards.Silver);
                SetText(layout.pointsValue, rewards.Points);
            }
            if (statusText != null)
            {
                statusText.text = rewards.Status ?? string.Empty;
                statusText.gameObject.SetActive(!string.IsNullOrEmpty(rewards.Status));
            }
        }

        public IEnumerator Hide(float duration)
        {
            if (showRoutine != null) StopCoroutine(showRoutine);
            if (group != null) group.interactable = false;
            yield return ResultTween.Fade(group, 0f, duration);
            ResultTween.SetVisible(group, false);
            gameObject.SetActive(false);
        }

        #endregion

        #region Helpers

        private IEnumerator AnimateIn()
        {
            ResultTween.SetVisible(group, true);
            if (group != null) group.alpha = 0f;
            if (continueButton != null) continueButton.interactable = false;
            yield return ResultTween.Run(0.45f, t =>
            {
                if (group != null) group.alpha = Mathf.Clamp01(t * 1.6f);
                if (content != null)
                    content.localScale = contentScale * Mathf.LerpUnclamped(0.85f, 1f, t);
            }, ResultTween.EaseOutBack);
            if (continueButton != null) continueButton.interactable = true;
            showRoutine = null;
        }

        private void OnContinueClicked()
        {
            if (continueButton != null) continueButton.interactable = false;
            ContinueRequested?.Invoke();
        }

        private static void SetActive(MatchResultBannerLayout layout, bool active)
        {
            if (layout?.root != null) layout.root.SetActive(active);
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        #endregion
    }
}
