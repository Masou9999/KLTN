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
        public const string UiSoundVolumeKey = "UiSoundVolume";
        public const string MusicMutedKey = "MusicMuted";
        public const string UiSoundMutedKey = "UiSoundMuted";
        public const float DefaultVolume = 0.7f;

        #endregion

        #region Events and Properties

        public static event Action Changed;

        public static float MusicVolume => PlayerPrefs.GetFloat(MusicVolumeKey, DefaultVolume);
        public static float SfxVolume => PlayerPrefs.GetFloat(SfxVolumeKey, DefaultVolume);
        public static float UiSoundVolume =>
            PlayerPrefs.GetFloat(UiSoundVolumeKey, DefaultVolume);
        public static bool MusicMuted => PlayerPrefs.GetInt(MusicMutedKey, 0) != 0;
        public static bool UiSoundMuted => PlayerPrefs.GetInt(UiSoundMutedKey, 0) != 0;

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
            // A zero slider value is also a mute state, so the icon stays truthful.
            PlayerPrefs.SetInt(MusicMutedKey, volume <= 0f ? 1 : 0);
            Save(MusicVolumeKey, volume);
        }

        public static void SetMusicMuted(bool muted)
        {
            if (!muted && MusicVolume <= 0f)
            {
                PlayerPrefs.SetFloat(MusicVolumeKey, DefaultVolume);
            }
            SaveMute(MusicMutedKey, muted);
        }

        /// <summary>
        /// Repairs legacy zero-volume preferences that claim music is unmuted.
        /// Explicit mute and zero volume saved by the current slider are preserved.
        /// </summary>
        public static void RepairLegacyMusicVolume()
        {
            if (!MusicMuted && MusicVolume <= 0f)
            {
                SetMusicVolume(DefaultVolume);
            }
        }

        public static void SetUiSoundMuted(bool muted)
        {
            if (!muted && UiSoundVolume <= 0f)
            {
                PlayerPrefs.SetFloat(UiSoundVolumeKey, DefaultVolume);
            }
            SaveMute(UiSoundMutedKey, muted);
        }

        public static void SetUiSoundVolume(float volume)
        {
            PlayerPrefs.SetInt(UiSoundMutedKey, volume <= 0f ? 1 : 0);
            Save(UiSoundVolumeKey, volume);
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

        private static void SaveMute(string key, bool muted)
        {
            PlayerPrefs.SetInt(key, muted ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        #endregion
    }
}
