using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    public sealed partial class CombatAnimationDirector
    {
        #region Ma Da Death Presentation

        private MaDaDeathProfile maDaDeathProfile;
        private bool maDaDeathProfileLoaded;
        private readonly List<MaDaDeathView> waterDeaths = new List<MaDaDeathView>();

        private MaDaDeathProfile ResolveWaterDeath(string definitionId)
        {
            if (!maDaDeathProfileLoaded)
            {
                maDaDeathProfile = Resources.Load<MaDaDeathProfile>("VFX/MaDaDeath");
                maDaDeathProfileLoaded = true;
            }
            return maDaDeathProfile != null && maDaDeathProfile.IsUsable
                && maDaDeathProfile.definitionId == definitionId
                ? maDaDeathProfile : null;
        }

        private IEnumerator PlayCardDeath(NetworkCardVisual target, string instanceId)
        {
            boardPresenter?.NotifyFlameDeparture(instanceId, true);
            if (target == null) yield break;
            CardDeathFeedback death = target.GetComponent<CardDeathFeedback>();
            MaDaDeathProfile water = ResolveWaterDeath(target.DefinitionId);
            if (water != null && death != null && death.VisualRoot != null)
            {
                death.ClearTargetPreviewCrack();
                waterDeaths.RemoveAll(view => view == null);
                var go = new GameObject("MaDaDeath", typeof(MaDaDeathView));
                go.transform.SetParent(animationLayer, false);
                var view = go.GetComponent<MaDaDeathView>();
                waterDeaths.Add(view);
                yield return view.Play(water, animationLayer, death.VisualRoot,
                    sfxAudioSource);
            }
            else if (death != null)
            {
                activeDeathFeedbacks.Add(death);
                yield return death.PlayDeath(instanceId, PlayDeathDust);
            }
        }

        private void ResetWaterDeaths()
        {
            foreach (MaDaDeathView view in waterDeaths)
            {
                if (view == null) continue;
                view.ResetImmediately();
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
            }
            waterDeaths.Clear();
        }

        #endregion
    }
}
