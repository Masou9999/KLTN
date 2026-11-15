using UnityEngine;

namespace KLTN.Game.Presentation
{
    [CreateAssetMenu(menuName = "KLTN/Presentation/Bonk Effect")]
    public sealed class BonkEffectProfile : ScriptableObject
    {
        public string abilityId = "ong_ke_play";
        public Texture2D atlas;
        // Normalized UV rectangles, bottom-left origin. Each stick points upward.
        public Rect[] sticks = { new Rect(0, .5f, .5f, .5f),
            new Rect(.5f, .5f, .5f, .5f), new Rect(0, 0, .5f, .5f) };
        public Vector2[] grips = { new Vector2(.5f, .08f), new Vector2(.5f, .08f), new Vector2(.5f, .08f) };
        public Vector2[] tips = { new Vector2(.5f, .92f), new Vector2(.5f, .92f), new Vector2(.5f, .92f) };
        public Rect bubble = new Rect(.5f, 0, .5f, .5f);
        [Min(.01f)] public float appearDuration = .07f;
        [Min(.01f)] public float raiseDuration = .14f;
        [Min(0)] public float windupHold = .05f;
        [Min(.01f)] public float swingDuration = .14f;
        [Min(0)] public float contactHold = .05f;
        [Min(.01f)] public float recoilDuration = .18f;
        [Min(.01f)] public float bubbleDuration = .37f;
        [Min(.1f)] public float bubbleWidth = .95f;
        public AudioClip hitSfx;
        [Range(0, 1)] public float sfxVolume = .85f;
        public float ImpactTime => appearDuration + raiseDuration + windupHold + swingDuration;
        public float Duration => ImpactTime + Mathf.Max(contactHold + recoilDuration, bubbleDuration);
        public bool IsUsable => atlas != null && sticks != null && sticks.Length == 3
            && grips != null && grips.Length == 3 && tips != null && tips.Length == 3;
    }
}
