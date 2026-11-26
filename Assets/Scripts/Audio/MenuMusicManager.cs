using KLTN.Audio;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
#endif
using UnityEngine.SceneManagement;

[RequireComponent(typeof(AudioSource))]
public class MenuMusicManager : MonoBehaviour
{
    #region Configuration and Runtime State

    private static MenuMusicManager instance;
    private AudioSource musicSource;

    // Âm lượng gốc đặt trong scene; slider Music nhân lên trên mức này.
    private float baseVolume = 1f;

    // False on WebGL until the first click/key/touch; music requested before then waits.
    private static bool audioUnlocked;
    private bool playPending;

    #endregion

    #region Unity Lifecycle and Audio Commands

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        musicSource = GetComponent<AudioSource>();
        musicSource.loop = true;

        AudioVolumeSettings.RepairLegacyMusicVolume();
        baseVolume = musicSource.volume;
        ApplyMusicVolume();
        AudioVolumeSettings.Changed += ApplyMusicVolume;
        SceneManager.sceneLoaded += OnSceneLoaded;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Browsers block audio until the first user gesture, and Unity keeps retrying (one
        // console warning per attempt) once a source is playing. Start music after it instead.
        InputSystem.onAnyButtonPress.CallOnce(_ => UnlockAudio());
#else
        audioUnlocked = true;
#endif
    }

    private void Start()
    {
        if (SceneManager.GetActiveScene().name == SceneNames.Login)
        {
            StopMusic();
            return;
        }

        PlayMusic();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "GameScene" || scene.name == SceneNames.Login)
        {
            StopMusic();
        }
        else if (scene.name == "MainMenu" || scene.name.StartsWith("HostClient"))
        {
            PlayMusic();
        }
    }

    /// <summary>
    /// Plays the menu music, or remembers the request until the browser allows audio.
    /// </summary>
    private void PlayMusic()
    {
        if (!audioUnlocked)
        {
            playPending = true;
            return;
        }

        if (!musicSource.isPlaying)
        {
            musicSource.Play();
        }
    }

    private void StopMusic()
    {
        playPending = false;
        musicSource.Stop();
    }

    private void UnlockAudio()
    {
        audioUnlocked = true;

        if (this != null && playPending)
        {
            playPending = false;
            PlayMusic();
        }
    }

    private void ApplyMusicVolume()
    {
        if (musicSource != null)
        {
            musicSource.volume = baseVolume * AudioVolumeSettings.MusicVolume;
            musicSource.mute = AudioVolumeSettings.MusicMuted;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            AudioVolumeSettings.Changed -= ApplyMusicVolume;
            instance = null;
        }
    }
    #endregion
}
