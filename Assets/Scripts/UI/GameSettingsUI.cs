using KLTN.Audio;
using TMPro;
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

    private Button saveButton;
    private Button closeButton;
    private AudioVolumeSettings.PreviewCheckpoint previewCheckpoint;
    private TMP_Text saveLabel;
    private TMP_Text saveStatus;

    #region Unity Lifecycle

    private void Awake()
    {
        BuildSaveControls();
    }

    /// <summary>Builds matching save controls using the scene's existing button style.</summary>
    private void BuildSaveControls()
    {
        if (settingsPanel == null) return;
        Button close = null;
        Button surrender = null;
        foreach (var button in settingsPanel.GetComponentsInChildren<Button>(true))
        {
            if (button.name == "CloseButton") close = button;
            if (button.name == "SurrenderButton") surrender = button;
        }
        if (close == null) return;
        closeButton = close;
        saveButton = Instantiate(close, close.transform.parent, false);
        saveButton.name = "SaveAudioButton";
        saveButton.onClick = new Button.ButtonClickedEvent();
        saveButton.onClick.AddListener(() => UIClickManager.Instance?.PlayClick());
        saveButton.onClick.AddListener(SaveAudio);
        var colors = saveButton.colors;
        colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.65f);
        saveButton.colors = colors;
        saveButton.transition = Selectable.Transition.ColorTint;
        saveLabel = saveButton.GetComponentInChildren<TMP_Text>(true);
        saveLabel.text = "Lưu";
        // Match settings retain their own action; only audio state is shared.
        PositionAction(saveButton, surrender != null ? 0 : -115);
        PositionAction(close, surrender != null ? 240 : 115);
        if (surrender != null)
        {
            PositionAction(surrender, -240);
        }
        close.GetComponentInChildren<TMP_Text>(true).text = "Đóng";
        saveStatus = Instantiate(saveLabel, close.transform.parent, false);
        saveStatus.name = "AudioSaveStatus";
        saveStatus.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        saveStatus.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        saveStatus.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        saveStatus.rectTransform.anchoredPosition = new Vector2(0, -144);
        saveStatus.rectTransform.sizeDelta = new Vector2(660, 22);
        saveStatus.enableAutoSizing = true;
        saveStatus.fontSizeMin = 14;
        saveStatus.fontSizeMax = 18;
        saveStatus.raycastTarget = false;
        Refresh();
    }

    private static void PositionAction(Button button, float x)
    {
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -184);
        rect.sizeDelta = new Vector2(220, 48);
    }

    private async void SaveAudio()
    {
        await AudioVolumeSettings.SaveAsync();
    }


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
        if (previewCheckpoint == null)
        {
            previewCheckpoint = AudioVolumeSettings.CapturePreview();
        }
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
        if (AudioVolumeSettings.IsSaving) return;
        AudioVolumeSettings.RestorePreview(previewCheckpoint);
        previewCheckpoint = null;
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
        if (closeButton != null)
        {
            closeButton.interactable = !AudioVolumeSettings.IsSaving;
        }
        if (saveButton != null)
        {
            saveButton.interactable = AudioVolumeSettings.CanSave;
            saveLabel.text = AudioVolumeSettings.IsSaving ? "Đang lưu…" : "Lưu";
            saveStatus.text = AudioVolumeSettings.Status
                ?? (AudioVolumeSettings.HasChanges ? "Có thay đổi chưa lưu" : "");
        }
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
