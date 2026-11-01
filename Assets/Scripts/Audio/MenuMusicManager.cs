using KLTN.Audio;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(AudioSource))]
public class MenuMusicManager : MonoBehaviour
{
    private static MenuMusicManager instance;
    private AudioSource musicSource;

    // Âm lượng gốc đặt trong scene; slider Music nhân lên trên mức này.
    private float baseVolume = 1f;

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

        baseVolume = musicSource.volume;
        ApplyMusicVolume();
        AudioVolumeSettings.Changed += ApplyMusicVolume;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        if (!musicSource.isPlaying)
        {
            musicSource.Play();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "GameScene")
        {
            musicSource.Stop();
        }
        else if (scene.name == "MainMenu" ||
                 scene.name.StartsWith("HostClient"))
        {
            if (!musicSource.isPlaying)
            {
                musicSource.Play();
            }
        }
    }

    private void ApplyMusicVolume()
    {
        if (musicSource != null)
        {
            musicSource.volume = baseVolume * AudioVolumeSettings.MusicVolume;
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
}
