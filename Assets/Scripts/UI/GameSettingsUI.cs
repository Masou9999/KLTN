using KLTN.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shares persisted audio settings between menu and match settings panels.
/// </summary>
public class GameSettingsUI : MonoBehaviour
{
    #region Serialized Fields

    [Header("Settings UI")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider uiSoundSlider;
    [SerializeField] private CanvasGroup menuGroup;
    [SerializeField] private Button openButton;

    [Header("Scene Audio Sources (optional in MainMenu)")]
    [SerializeField] private AudioSource musicAudioSource;
    [SerializeField] private AudioSource sfxAudioSource;

    #endregion

    #region Unity Lifecycle

    private void OnEnable()
    {
        AudioVolumeSettings.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        AudioVolumeSettings.Changed -= Refresh;
    }

    #endregion

    #region Public Commands

    public void OpenSettings()
    {
        Refresh();
        if (menuGroup != null)
        {
            menuGroup.interactable = false;
        }
        settingsPanel.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(musicSlider.gameObject);
    }

    public void CloseSettings()
    {
        settingsPanel.SetActive(false);
        if (menuGroup != null)
        {
            menuGroup.interactable = true;
        }
        if (openButton != null)
        {
            EventSystem.current?.SetSelectedGameObject(openButton.gameObject);
        }
    }

    public void SetMusicVolume(float volume)
    {
        AudioVolumeSettings.SetMusicVolume(volume);
    }

    public void SetSFXVolume(float volume)
    {
        AudioVolumeSettings.SetSfxVolume(volume);
    }

    public void SetUiSoundVolume(float volume)
    {
        AudioVolumeSettings.SetUiSoundVolume(volume);
    }

    #endregion

    #region Audio Synchronization

    private void Refresh()
    {
        // Show the effective music level without losing the pre-mute saved level.
        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(AudioVolumeSettings.MusicMuted
                ? 0f : AudioVolumeSettings.MusicVolume);
        }
        if (sfxSlider != null)
        {
            sfxSlider.SetValueWithoutNotify(AudioVolumeSettings.SfxVolume);
        }
        if (uiSoundSlider != null)
        {
            uiSoundSlider.SetValueWithoutNotify(AudioVolumeSettings.UiSoundMuted
                ? 0f : AudioVolumeSettings.UiSoundVolume);
        }
        if (musicAudioSource != null)
        {
            musicAudioSource.volume = AudioVolumeSettings.MusicVolume;
            musicAudioSource.mute = AudioVolumeSettings.MusicMuted;
        }
        if (sfxAudioSource != null)
        {
            sfxAudioSource.volume = AudioVolumeSettings.SfxVolume;
        }
    }

    #endregion
}
