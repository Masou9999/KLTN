using UnityEngine;
namespace KLTN.Game.Presentation
{
    [CreateAssetMenu(menuName = "KLTN/Presentation/Bat Swarm")]
    public sealed class BatSwarmProfile : ScriptableObject
    {
        public string abilityId = "ma_cang_sung_ally_death";
        public Texture2D sheet;
        public AudioClip wings, chirp;
        public float duration = 2f;
        public float surroundTime = .65f;
        public float disperseTime = 1.1f;
        public float arrivalTime = 1.7f;
        public float batWidth = 91f;
        [Range(.1f, 3f)] public float wingsPitch = 1.5f;
        [Range(0, 1)] public float wingsVolume = .85f;
        [Range(0, 1)] public float chirpVolume = .7f;
        public bool IsUsable => sheet != null && duration > arrivalTime && arrivalTime > disperseTime
            && disperseTime > surroundTime && surroundTime > .2f;
    }
}
