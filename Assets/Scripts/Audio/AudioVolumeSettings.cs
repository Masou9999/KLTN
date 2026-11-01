using System;
using UnityEngine;

namespace KLTN.Audio
{
    /// <summary>
    /// Persisted Music/SFX volume (FR-SET-01/02), shared by every scene.
    /// Setters save to PlayerPrefs immediately and raise <see cref="Changed"/> so
    /// long-lived sources (UI click, menu music) follow the sliders without a reload.
    /// </summary>
    public static class AudioVolumeSettings
    {
        #region Constants

        public const string MusicVolumeKey = "MusicVolume";
        public const string SfxVolumeKey = "SFXVolume";
        public const float DefaultVolume = 0.7f;

        #endregion

        #region Events and Properties

        public static event Action Changed;

        public static float MusicVolume => PlayerPrefs.GetFloat(MusicVolumeKey, DefaultVolume);
        public static float SfxVolume => PlayerPrefs.GetFloat(SfxVolumeKey, DefaultVolume);

        #endregion

        #region Static Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Changed = null;
        }

        #endregion

        #region Public Commands

        public static void SetMusicVolume(float volume)
        {
            Save(MusicVolumeKey, volume);
        }

        public static void SetSfxVolume(float volume)
        {
            Save(SfxVolumeKey, volume);
        }

        #endregion

        #region Helpers

        private static void Save(string key, float volume)
        {
            PlayerPrefs.SetFloat(key, Mathf.Clamp01(volume));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        #endregion
    }
}
