using System;
using KLTN.Audio;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Owns a bounded set of slot effects and a single SFX loop outside card lifetimes.
    /// The board supplies its currently presented occupancy, including local drafts.
    /// </summary>
    public sealed class QuyCauFlamePresenter : IDisposable
    {
        #region Runtime State

        private readonly QuyCauFlameProfile profile;
        private readonly RectTransform root;
        private readonly AudioSource audio;
        private readonly QuyCauFlameView[] views;
        private readonly Canvas canvas;
        private bool raised = true;
        private bool disposed;

        #endregion

        #region Construction And Cleanup

        /// <param name="boardArea">
        /// The flames are inserted right after this sibling, so they draw above board
        /// cards but below the deck, hand and reserve zones that follow it.
        /// </param>
        public QuyCauFlamePresenter(Transform boardArea, QuyCauFlameProfile settings,
            int slotCount)
        {
            profile = settings;
            var go = new GameObject("QuyCauFlames", typeof(RectTransform),
                typeof(Canvas), typeof(AudioSource));
            root = go.GetComponent<RectTransform>();
            root.SetParent(boardArea.parent, false);
            root.SetSiblingIndex(boardArea.GetSiblingIndex() + 1);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            canvas = go.GetComponent<Canvas>();
            SetRaised(false);
            audio = go.GetComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.loop = true;
            audio.clip = profile.sound;
            views = new QuyCauFlameView[slotCount];
            AudioVolumeSettings.Changed += RefreshVolume;
            RefreshVolume();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            AudioVolumeSettings.Changed -= RefreshVolume;
            if (audio != null) audio.Stop();
            if (root != null)
            {
                root.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(root.gameObject);
            }
        }

        #endregion

        #region Presentation

        public void UpdateSlot(int index, bool desired, RectTransform anchor,
            float deltaSeconds, bool immediateEnd)
        {
            if (disposed) return;
            if (views[index] == null)
            {
                if (!desired) return;
                views[index] = new QuyCauFlameView(root, profile.sheet, index);
            }
            QuyCauFlameView view = views[index];
            view.Playback.SetDesired(desired, immediateEnd);
            view.Playback.Tick(deltaSeconds, profile.frameSeconds);
            view.Render(anchor, profile);
        }

        /// <summary>
        /// Normally the flames inherit the board canvas order (hierarchy decides), so
        /// hand/reserve/deck cover them. While combat lifts cards into CombatLayer
        /// (CardMotion 200) the flames follow them to CardMotion 205, still below
        /// DragLayer (210) and drawn cards (250+).
        /// </summary>
        public void SetRaised(bool value)
        {
            if (disposed || canvas == null || raised == value) return;
            raised = value;
            if (value) MatchSorting.Apply(canvas, MatchSorting.CardMotion, 5);
            else canvas.overrideSorting = false;
        }

        public void RefreshSound()
        {
            if (disposed || audio == null) return;
            bool visible = false;
            foreach (QuyCauFlameView view in views)
                visible |= view != null && view.Playback.IsVisible;
            if (visible && !audio.isPlaying && audio.clip != null) audio.Play();
            else if (!visible && audio.isPlaying) audio.Stop();
        }

        private void RefreshVolume()
        {
            if (audio != null)
                audio.volume = AudioVolumeSettings.SfxVolume * profile.soundGain;
        }

        #endregion
    }
}
