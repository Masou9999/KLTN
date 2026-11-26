
using UnityEngine;

namespace KLTN.Game.Presentation
{
    public class SkillTargetZoneHighlightController : MonoBehaviour
    {
        [Header("Skill Target Effects")]
        [SerializeField] private GameObject skillTargetVisuals;

        private void Awake()
        {
            HideEffects();
        }

        public void ShowEffects()
        {
            if (skillTargetVisuals != null)
                skillTargetVisuals.SetActive(true);
        }

        public void HideEffects()
        {
            if (skillTargetVisuals != null)
                skillTargetVisuals.SetActive(false);
        }

        private void OnDisable()
        {
            HideEffects();
        }
    }
}
