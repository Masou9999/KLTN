using System;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Coalesces placement changes into one finite intro/outro and a bounded loop.
    /// A withdrawal latches an outro; re-entry never queues another playback object.
    /// </summary>
    public sealed class QuyCauFlamePlayback
    {
        #region Runtime State

        private bool desired;
        private bool ending;
        private float elapsed;
        public int Frame { get; private set; } = -1;
        public bool IsVisible => Frame >= 0;

        #endregion

        #region Playback

        public void SetDesired(bool value, bool immediateEnd = false)
        {
            desired = value;
            if (!value && IsVisible)
            {
                ending = true;
                if ((Frame >= 4 && Frame < 16) || (immediateEnd && Frame < 16))
                {
                    Frame = 16;
                    elapsed = 0f;
                }
            }
            if (value && !IsVisible)
            {
                Frame = 0;
                ending = false;
                elapsed = 0f;
            }
        }

        public void Tick(float deltaSeconds, float frameSeconds)
        {
            if (!IsVisible) return;
            float duration = Math.Max(0.01f, frameSeconds);
            elapsed += Math.Max(0f, deltaSeconds);
            while (elapsed >= duration && IsVisible)
            {
                elapsed -= duration;
                Frame++;
                if (Frame == 4 && ending) Frame = 16;
                else if (Frame == 16 && !ending) Frame = 4;
                else if (Frame == 24)
                {
                    Frame = desired ? 0 : -1;
                    ending = false;
                }
            }
        }

        #endregion
    }
}
