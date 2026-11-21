using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Overlay shown while the backend saves the match result, modelled on the
    /// LoginStateChecking screen. The skip button leaves for the main menu at any time;
    /// the result is still recorded by the server (or the listen host's detached report).
    /// </summary>
    public sealed class MatchResultLoadingView : MonoBehaviour
    {
        #region Constants

        public const string LoadingMessage = "Đang tải kết quả trận...";
        public const string DelayedMessage = "Kết quả sẽ được cập nhật sau";

        #endregion

        #region Serialized Fields

        [SerializeField] private CanvasGroup group;
        [SerializeField] private GameObject spinner;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Button skipButton;

        #endregion

        #region Events

        public event Action SkipRequested;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (skipButton != null) skipButton.onClick.AddListener(OnSkipClicked);
        }

        private void OnDestroy()
        {
            if (skipButton != null) skipButton.onClick.RemoveListener(OnSkipClicked);
        }

        #endregion

        #region Public API

        public void Show()
        {
            gameObject.SetActive(true);
            if (spinner != null) spinner.SetActive(true);
            if (label != null) label.text = LoadingMessage;
            if (skipButton != null) skipButton.interactable = true;
            ResultTween.SetVisible(group, true);
        }

        /// <summary>Stops the spinner after the client wait limit; only skip remains.</summary>
        public void ShowDelayed()
        {
            if (spinner != null) spinner.SetActive(false);
            if (label != null) label.text = DelayedMessage;
        }

        public void Hide()
        {
            ResultTween.SetVisible(group, false);
            gameObject.SetActive(false);
        }

        #endregion

        #region Helpers

        private void OnSkipClicked()
        {
            if (skipButton != null) skipButton.interactable = false;
            SkipRequested?.Invoke();
        }

        #endregion
    }
}
