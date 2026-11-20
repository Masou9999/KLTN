using UnityEngine;

namespace KLTN.Game.Presentation
{
    /// <summary>Shared badge-local atlas, timing and sound for mana reductions.</summary>
    [CreateAssetMenu(menuName = "KLTN/Presentation/Mana Reduction")]
    public sealed class ManaReductionProfile : ScriptableObject
    {
        public Texture2D sheet;
        public AudioClip sound;
        [Min(0.01f)] public float frameSeconds = 0.05f;
        [Range(0, 23)] public int costChangeFrame = 20;
        [Min(0.1f)] public float widthRelativeToCard = 0.403f;
        [Min(0.01f)] public float pulseSeconds = 0.28f;
        [Min(1f)] public float pulseScale = 1.3f;
        [Range(0f, 1f)] public float soundGain = 0.85f;
        [Min(0f)] public float soundStartSeconds;
        public bool IsUsable => sheet != null && sheet.width % 6 == 0
            && sheet.height % 4 == 0;
    }
}
