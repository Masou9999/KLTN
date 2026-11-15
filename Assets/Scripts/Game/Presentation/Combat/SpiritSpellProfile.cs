using UnityEngine;
namespace KLTN.Game.Presentation
{
    [CreateAssetMenu(menuName = "KLTN/Presentation/Spirit Spell")]
    public sealed class SpiritSpellProfile : ScriptableObject
    {
        public string abilityId = "thien_linh_cai_play";
        public Texture2D reviveSheet;
        public Texture2D chargeSheet, projectileSheet, explosionSheet;
        public AudioClip whoosh, explosion, healing;
        public float charge = .8f, flight = .36f, reaction = .12f, explosionDuration = .28f;
        public float streamTravel = .35f, reconstruction = .68f, fade = .12f;
        public float chargeWidth = .95f, fireballWidth = 1.52f, explosionWidth = 2.64f, ringWidth = 1.35f;
        [Range(0,1)] public float volume = .8f;
        public bool IsUsable => reviveSheet != null && chargeSheet != null && projectileSheet != null
            && explosionSheet != null && charge > 0 && flight > 0 && reaction > 0 && explosionDuration > 0
            && streamTravel > 0 && reconstruction > 0;
    }
}
