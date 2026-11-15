using UnityEngine;

namespace KLTN.Game.Presentation
{
    [CreateAssetMenu(menuName = "KLTN/Presentation/Feast Effect")]
    public sealed class FeastEffectProfile : ScriptableObject
    {
        public string abilityId = "ma_doi_play";
        public Texture2D chicken;
        public Texture2D smoke;
        public float smokeDelay = .02f;
        public float shrinkDuration = .09f;
        public float chickenDelay = .08f;
        public float chickenGrowDuration = .13f;
        public float windupDuration = .14f;
        public float flightDuration = .4f;
        public float returnDuration = .1f;
        public float chewDelay = .3f;
        public float impactHold = .2f;
        public float chewPitch = 1.5f;
        public float chickenWidth = .8f;
        public float smokeWidth = 1.5f;
        public float leanDegrees = 7f;
        public float spinDegrees = 180f;
        public AudioClip pop, swish, chew;
        public float popOffset = .105f, popEnd = .26f;
        public float swishOffset = .15f, swishEnd = .31f;
        public float chewEnd = 2.03f;
        [Range(0, 1)] public float volume = .85f;
        public float SmokeDuration => .61f;
        public float WindupStart => smokeDelay + SmokeDuration;
        public float FlightStart => WindupStart + windupDuration;
        public float ImpactTime => FlightStart + flightDuration;
        public float ChewStart => ImpactTime + chewDelay;
        public float Duration => ImpactTime + Mathf.Max(returnDuration, impactHold);
        public bool IsUsable => chicken != null && smoke != null && flightDuration > 0
            && windupDuration > 0 && shrinkDuration > 0 && chickenGrowDuration > 0 && returnDuration > 0;
        public int SmokeFrame(float time)
        {
            if (time < .08f) return Mathf.Clamp((int)(time / .02f), 0, 3);
            if (time < .43f) return Mathf.Clamp(4 + (int)((time - .08f) / .025f), 4, 17);
            return Mathf.Clamp(18 + (int)((time - .43f) / .03f), 18, 23);
        }
    }
}
