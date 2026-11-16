using UnityEngine;
using UnityEngine.UI;

namespace KLTN.Audio
{
    /// <summary>
    /// Toggles a persisted mute flag without changing the saved volume.
    /// </summary>
    [RequireComponent(typeof(Button), typeof(Image))]
    public sealed class AudioMuteButton : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField] private bool controlsMusic;
        // Defaults to false so existing Login music/UI buttons retain their channel.
        [SerializeField] private bool controlsCombat;
        [SerializeField] private Sprite enabledIcon;
        [SerializeField] private Sprite mutedIcon;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            GetComponent<Button>().onClick.AddListener(Toggle);
            AudioVolumeSettings.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            GetComponent<Button>().onClick.RemoveListener(Toggle);
            AudioVolumeSettings.Changed -= Refresh;
        }

        #endregion

        #region Helpers

        private void Toggle()
        {
            if (controlsCombat)
            {
                AudioVolumeSettings.SetSfxMuted(!AudioVolumeSettings.SfxMuted);
            }
            else if (controlsMusic)
            {
                AudioVolumeSettings.SetMusicMuted(!AudioVolumeSettings.MusicMuted);
            }
            else
            {
                AudioVolumeSettings.SetUiSoundMuted(!AudioVolumeSettings.UiSoundMuted);
            }
        }

        private void Refresh()
        {
            bool muted = controlsCombat ? AudioVolumeSettings.SfxMuted
                : controlsMusic ? AudioVolumeSettings.MusicMuted
                : AudioVolumeSettings.UiSoundMuted;
            GetComponent<Image>().sprite = muted ? mutedIcon : enabledIcon;
        }

        #endregion
    }
}
