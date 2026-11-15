using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using KLTN.Audio;
using KLTN.Infrastructure.Audio;

class Program
{
    static int checks;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    static async Task Main()
    {
        await AudioVolumeSettings.LoadAccountAsync("A");
        Check(AudioVolumeSettings.MusicVolume == .75f && !AudioVolumeSettings.HasChanges,
            "New account defaults to 75 and save disabled");
        AudioVolumeSettings.SetMusicVolume(1);
        AudioVolumeSettings.SetUiSoundVolume(.7f);
        AudioVolumeSettings.SetSfxVolume(0);
        Check(AudioVolumeSettings.CanSave, "Preview is dirty");
        await AudioVolumeSettings.SaveAsync();
        Check(!AudioVolumeSettings.CanSave, "Successful save clears dirty");
        await AudioVolumeSettings.SaveAsync();
        Check(FirestoreAudioPreferencesGateway.Writes == 1, "No redundant save");
        await AudioVolumeSettings.LoadAccountAsync("B");
        Check(AudioVolumeSettings.MusicVolume == .75f && AudioVolumeSettings.SfxVolume == .75f,
            "B does not inherit A");
        await AudioVolumeSettings.LoadAccountAsync("A");
        Check(AudioVolumeSettings.MusicVolume == 1 && AudioVolumeSettings.SfxVolume == 0,
            "A reloads its own cloud settings");
        AudioVolumeSettings.SetMusicVolume(.4f);
        AudioVolumeSettings.SetMusicVolume(1);
        Check(!AudioVolumeSettings.HasChanges, "Returning to saved value clears dirty");
        AudioVolumeSettings.SetUiSoundMuted(true);
        Check(AudioVolumeSettings.HasChanges && AudioVolumeSettings.MusicVolume == 1,
            "Mute dirties only its channel");
        FirestoreAudioPreferencesGateway.Fail = true;
        await AudioVolumeSettings.SaveAsync();
        Check(AudioVolumeSettings.CanSave && !AudioVolumeSettings.IsSaving,
            "Failed save preserves preview and permits retry");
        FirestoreAudioPreferencesGateway.Fail = false;
        var pending = new TaskCompletionSource<bool>();
        FirestoreAudioPreferencesGateway.Pending = pending.Task;
        Task saving = AudioVolumeSettings.SaveAsync();
        Check(AudioVolumeSettings.IsSaving && !AudioVolumeSettings.CanSave,
            "Saving locks button");
        AudioVolumeSettings.SetMusicVolume(.2f);
        pending.SetResult(true);
        await saving;
        Check(AudioVolumeSettings.HasChanges, "Edits during save remain dirty");
        pending = new TaskCompletionSource<bool>();
        FirestoreAudioPreferencesGateway.Pending = pending.Task;
        saving = AudioVolumeSettings.SaveAsync();
        await AudioVolumeSettings.LoadAccountAsync("B");
        pending.SetResult(true);
        await saving;
        Check(AudioVolumeSettings.MusicVolume == .75f && !AudioVolumeSettings.HasChanges,
            "Late save cannot contaminate B");
        AudioVolumeSettings.UseGuest();
        AudioVolumeSettings.SetMusicVolume(.3f);
        await AudioVolumeSettings.LoadAccountAsync("B");
        AudioVolumeSettings.UseGuest();
        Check(AudioVolumeSettings.MusicVolume == .3f, "Guest settings are independent");
        Check(AudioVolumeSettings.Snap(.43f) == 40 && AudioVolumeSettings.Snap(.56f) == 60
            && AudioVolumeSettings.Snap(.73f) == 75 && AudioVolumeSettings.Snap(.75f) == 75,
            "Slider snaps to tens plus 75");
        FirestoreAudioPreferencesGateway.FailLoad = true;
        await AudioVolumeSettings.LoadAccountAsync("A");
        Check(AudioVolumeSettings.MusicVolume == 1 && !AudioVolumeSettings.HasChanges,
            "Offline load uses last confirmed cache, not an unsaved preview");
        FirestoreAudioPreferencesGateway.FailLoad = false;
        var loading = new TaskCompletionSource<AudioPreferences>();
        FirestoreAudioPreferencesGateway.PendingLoad = loading.Task;
        Task<bool> oldLoad = AudioVolumeSettings.LoadAccountAsync("A");
        await AudioVolumeSettings.LoadAccountAsync("B");
        loading.SetResult(new AudioPreferences { music = 10 });
        Check(!await oldLoad && AudioVolumeSettings.MusicVolume == .75f,
            "Late load cannot replace B or trigger stale scene navigation");
        await AudioVolumeSettings.LoadAccountAsync("Preview");
        var checkpoint = AudioVolumeSettings.CapturePreview();
        int writesBefore = FirestoreAudioPreferencesGateway.Writes;
        AudioVolumeSettings.SetMusicVolume(.2f);
        AudioVolumeSettings.SetUiSoundMuted(true);
        AudioVolumeSettings.SetSfxVolume(0);
        AudioVolumeSettings.RestorePreview(checkpoint);
        Check(AudioVolumeSettings.MusicVolume == .75f
            && AudioVolumeSettings.SfxVolume == .75f
            && !AudioVolumeSettings.UiSoundMuted && !AudioVolumeSettings.HasChanges
            && FirestoreAudioPreferencesGateway.Writes == writesBefore,
            "Close restores all opening levels and mute without cloud writes");
        AudioVolumeSettings.SetMusicMuted(true);
        checkpoint = AudioVolumeSettings.CapturePreview();
        AudioVolumeSettings.SetMusicVolume(.4f);
        AudioVolumeSettings.RestorePreview(checkpoint);
        Check(AudioVolumeSettings.MusicMuted && AudioVolumeSettings.HasChanges,
            "Close preserves quick mute from before opening");
        checkpoint = AudioVolumeSettings.CapturePreview();
        AudioVolumeSettings.SetMusicVolume(.6f);
        await AudioVolumeSettings.SaveAsync();
        AudioVolumeSettings.SetMusicVolume(.3f);
        AudioVolumeSettings.RestorePreview(checkpoint);
        Check(AudioVolumeSettings.MusicVolume == .6f && !AudioVolumeSettings.HasChanges,
            "Close after save discards only edits after the successful save");
        checkpoint = AudioVolumeSettings.CapturePreview();
        AudioVolumeSettings.SetMusicVolume(.1f);
        FirestoreAudioPreferencesGateway.Fail = true;
        await AudioVolumeSettings.SaveAsync();
        FirestoreAudioPreferencesGateway.Fail = false;
        AudioVolumeSettings.RestorePreview(checkpoint);
        Check(AudioVolumeSettings.MusicVolume == .6f && !AudioVolumeSettings.HasChanges,
            "Close after failed save restores opening state");
        await AudioVolumeSettings.LoadAccountAsync("B");
        AudioVolumeSettings.RestorePreview(checkpoint);
        Check(AudioVolumeSettings.MusicVolume == .75f,
            "Old modal checkpoint cannot affect another account");
        Console.WriteLine($"{checks} checks passed");
    }
}
namespace KLTN.Infrastructure.Audio
{
    public class FirestoreAudioPreferencesGateway
    {
        public static Dictionary<string, AudioPreferences> Cloud = new();
        public static int Writes;
        public static bool Fail;
        public static Task Pending;
        public static bool FailLoad;
        public static Task<AudioPreferences> PendingLoad;
        public Task<AudioPreferences> LoadAsync(string uid)
        {
            if (FailLoad) throw new Exception("Offline");
            if (PendingLoad != null)
            {
                var result = PendingLoad;
                PendingLoad = null;
                return result;
            }
            return Task.FromResult(Cloud.TryGetValue(uid, out var value)
                ? value.Copy() : null);
        }
        public async Task SaveAsync(string uid, AudioPreferences value)
        {
            Writes++;
            if (Pending != null) { var wait = Pending; Pending = null; await wait; }
            if (Fail) throw new Exception("Test failure");
            Cloud[uid] = value.Copy();
        }
    }
}
namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
    public static class Mathf
    {
        public static float Clamp01(float v) => Math.Clamp(v, 0, 1);
        public static float Abs(float v) => Math.Abs(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
    }
    public static class PlayerPrefs
    {
        static Dictionary<string, string> data = new();
        public static string GetString(string k, string fallback) => data.GetValueOrDefault(k, fallback);
        public static void SetString(string k, string v) => data[k] = v;
        public static void Save() { }
    }
    public static class JsonUtility
    {
        static JsonSerializerOptions options = new() { IncludeFields = true };
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, options);
        public static string ToJson(object value) => JsonSerializer.Serialize(value, options);
    }
}
