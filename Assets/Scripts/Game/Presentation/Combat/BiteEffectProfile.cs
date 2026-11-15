using UnityEngine;

namespace KLTN.Game.Presentation
{
    [CreateAssetMenu(menuName = "KLTN/Presentation/Bite Effect")]
    public sealed class BiteEffectProfile : ScriptableObject
    {
        public string abilityId = "ma_tranh_summon";
        public Texture2D sheet;
        [Tooltip("Only frames 1–9. Pixel rectangles use bottom-left texture coordinates; labels are excluded.")]
        public Rect[] frames;
        public float[] frameMilliseconds = { 45, 50, 60, 85, 35, 25, 25, 35, 120 };
        [Min(0.1f)] public float widthRelativeToCard = 1.18f;
        [Min(0.01f)] public float fadeDuration = 0.06f;
        [Min(0.01f)] public float echoDuration = 0.24f;
        [Min(1f)] public float echoScale = 1.5f;
        [Range(0f, 1f)] public float echoOpacity = 0.45f;
        [Min(0f)] public float shakeDegrees = 1.8f;
        [Min(0.01f)] public float shakeDuration = 0.055f;
        public AudioClip biteSfx;
        [Min(0f)] public float sfxStartTime = 0.45f;
        [Range(0f, 1f)] public float sfxVolume = 0.85f;

        public bool IsUsable => sheet != null && frames != null && frames.Length == 9
            && frameMilliseconds != null && frameMilliseconds.Length == 9;

        public float TimeBeforeFrame(int index)
        {
            float time = 0f;
            for (int i = 0; i < Mathf.Clamp(index, 0, 9); i++)
                time += Mathf.Max(1f, frameMilliseconds[i]) / 1000f;
            return time;
        }

        public int FrameAt(float time)
        {
            for (int i = 0; i < 8; i++)
                if (time < TimeBeforeFrame(i + 1)) return i;
            return 8;
        }
    }
}
