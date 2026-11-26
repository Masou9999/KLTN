using UnityEngine;

namespace KLTN.Game.Presentation
{
    public class SummonZoneHighlightController : MonoBehaviour
    {
        [Header("Summon Effects")]
        [SerializeField] private GameObject summonEffectsRoot;

        private void Awake()
        {
            HideEffects();
        }

        public void ShowEffects()
        {
            if (summonEffectsRoot != null)
            {
                summonEffectsRoot.SetActive(true);
            }
        }

        public void HideEffects()
        {
            if (summonEffectsRoot != null)
            {
                summonEffectsRoot.SetActive(false);
            }
        }

        private void OnDisable()
        {
            HideEffects();
        }
    }
}