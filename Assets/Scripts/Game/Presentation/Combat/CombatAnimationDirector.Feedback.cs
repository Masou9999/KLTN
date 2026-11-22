using System;
using System.Collections;
using System.Collections.Generic;
using KLTN.Game.Networking;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    public sealed partial class CombatAnimationDirector : MonoBehaviour
    {
        #region Damage Feedback

        private void BeginFeedback(DamageFeedbackView feedback)
        {
            if (feedback == null)
            {
                return;
            }

            activeFeedbackViews.Add(feedback);
            feedback.BeginAnticipation();
        }

        private static IEnumerator PlayCardDamage(
            DamageFeedbackView feedback,
            ResolvedCardDto card
        )
        {
            if (feedback == null || !HasResolvedCard(card))
            {
                yield break;
            }

            int damage = Mathf.Max(0, card.damageTaken);

            yield return feedback.PlayDamage(damage, card.healthAfter);
        }

        private static bool TryGetNexusImpact(
            CombatStepDto step,
            int viewerSeat,
            out bool targetIsSelf,
            out int damagedSeat,
            out int healthBefore,
            out int healthAfter,
            out int damage
        )
        {
            const int hostSeat = 0;
            const int guestSeat = 1;

            bool hasHostCard = HasResolvedCard(step.hostCard);
            bool hasGuestCard = HasResolvedCard(step.guestCard);

            bool hostAttacksGuest = hasHostCard && !hasGuestCard;
            bool guestAttacksHost = hasGuestCard && !hasHostCard;

            if (hostAttacksGuest)
            {
                damagedSeat = guestSeat;
                healthBefore = step.guestNexusHealthBefore;
                healthAfter = step.guestNexusHealthAfter;
            }
            else if (guestAttacksHost)
            {
                damagedSeat = hostSeat;
                healthBefore = step.hostNexusHealthBefore;
                healthAfter = step.hostNexusHealthAfter;
            }
            else
            {
                targetIsSelf = false;
                damagedSeat = -1;
                healthBefore = 0;
                healthAfter = 0;
                damage = 0;
                return false;
            }

            targetIsSelf = damagedSeat == viewerSeat;
            damage = Mathf.Max(0, healthBefore - healthAfter);
            return true;
        }

        private static bool HasResolvedCard(ResolvedCardDto card)
        {
            return card?.cardBefore != null
                && !string.IsNullOrWhiteSpace(card.cardBefore.instanceId);
        }

        private static string CardIdOrNone(ResolvedCardDto card)
        {
            return HasResolvedCard(card) ? card.cardBefore.instanceId : "none";
        }

        private void ResetFeedbackViews()
        {
            foreach (DamageFeedbackView feedback in activeFeedbackViews)
            {
                if (feedback != null)
                {
                    feedback.ResetImmediately();
                }
            }

            activeFeedbackViews.Clear();
        }

        #endregion

        #region Death Feedback

        private IEnumerator ShowStepLethalHighlights(CombatStepDto step)
        {
            var routines = new List<IEnumerator>(2);

            RegisterLethalHighlight(step.hostCard, routines);
            RegisterLethalHighlight(step.guestCard, routines);

            yield return RunMany(routines);
        }

        private void RegisterLethalHighlight(
            ResolvedCardDto card,
            List<IEnumerator> routines
        )
        {
            if (!IsDead(card))
            {
                return;
            }

            string instanceId = card.cardBefore.instanceId;

            boardPresenter.NotifyFlameDeparture(instanceId, true);

            if (pendingDeathsById.ContainsKey(instanceId))
            {
                return;
            }

            if (
                !boardPresenter.TryGetFaceUpVisual(
                    instanceId,
                    out NetworkCardVisual visual
                )
            )
            {
                Debug.LogWarning(
                    $"Cannot prepare dead card {instanceId}: visual not found."
                );

                return;
            }

            CardDeathFeedback deathFeedback = visual.GetComponent<CardDeathFeedback>();

            if (deathFeedback == null)
            {
                Debug.LogWarning(
                    $"Card {instanceId} does not have " + $"{nameof(CardDeathFeedback)}."
                );

                return;
            }

            pendingDeathsById.Add(instanceId, deathFeedback);
            activeDeathFeedbacks.Add(deathFeedback);
            if (ResolveWaterDeath(visual.DefinitionId) == null)
            {
                routines.Add(deathFeedback.ShowLethalHighlight(instanceId));
            }
        }

        private IEnumerator AnimatePendingDeaths()
        {
            var routines = new List<IEnumerator>(pendingDeathsById.Count);

            foreach (KeyValuePair<string, CardDeathFeedback> pair in pendingDeathsById)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                routines.Add(PlayCardDeath(
                    pair.Value.GetComponent<NetworkCardVisual>(), pair.Key
                ));
            }

            yield return RunMany(routines);
        }

        private void ResetDeathFeedbacks()
        {
            foreach (CardDeathFeedback feedback in activeDeathFeedbacks)
            {
                if (feedback != null)
                {
                    feedback.ResetImmediately();
                }
            }

            activeDeathFeedbacks.Clear();
        }

        private static bool IsDead(ResolvedCardDto card)
        {
            return HasResolvedCard(card) && card.died;
        }

        #endregion
    }
}
