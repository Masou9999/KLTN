using System.Collections;
using UnityEngine;

namespace KLTN.Audio
{
    /// <summary>
    /// Plays the match music once per cycle: fades in at the start of each
    /// play-through, then rests for a fixed gap before playing again.
    /// The volume follows the Music setting.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MatchMusicPlayer : MonoBehaviour
    {
        #region Serialized Fields

        [SerializeField, Min(0f)] private float fadeInSeconds = 3f;
        [SerializeField, Min(0f)] private float gapSeconds = 20f;

        #endregion

        #region Runtime State

        private AudioSource source;
        private float fade;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            // This component owns playback; the source must not start or loop on its own.
            source.playOnAwake = false;
            source.loop = false;
            source.Stop();
        }

        private void OnEnable()
        {
            if (Application.isBatchMode || source.resource == null)
            {
                return;
            }
            StartCoroutine(PlayCycle());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            source.Stop();
        }

        // Applied every frame so the fade and the Settings sliders never overwrite each other.
        private void LateUpdate()
        {
            source.volume = AudioVolumeSettings.MusicVolume * fade;
            source.mute = AudioVolumeSettings.MusicMuted;
        }

        #endregion

        #region Playback Cycle

        private IEnumerator PlayCycle()
        {
            while (true)
            {
                fade = 0f;
                source.Play();

                for (float t = 0f; t < fadeInSeconds; t += Time.unscaledDeltaTime)
                {
                    fade = t / fadeInSeconds;
                    yield return null;
                }
                fade = 1f;

                // Paused audio (lost focus) still reports isPlaying, so the gap starts
                // only after the track really reached its end.
                while (source.isPlaying)
                {
                    yield return null;
                }

                yield return new WaitForSecondsRealtime(gapSeconds);
            }
        }

        #endregion
    }
}
