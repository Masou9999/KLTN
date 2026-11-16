using UnityEngine;

namespace KLTN.Game.Presentation
{
    /// <summary>Timing and atlas alignment for the card's jump into water.</summary>
    [CreateAssetMenu(menuName = "KLTN/Presentation/Ma Da Death")]
    public sealed class MaDaDeathProfile : ScriptableObject
    {
        public string definitionId = "MaDa";
        public Texture2D sheet;
        public AudioClip splashSfx;
        [Min(0.01f)] public float jumpDuration = 0.26f;
        [Min(0f)] public float jumpHeightRelativeToCard = 0.3f;
        [Range(0.1f, 1f)] public float jumpEndScale = 0.75f;
        public float jumpEndRotation = -12f;
        public float[] frameMilliseconds =
            { 70, 60, 60, 65, 65, 70, 90, 90, 80, 80, 90, 120 };
        public Vector2 firstCardCenter = new Vector2(0.5f, 0.75f);
        [Range(0.1f, 1f)] public float firstCardHeight = 0.47f;
        [Range(0, 11)] public int sfxFrame = 0;
        [Min(0f)] public float sfxStartTime;
        [Range(0f, 1f)] public float sfxVolume = 0.85f;
        [Min(0.01f)] public float fadeDuration = 0.1f;

        public bool IsUsable => sheet != null && frameMilliseconds != null
            && frameMilliseconds.Length == 12;
    }
}
