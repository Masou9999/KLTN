
using KLTN.Audio;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class UIClickManager : MonoBehaviour
{
    #region Configuration and Runtime State

    public static UIClickManager Instance { get; private set; }

    [SerializeField] private AudioClip clickSound;
    [SerializeField] private AudioClip hoverSound;

    [SerializeField, Range(0f, 1f)]
    private float clickVolume = 0.5f;

    private AudioSource audioSource;

    // Âm lượng gốc đặt trong scene; slider âm thanh giao diện nhân lên trên mức này.
    private float baseVolume = 1f;

    #endregion

    #region Unity Lifecycle and Audio Commands

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;

        // UI feedback has its own channel, independent of combat SFX.
        baseVolume = audioSource.volume;
        ApplyUiSoundVolume();
        AudioVolumeSettings.Changed += ApplyUiSoundVolume;
    }

    public void PlayHover()
    {
        if (hoverSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(hoverSound, clickVolume);
        }
    }

    public void PlayClick()
    {
        if (clickSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(clickSound, clickVolume);
        }
    }

    private void ApplyUiSoundVolume()
    {
        if (audioSource != null)
        {
            audioSource.volume = baseVolume * AudioVolumeSettings.UiSoundVolume;
            audioSource.mute = AudioVolumeSettings.UiSoundMuted;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            AudioVolumeSettings.Changed -= ApplyUiSoundVolume;
            Instance = null;
        }
    }
    #endregion
}
