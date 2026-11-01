using KLTN.Audio;
using UnityEngine;
using UnityEngine.UI;

public class GameSettingsUI : MonoBehaviour
{
    [Header("Settings UI")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicAudioSource;
    [SerializeField] private AudioSource sfxAudioSource;

    private void Start()
    {
        // Đọc âm lượng đã lưu
        float savedMusicVolume = AudioVolumeSettings.MusicVolume;
        float savedSFXVolume = AudioVolumeSettings.SfxVolume;

        // Cập nhật vị trí slider
        musicSlider.value = savedMusicVolume;
        sfxSlider.value = savedSFXVolume;

        // Áp dụng âm lượng ngay khi vào trận
        musicAudioSource.volume = savedMusicVolume;
        sfxAudioSource.volume = savedSFXVolume;
    }

    public void OpenSettings()
    {
        settingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        settingsPanel.SetActive(false);
    }

    public void SetMusicVolume(float volume)
    {
        musicAudioSource.volume = volume;

        // Lưu PlayerPrefs và báo cho nguồn âm sống lâu (nhạc menu) cập nhật theo.
        AudioVolumeSettings.SetMusicVolume(volume);
    }

    public void SetSFXVolume(float volume)
    {
        sfxAudioSource.volume = volume;

        // Lưu PlayerPrefs và báo cho UIClickManager cập nhật tiếng click theo slider SFX.
        AudioVolumeSettings.SetSfxVolume(volume);
    }
}
