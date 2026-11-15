using UnityEngine;

namespace KLTN.Game.Presentation
{
    [CreateAssetMenu(menuName = "KLTN/Presentation/Lightning Effect")]
    public sealed class LightningEffectProfile : ScriptableObject
    {
        public string abilityId = "than_trung_play";
        public Texture2D atlas;
        public AudioClip strikeSfx;
        [Tooltip("Skip the supplied recording's quiet lead-in so the crack lands on impact.")]
        [Min(0f)] public float sfxStartTime = 0.2f;
        [Range(0f, 1f)] public float sfxVolume = 0.8f;
        [Min(0.01f)] public float chargeDuration = 0.14f;
        [Min(0.01f)] public float travelDuration = 0.1f;
        [Min(0.01f)] public float holdDuration = 0.14f;
        [Min(0.01f)] public float fadeDuration = 0.18f;
        [Min(1f)] public float framesPerSecond = 15f;
        [Min(1f)] public float minimumWidth = 55f;
        [Min(1f)] public float maximumWidth = 145f;
        public Color glowColor = new Color(1f, 0.035f, 0.06f, 1f);

        [Tooltip("Horizontal texture bounds shared by every frame.")]
        public Vector2 horizontalBounds = new Vector2(0.06f, 0.925f);
        [Tooltip("Frames run top to bottom. Endpoint heights are measured from each row's bottom. Last row is dissipation.")]
        public Vector2[] endpointHeights =
        {
            new Vector2(0.268f, 0.596f),
            new Vector2(0.351f, 0.68f),
            new Vector2(0.434f, 0.771f),
            new Vector2(0.533f, 0.702f),
        };

        public bool IsUsable => atlas != null && endpointHeights != null
            && endpointHeights.Length >= 2
            && horizontalBounds.y > horizontalBounds.x;
    }
}
