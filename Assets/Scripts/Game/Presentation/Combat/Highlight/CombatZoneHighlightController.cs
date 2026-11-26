
using UnityEngine;

namespace KLTN.Game.Presentation
{
    public class CombatZoneHighlightController : MonoBehaviour
    {
        [Header("Combat Effects")]
        [SerializeField] private GameObject combatEffectsRoot;

        private void Awake()
        {
            HideEffects();
        }

        public void ShowEffects()
        {
            if (combatEffectsRoot != null)
            {
                combatEffectsRoot.SetActive(true);
            }
        }

        public void HideEffects()
        {
            if (combatEffectsRoot != null)
            {
                combatEffectsRoot.SetActive(false);
            }
        }

        private void OnDisable()
        {
            HideEffects();
        }
    }
}
