using System;
using System.Threading.Tasks;
using KLTN.Infrastructure.Audio;
using UnityEngine;

namespace KLTN.Audio
{
    /// <summary>
    /// Shares live previews across scenes, with explicit cloud saves per account.
    /// Only confirmed account settings and guest settings are cached on this device.
    /// </summary>
    public static class AudioVolumeSettings
    {
        #region Constants

        public const string MusicVolumeKey = "MusicVolume";
        public const string SfxVolumeKey = "SFXVolume";
        public const string UiSoundVolumeKey = "UiSoundVolume";
        public const string MusicMutedKey = "MusicMuted";
        public const string UiSoundMutedKey = "UiSoundMuted";
        public const float DefaultVolume = 0.75f;

        #endregion

        #region Events and Properties

        public static event Action Changed;

        private static AudioPreferences current = new AudioPreferences();
        private static AudioPreferences saved = new AudioPreferences();
        private static string userId;
        private static int session;
        private static int savedRevision;

        /// <summary>Captures the opening state without exposing a mutable snapshot.</summary>
        public sealed class PreviewCheckpoint
        {
            internal AudioPreferences value;
            internal int generation;
            internal int revision;
        }
        public static bool IsSaving { get; private set; }
        public static string Status { get; private set; }
        public static bool HasChanges => !current.SameAs(saved);
        public static bool CanSave => userId != null && HasChanges && !IsSaving;
        public static float MusicVolume => current.music / 100f;
        public static float SfxVolume => current.combat / 100f;
        public static float UiSoundVolume => current.ui / 100f;
        public static bool MusicMuted => current.musicMuted;
        public static bool UiSoundMuted => current.uiMuted;

        #endregion

        #region Static Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Changed = null;
            userId = null;
            session++;
            IsSaving = false;
            Status = null;
            current = ReadCache("guest");
            saved = current.Copy();
        }

        #endregion

        #region Public Commands

        /// <summary>Loads only this account's confirmed cache, then its cloud settings.</summary>
        public static async Task<bool> LoadAccountAsync(string uid)
        {
            int generation = ++session;
            userId = uid;
            IsSaving = false;
            Status = null;
            current = ReadCache(uid);
            saved = current.Copy();
            Changed?.Invoke();
            try
            {
                var gateway = new FirestoreAudioPreferencesGateway();
                AudioPreferences loaded = await gateway.LoadAsync(uid);
                if (generation != session) return false;
                current = loaded ?? new AudioPreferences();
                saved = current.Copy();
                WriteCache(uid, saved);
            }
            catch (Exception)
            {
                if (generation != session) return false;
                Status = "Không tải được cài đặt; đang dùng bản lưu trên máy.";
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Discards account previews and restores the independent login settings.</summary>
        public static void UseGuest()
        {
            session++;
            userId = null;
            IsSaving = false;
            Status = null;
            current = ReadCache("guest");
            saved = current.Copy();
            Changed?.Invoke();
        }

        /// <summary>Starts a modal preview, preserving any earlier quick-mute changes.</summary>
        public static PreviewCheckpoint CapturePreview()
        {
            return new PreviewCheckpoint
            {
                value = current.Copy(), generation = session, revision = savedRevision,
            };
        }

        /// <summary>
        /// Cancels modal edits back to opening state, or the latest successful save.
        /// Ignores checkpoints from another login session; never cancels an in-flight write.
        /// </summary>
        public static void RestorePreview(PreviewCheckpoint checkpoint)
        {
            if (checkpoint == null || checkpoint.generation != session || IsSaving)
            {
                return;
            }
            current = checkpoint.revision == savedRevision
                ? checkpoint.value.Copy() : saved.Copy();
            NotifyPreview();
        }

        /// <summary>Saves a captured snapshot; late replies never change another account.</summary>
        public static async Task SaveAsync()
        {
            if (!CanSave) return;
            int generation = session;
            string uid = userId;
            AudioPreferences snapshot = current.Copy();
            IsSaving = true;
            Status = null;
            Changed?.Invoke();
            try
            {
                await new FirestoreAudioPreferencesGateway().SaveAsync(uid, snapshot);
                if (generation != session) return;
                saved = snapshot;
                savedRevision++;
                WriteCache(uid, saved);
                Status = "Đã lưu.";
            }
            catch (Exception)
            {
                if (generation != session) return;
                Status = "Lưu thất bại. Vui lòng thử lại.";
            }
            finally
            {
                if (generation == session)
                {
                    IsSaving = false;
                    Changed?.Invoke();
                }
            }
        }

        public static void SetMusicVolume(float volume)
        {
            current.music = Snap(volume);
            current.musicMuted = current.music == 0;
            NotifyPreview();
        }

        public static void SetMusicMuted(bool muted)
        {
            if (!muted && current.music == 0) current.music = 75;
            current.musicMuted = muted;
            NotifyPreview();
        }

        public static void RepairLegacyMusicVolume()
        {
            // Global legacy keys have no owner; never import them into an account.
        }

        public static void SetUiSoundMuted(bool muted)
        {
            if (!muted && current.ui == 0) current.ui = 75;
            current.uiMuted = muted;
            NotifyPreview();
        }

        public static void SetUiSoundVolume(float volume)
        {
            current.ui = Snap(volume);
            current.uiMuted = current.ui == 0;
            NotifyPreview();
        }

        public static void SetSfxVolume(float volume)
        {
            current.combat = Snap(volume);
            NotifyPreview();
        }

        #endregion

        #region Helpers

        /// <summary>Rounds to tens, with 75 percent as an additional default stop.</summary>
        public static int Snap(float volume)
        {
            float percent = Mathf.Clamp01(volume) * 100f;
            int tens = Mathf.RoundToInt(percent / 10f) * 10;
            return Mathf.Abs(percent - 75) < Mathf.Abs(percent - tens) ? 75 : tens;
        }

        private static void NotifyPreview()
        {
            Status = null;
            if (userId == null)
            {
                WriteCache("guest", current);
                saved = current.Copy();
            }
            Changed?.Invoke();
        }

        private static AudioPreferences ReadCache(string uid)
        {
            try
            {
                string json = PlayerPrefs.GetString("Audio.v2." + uid, "");
                var value = JsonUtility.FromJson<AudioPreferences>(json);
                return value != null && value.IsValid() ? value : new AudioPreferences();
            }
            catch (Exception) { return new AudioPreferences(); }
        }

        private static void WriteCache(string uid, AudioPreferences value)
        {
            PlayerPrefs.SetString("Audio.v2." + uid, JsonUtility.ToJson(value));
            PlayerPrefs.Save();
        }

        #endregion
    }
}
