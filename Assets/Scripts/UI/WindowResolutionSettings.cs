using UnityEngine;

namespace KLTN.UI
{
    /// <summary>Stores fixed 16:9 window sizes locally on this device.</summary>
    public static class WindowResolutionSettings
    {
        #region Configuration and Queries

        public const int Count = 3;
        private const string PreferenceKey = "Display.WindowResolution.v1";

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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR && !UNITY_SERVER
            if (Application.isBatchMode) return;
            int index = SavedIndex;
            while (index > 0 && !FitsDisplay(index)) index--;
            Save(index);
#endif
        }

        /// <summary>Saves locally and applies only to desktop clients.</summary>
        public static void Save(int index)
        {
            index = Mathf.Clamp(index, 0, Count - 1);
            PlayerPrefs.SetInt(PreferenceKey, index);
            PlayerPrefs.Save();
#if UNITY_STANDALONE && !UNITY_EDITOR && !UNITY_SERVER
            if (Application.isBatchMode) return;
            var size = Size(index);
            Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
#endif
        }

        #endregion
    }
}
