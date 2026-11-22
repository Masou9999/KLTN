using System;
using System.Collections;
using System.Collections.Generic;
using KLTN.Game.Networking;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    public sealed partial class CombatAnimationDirector : MonoBehaviour
    {

        #region Round Animation

        private IEnumerator AnimateResolution(MatchUpdateDto update)
        {
            if (boardPresenter == null || animationLayer == null)
            {
                Debug.LogWarning("Combat animation references are incomplete.");

                yield break;
            }

            RoundResolutionDto resolution = update.resolution;
            int viewerSeat = update.snapshot.viewerSeat;

            pendingDeathsById.Clear();

            boardPresenter.PrepareCombat(resolution, viewerSeat);
            Canvas.ForceUpdateCanvases();

            yield return null;

            yield return WaitUnscaled(combatPreviewDuration);

            for (int i = 0; i < resolution.steps.Length; i++)
            {
                yield return AnimateStep(resolution.steps[i], viewerSeat);

                if (intervalBetweenSlots > 0f && i < resolution.steps.Length - 1)
                {
                    yield return WaitUnscaled(intervalBetweenSlots);
                }
            }

            bool hasPendingDeaths = pendingDeathsById.Count > 0;

            if (hasPendingDeaths)
            {
                yield return WaitUnscaled(deathDelayAfterCombat);
                yield return AnimatePendingDeaths();
            }

            yield return WaitForDamageFeedback();

            RestorePreparedCards();
            ResetFeedbackViews();

            yield return AnimateSurvivingCombatCardsReturnToReserve(update);

            activeDeathFeedbacks.Clear();
            pendingDeathsById.Clear();

            // MatchResultPresenter observes the authoritative snapshot applied after
            // both combat and ability animations by ProcessUpdates.
        }

        private IEnumerator AnimateSurvivingCombatCardsReturnToReserve(MatchUpdateDto update)
        {
            if (boardPresenter == null || update?.resolution?.steps == null)
            {
                yield break;
            }

            int viewerSeat = update.snapshot != null ? update.snapshot.viewerSeat : 0;
            var survivingCards = new List<ResolvedCardDto>();

            for (int i = 0; i < update.resolution.steps.Length; i++)
            {
                CombatStepDto step = update.resolution.steps[i];
                if (step == null)
                {
                    continue;
                }

                if (ShouldReturnToReserveAfterCombat(step.hostCard))
                {
                    survivingCards.Add(step.hostCard);
                }

                if (ShouldReturnToReserveAfterCombat(step.guestCard))
                {
                    survivingCards.Add(step.guestCard);
                }
            }

            if (survivingCards.Count == 0)
            {
                yield break;
            }

            foreach (ResolvedCardDto survivor in survivingCards)
                boardPresenter.NotifyFlameDeparture(survivor.cardBefore.instanceId, false);

            yield return boardPresenter.AnimateCombatSurvivorsReturnToReserve(
                survivingCards,
                viewerSeat,
                update.snapshot,
                animationLayer
            );
        }

        private bool ShouldReturnToReserveAfterCombat(ResolvedCardDto card)
        {
            if (!HasResolvedCard(card))
            {
                return false;
            }

            if (card.died || card.diedFromEphemeral || card.healthAfter <= 0)
            {
                return false;
            }

            string instanceId = card.cardBefore.instanceId;
            if (pendingDeathsById.ContainsKey(instanceId))
            {
                return false;
            }

            string definitionId = card.cardBefore.definitionId;
            if (
                string.Equals(definitionId, "MaDa", StringComparison.OrdinalIgnoreCase)
                || string.Equals(definitionId, "MaLai", StringComparison.OrdinalIgnoreCase)
                || ResolveWaterDeath(definitionId) != null
            )
            {
                return false;
            }

            return true;
        }

        private IEnumerator AnimateRoundTransition(MatchUpdateDto update)
        {
            RoundTransitionDto transition = update.roundTransition;

            if (transitionBanner == null)
            {
                Debug.LogWarning("Round transition banner is not assigned.", this);

                yield break;
            }

            yield return transitionBanner.PlayRoundStart(transition.nextRoundNumber);
        }

        #endregion

        #region Slot Animation

        private IEnumerator AnimateStep(CombatStepDto step, int viewerSeat)
        {
            bool hasHostCard = HasResolvedCard(step.hostCard);
            bool hasGuestCard = HasResolvedCard(step.guestCard);

            CombatCardAnimator hostAnimator = null;
            CombatCardAnimator guestAnimator = null;

            bool hasHostVisual =
                hasHostCard && TryGetAnimator(step.hostCard, out hostAnimator);

            bool hasGuestVisual =
                hasGuestCard && TryGetAnimator(step.guestCard, out guestAnimator);

            Debug.Log(
                $"Combat step: Slot={step.slotIndex}, "
                    + $"HostCard={CardIdOrNone(step.hostCard)}, "
                    + $"GuestCard={CardIdOrNone(step.guestCard)}, "
                    + $"HostVisual={hasHostVisual}, GuestVisual={hasGuestVisual}."
            );

            if (hasHostCard && hasGuestCard)
            {
                if (!hasHostVisual || !hasGuestVisual)
                {
                    Debug.LogWarning(
                        $"Không tìm thấy đủ visual cho combat card-vs-card. "
                            + $"Slot={step.slotIndex}, HostVisual={hasHostVisual}, "
                            + $"GuestVisual={hasGuestVisual}."
                    );

                    yield break;
                }

                PrepareAnimator(step.hostCard, viewerSeat, hostAnimator);
                PrepareAnimator(step.guestCard, viewerSeat, guestAnimator);

                yield return AnimateRetreat(hostAnimator, guestAnimator);

                yield return AnimateUnitCollision(step, hostAnimator, guestAnimator);

                preparedAnimators.Remove(hostAnimator);
                preparedAnimators.Remove(guestAnimator);
                yield break;
            }

            CombatCardAnimator attackerAnimator = hasHostCard
                ? hostAnimator
                : guestAnimator;

            bool hasAttackerVisual = hasHostCard ? hasHostVisual : hasGuestVisual;

            if (!hasAttackerVisual || attackerAnimator == null)
            {
                Debug.LogWarning(
                    $"Không tìm thấy attacker visual cho direct attack. "
                        + $"Slot={step.slotIndex}."
                );

                yield break;
            }

            ResolvedCardDto attackerCard = hasHostCard ? step.hostCard : step.guestCard;

            PrepareAnimator(attackerCard, viewerSeat, attackerAnimator);

            yield return AnimateRetreat(attackerAnimator, null);

            yield return AnimateDirectAttack(step, viewerSeat, attackerAnimator);
            preparedAnimators.Remove(attackerAnimator);
        }

        private IEnumerator AnimateRetreat(
            CombatCardAnimator first,
            CombatCardAnimator second
        )
        {
            var routines = new List<IEnumerator>(4);

            AddRetreatRoutines(first, routines);
            AddRetreatRoutines(second, routines);

            yield return RunMany(routines);
        }

        private void AddRetreatRoutines(
            CombatCardAnimator animator,
            List<IEnumerator> routines
        )
        {
            if (animator == null)
            {
                return;
            }

            routines.Add(animator.MoveToRetreat(retreatDuration));

            CombatImpactFlashView flash = animator.GetComponent<CombatImpactFlashView>();

            if (flash != null)
            {
                routines.Add(flash.EaseIn(retreatDuration));
            }
        }

        private IEnumerator AnimateUnitCollision(
            CombatStepDto step,
            CombatCardAnimator hostAnimator,
            CombatCardAnimator guestAnimator
        )
        {
            DamageFeedbackView hostFeedback =
                hostAnimator.GetComponent<DamageFeedbackView>();

            DamageFeedbackView guestFeedback =
                guestAnimator.GetComponent<DamageFeedbackView>();

            BeginFeedback(hostFeedback);
            BeginFeedback(guestFeedback);

            CalculateCollisionTargets(
                hostAnimator,
                guestAnimator,
                out Vector3 hostTarget,
                out Vector3 guestTarget
            );

            PlaySlide(hostAnimator);
            PlaySlide(guestAnimator);

            yield return RunTogether(
                hostAnimator.Strike(hostTarget, strikeDuration),
                guestAnimator.Strike(guestTarget, strikeDuration)
            );

            PlayAttackSfx();

            if (vfxPresenter != null)
            {
                Vector3 impactPosition = (hostTarget + guestTarget) * 0.5f;
                vfxPresenter.PlayCardHit(impactPosition);
            }

            StartDamageFeedback(PlayCardDamage(hostFeedback, step.hostCard));

            StartDamageFeedback(PlayCardDamage(guestFeedback, step.guestCard));

            yield return WaitUnscaled(impactHoldDuration);

            yield return ShowStepLethalHighlights(step);

            yield return ReturnAfterCollision(hostAnimator, guestAnimator);
        }

        private IEnumerator AnimateDirectAttack(
            CombatStepDto step,
            int viewerSeat,
            CombatCardAnimator attackerAnimator
        )
        {
            if (
                !TryGetNexusImpact(
                    step,
                    viewerSeat,
                    out bool targetIsSelf,
                    out int damagedSeat,
                    out int healthBefore,
                    out int healthAfter,
                    out int damage
                )
            )
            {
                Debug.LogWarning(
                    $"Combat step tại slot {step.slotIndex} "
                        + "không phải direct Nexus attack."
                );

                yield break;
            }

            DamageFeedbackView targetFeedback = targetIsSelf
                ? selfNexusFeedback
                : opponentNexusFeedback;

            RectTransform nexusTarget = targetIsSelf
                ? selfNexusTarget
                : opponentNexusTarget;

            Vector3 attackPosition = CalculateDirectAttackTarget(
                attackerAnimator,
                nexusTarget,
                targetIsSelf,
                out Vector3 nexusImpactPosition
            );

            Debug.Log(
                $"Nexus impact: Viewer={viewerSeat}, DamagedSeat={damagedSeat}, "
                    + $"Target={(targetIsSelf ? "Self" : "Opponent")}, "
                    + $"HP={healthBefore}->{healthAfter}, Damage={damage}."
            );

            BeginFeedback(targetFeedback);
            PlaySlide(attackerAnimator);

            yield return attackerAnimator.Strike(attackPosition, strikeDuration);

            PlayAttackSfx();

            if (vfxPresenter != null)
            {
                vfxPresenter.PlayNexusHit(nexusImpactPosition, targetIsSelf);
            }

            if (targetFeedback != null)
            {
                StartDamageFeedback(targetFeedback.PlayDamage(damage, healthAfter));
            }

            yield return WaitUnscaled(impactHoldDuration);

            // An unblocked Ephemeral attacker still dies after striking the Nexus.
            // Queue its death before returning home, just like unit-vs-unit combat.
            yield return ShowStepLethalHighlights(step);

            yield return ReturnCard(attackerAnimator);
        }

        private IEnumerator ReturnAfterCollision(
            CombatCardAnimator hostAnimator,
            CombatCardAnimator guestAnimator
        )
        {
            yield return RunTogether(ReturnCard(hostAnimator), ReturnCard(guestAnimator));
        }

        private IEnumerator ReturnCard(CombatCardAnimator animator)
        {
            if (animator == null)
            {
                yield break;
            }

            CombatImpactFlashView flash = animator.GetComponent<CombatImpactFlashView>();

            if (flash == null)
            {
                yield return animator.ReturnHome(returnDuration);
                yield break;
            }

            yield return RunTogether(
                animator.ReturnHome(returnDuration),
                flash.FadeOut(returnDuration)
            );
        }

        #endregion

        #region Combat VFX

        private void PlaySlide(CombatCardAnimator animator)
        {
            if (vfxPresenter == null || animator == null)
            {
                return;
            }

            Vector3 trailingDirection = animator.IsSelfCard ? Vector3.down : Vector3.up;

            Vector3 effectPosition =
                animator.CurrentWorldPosition
                + trailingDirection * animator.WorldHeight * 0.42f;

            bool flip = !animator.IsSelfCard;
            vfxPresenter.PlaySlide(effectPosition, flip);
        }

        private void PlayDeathDust(Vector3 worldPosition)
        {
            if (vfxPresenter != null)
            {
                vfxPresenter.PlayDeathDust(worldPosition);
            }
        }

        #endregion

        #region Target Calculation

        private void CalculateCollisionTargets(
            CombatCardAnimator hostAnimator,
            CombatCardAnimator guestAnimator,
            out Vector3 hostTarget,
            out Vector3 guestTarget
        )
        {
            Vector3 hostPosition = hostAnimator.CurrentWorldPosition;

            Vector3 guestPosition = guestAnimator.CurrentWorldPosition;

            Vector3 collisionAxis = guestPosition - hostPosition;

            if (collisionAxis.sqrMagnitude <= 0.001f)
            {
                collisionAxis = Vector3.up;
            }

            collisionAxis.Normalize();

            Vector3 collisionCenter = (hostPosition + guestPosition) * 0.5f;

            float requiredCenterDistance =
                hostAnimator.WorldHeight * 0.5f
                + guestAnimator.WorldHeight * 0.5f
                + collisionGap;

            float halfDistance = requiredCenterDistance * 0.5f;

            hostTarget = collisionCenter - collisionAxis * halfDistance;

            guestTarget = collisionCenter + collisionAxis * halfDistance;
        }

        private Vector3 CalculateDirectAttackTarget(
            CombatCardAnimator attackerAnimator,
            RectTransform nexusTarget,
            bool targetIsSelf,
            out Vector3 impactWorldPosition
        )
        {
            Vector3 attackerPosition = attackerAnimator.CurrentWorldPosition;
            Vector3 fallbackDirection = targetIsSelf ? Vector3.down : Vector3.up;

            if (nexusTarget == null)
            {
                Vector3 fallbackTarget =
                    attackerAnimator.HomeWorldPosition
                    + fallbackDirection * directAttackDistance;

                impactWorldPosition = fallbackTarget;
                return fallbackTarget;
            }

            nexusTarget.GetWorldCorners(nexusTargetCorners);

            float contactEdgeY = targetIsSelf
                ? Mathf.Max(nexusTargetCorners[1].y, nexusTargetCorners[2].y)
                : Mathf.Min(nexusTargetCorners[0].y, nexusTargetCorners[3].y);

            float distanceFromCardCenter =
                attackerAnimator.WorldHeight * 0.5f + nexusContactGap;

            float cardCenterY = targetIsSelf
                ? contactEdgeY + distanceFromCardCenter
                : contactEdgeY - distanceFromCardCenter;

            impactWorldPosition = new Vector3(
                attackerPosition.x,
                contactEdgeY,
                attackerPosition.z
            );

            return new Vector3(attackerPosition.x, cardCenterY, attackerPosition.z);
        }

        #endregion

        #region Animator Preparation and Lookup

        private void PrepareAnimator(
            ResolvedCardDto card,
            int viewerSeat,
            CombatCardAnimator animator
        )
        {
            bool isSelfCard = card.seat == viewerSeat;

            animator.Prepare(animationLayer, isSelfCard, retreatDistance);

            preparedAnimators.Add(animator);
        }

        private bool TryGetAnimator(ResolvedCardDto card, out CombatCardAnimator animator)
        {
            animator = null;

            if (!HasResolvedCard(card))
            {
                return false;
            }

            if (
                !boardPresenter.TryGetFaceUpVisual(
                    card.cardBefore.instanceId,
                    out NetworkCardVisual visual
                )
            )
            {
                Debug.LogWarning(
                    $"Không tìm thấy combat visual. "
                        + $"Card={card.cardBefore.instanceId}, Seat={card.seat}."
                );

                return false;
            }

            animator = visual.GetComponent<CombatCardAnimator>();

            if (animator != null)
            {
                return true;
            }

            Debug.LogWarning(
                $"Card {card.cardBefore.instanceId} does not have "
                    + $"{nameof(CombatCardAnimator)}."
            );

            return false;
        }

        #endregion
    }
}
