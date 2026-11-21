using System;
using UnityEngine;

namespace KLTN.UI
{
    /// <summary>Stores fixed 16:9 window sizes locally on this device.</summary>
    public static class WindowResolutionSettings
    {
        #region Configuration and Queries

        public const int Count = 3;
        private const string PreferenceKey = "Display.WindowResolution.v1";
        private const string FullscreenKey = "Display.Fullscreen.v1";

        public static event Action FullscreenChanged;
        public static bool SavedFullscreen => PlayerPrefs.GetInt(FullscreenKey, 0) == 1;

        public static int SavedIndex => Mathf.Clamp(
            PlayerPrefs.GetInt(PreferenceKey, 1), 0, Count - 1);

        public static Vector2Int Size(int index)
        {
            int width = 960 + Mathf.Clamp(index, 0, Count - 1) * 320;
            return new Vector2Int(width, width * 9 / 16);
        }

        /// <summary>Reserves space for desktop window borders and the taskbar.</summary>
        public static bool FitsDisplay(int index)
        {
#if UNITY_STANDALONE && !UNITY_EDITOR && !UNITY_SERVER
            var size = Size(index);
            var display = Screen.currentResolution;
            return size.x <= display.width - 32 && size.y <= display.height - 80;
#else
            return true;
#endif
        }

        #endregion

        #region Startup and Persistence

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEvents()
        {
            FullscreenChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR && !UNITY_SERVER
            if (Application.isBatchMode) return;
            Apply(SavedIndex, SavedFullscreen);
            var shortcuts = new GameObject("DisplayShortcuts");
            UnityEngine.Object.DontDestroyOnLoad(shortcuts);
            shortcuts.AddComponent<FullscreenShortcut>();
#endif
        }

        /// <summary>Saves locally and applies only to desktop clients.</summary>
        public static void Save(int index)
        {
            Save(index, SavedFullscreen);
        }

        /// <summary>Commits both modal display selections without touching audio.</summary>
        public static void Save(int index, bool fullscreen)
        {
            index = Mathf.Clamp(index, 0, Count - 1);
            PlayerPrefs.SetInt(PreferenceKey, index);
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            PlayerPrefs.Save();
            Apply(index, fullscreen);
            FullscreenChanged?.Invoke();
        }

        /// <summary>
        /// Toggles the last requested mode, not Screen's delayed end-of-frame state.
        /// Saves only fullscreen; modal drafts and the preferred window size survive.
        /// </summary>
        public static void ToggleFullscreenAndSave()
        {
            bool fullscreen = !SavedFullscreen;
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            PlayerPrefs.Save();
            Apply(SavedIndex, fullscreen);
            FullscreenChanged?.Invoke();
        }

        private static void Apply(int index, bool fullscreen)
        {
#if UNITY_STANDALONE && !UNITY_EDITOR && !UNITY_SERVER
            if (Application.isBatchMode) return;
            // Fit the window without replacing the user's preferred resolution.
            while (!fullscreen && index > 0 && !FitsDisplay(index)) index--;
            var size = Size(index);
            Screen.SetResolution(size.x, size.y, fullscreen
                ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
#endif
        }

        #endregion
    }
}
