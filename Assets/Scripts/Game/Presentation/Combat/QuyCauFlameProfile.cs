using UnityEngine;

namespace KLTN.Game.Presentation
{
    /// <summary>Atlas, timing and shared loop audio for the adjacent Support marker.</summary>
    [CreateAssetMenu(menuName = "KLTN/Presentation/Quy Cau Flame")]
    public sealed class QuyCauFlameProfile : ScriptableObject
    {
        public string definitionId = "QuyCau";
        public Texture2D sheet;
        public AudioClip sound;
        [Min(0.01f)] public float frameSeconds = 0.11f;
        public Vector2 frameSize = new Vector2(275f, 385f);
        [Tooltip("Shared content bounds inside each 300x420 cell, from its top-left.")]
        public Rect frameContentPixels = new Rect(27f, 10f, 241f, 381f);
        [Min(0.1f)] public float sizeMultiplier = 1f;
        public Vector2 offset;
        [Range(0f, 1f)] public float soundGain = 0.65f;

        public bool IsUsable => sheet != null && sheet.width % 4 == 0
            && sheet.height % 6 == 0;

        /// <summary>Recovers the atlas crop when an older asset has an invalid Rect.</summary>
        public Rect ContentPixels
        {
            get
            {
                Rect crop = frameContentPixels;
                return crop.width > 0f && crop.height > 0f
                    && crop.x >= 0f && crop.y >= 0f
                    && crop.xMax <= 300f && crop.yMax <= 420f
                    ? crop : new Rect(27f, 10f, 241f, 381f);
            }
        }
    }
}
