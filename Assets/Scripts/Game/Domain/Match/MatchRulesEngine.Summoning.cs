using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public sealed partial class MatchRulesEngine
    {
        #region Summoning and Ability Selection

        /// <summary>
        /// Validates a unit summon. Targeted Play abilities open an atomic pending
        /// selection instead of spending mana or moving the card immediately.
        /// </summary>
        public CommandResult TrySummonUnit(
            MatchState state,
            SeatId actor,
            ulong cardInstanceId
        )
        {
            CommandResult actorValidation = ValidatePriorityActor(state, actor);

            if (!actorValidation.Accepted)
            {
                return actorValidation;
            }

            PlayerState player = state.Player(actor);

            CardInstance card = player.FindCardInDrawHand(cardInstanceId);

            if (card == null)
            {
                return CommandResult.Reject(CommandRejectionReason.CardNotInHand);
            }

            if (
                !definitionsById.TryGetValue(
                    card.DefinitionId,
                    out CardDefinition definition
                )
            )
            {
                return CommandResult.Reject(CommandRejectionReason.MissingCardDefinition);
            }

            if (!player.HasActiveRosterSpace)
            {
                return CommandResult.Reject(CommandRejectionReason.ActiveRosterFull);
            }

            int manaCost = CardCostCalculator.GetEffectiveCost(state, card, definition);

            if (player.Mana < manaCost)
            {
                return CommandResult.Reject(CommandRejectionReason.InsufficientMana);
            }

            AbilityDefinition targetedPlayAbility = FindTargetedPlayAbility(
                state,
                card,
                definition
            );

            if (targetedPlayAbility != null)
            {
                bool hasTargets = abilityTargetValidator.HasEnoughValidTargets(
                    state,
                    actor,
                    card.InstanceId,
                    targetedPlayAbility.RequiredTargets
                );

                if (!hasTargets)
                {
                    if (
                        targetedPlayAbility.MissingTargetPolicy
                        == MissingTargetPolicy.SkipAbility
                    )
                    {
                        return CommitSummon(state, actor, card, definition, null, null);
                    }

                    return CommandResult.Reject(
                        CommandRejectionReason.NoValidAbilityTargets
                    );
                }

                GameEvent playEvent = GameEvent.FromCard(
                    GameEventType.UnitPlayed,
                    state.RoundNumber,
                    card
                );

                var triggeredAbility = new TriggeredAbility(
                    card.InstanceId,
                    actor,
                    targetedPlayAbility,
                    playEvent
                );

                bool opened = state.TryOpenPendingSelection(
                    triggeredAbility,
                    canCancel: true
                );

                if (!opened)
                {
                    throw new InvalidOperationException(
                        "Validated Play selection could not be opened."
                    );
                }

                state.LastEvent =
                    $"{actor} đang chọn mục tiêu cho " + $"{targetedPlayAbility.Id}.";

                return CommandResult.Success();
            }

            return CommitSummon(state, actor, card, definition, null, null);
        }

        /// <summary>
        /// Commits the targets of the currently pending ability and resumes the phase
        /// that was suspended while the player was selecting cards.
        /// </summary>
        public CommandResult TrySubmitAbilitySelection(
            MatchState state,
            SeatId actor,
            ulong requestId,
            AbilityTargetSelection selection
        )
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            PendingAbilitySelection pending = state.PendingSelection;

            if (pending == null)
            {
                return CommandResult.Reject(
                    CommandRejectionReason.NoPendingAbilitySelection
                );
            }

            if (pending.RequestId != requestId)
            {
                return CommandResult.Reject(
                    CommandRejectionReason.InvalidAbilitySelectionRequest
                );
            }

            if (pending.ChoosingSeat != actor)
            {
                return CommandResult.Reject(CommandRejectionReason.NotAbilitySelector);
            }

            if (!abilityTargetValidator.Validate(state, pending, selection))
            {
                return CommandResult.Reject(CommandRejectionReason.InvalidAbilityTarget);
            }

            CardInstance sourceCard = state
                .Player(actor)
                .FindCardAnywhere(pending.SourceCardInstanceId);

            bool isUncommittedPlay =
                pending.TriggerEvent.Type == GameEventType.UnitPlayed
                && sourceCard != null
                && sourceCard.Zone == CardZone.DrawHand;

            if (isUncommittedPlay)
            {
                if (
                    !definitionsById.TryGetValue(
                        sourceCard.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    return CommandResult.Reject(
                        CommandRejectionReason.MissingCardDefinition
                    );
                }

                PlayerState player = state.Player(actor);

                if (!player.HasActiveRosterSpace)
                {
                    return CommandResult.Reject(CommandRejectionReason.ActiveRosterFull);
                }

                int manaCost = CardCostCalculator.GetEffectiveCost(
                    state,
                    sourceCard,
                    definition
                );

                if (player.Mana < manaCost)
                {
                    return CommandResult.Reject(CommandRejectionReason.InsufficientMana);
                }

                state.TryClosePendingSelection(requestId);

                return CommitSummon(
                    state,
                    actor,
                    sourceCard,
                    definition,
                    pending.Ability,
                    selection
                );
            }

            state.TryClosePendingSelection(requestId);

            ResolveQueuedAbilities(
                state,
                pending.SourceCardInstanceId,
                pending.Ability.Id,
                selection
            );

            RoundTransition automaticTransition = ContinueAutomaticPhase(state);

            return CommandResult.Success(roundTransition: automaticTransition);
        }

        /// <summary>
        /// Cancels a pending optional Play selection without consuming mana, moving the
        /// source card or marking its ability as used.
        /// </summary>
        public CommandResult TryCancelAbilitySelection(
            MatchState state,
            SeatId actor,
            ulong requestId
        )
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            PendingAbilitySelection pending = state.PendingSelection;

            if (pending == null)
            {
                return CommandResult.Reject(
                    CommandRejectionReason.NoPendingAbilitySelection
                );
            }

            if (pending.RequestId != requestId)
            {
                return CommandResult.Reject(
                    CommandRejectionReason.InvalidAbilitySelectionRequest
                );
            }

            if (pending.ChoosingSeat != actor)
            {
                return CommandResult.Reject(CommandRejectionReason.NotAbilitySelector);
            }

            if (!pending.CanCancel)
            {
                return CommandResult.Reject(
                    CommandRejectionReason.AbilitySelectionCannotBeCancelled
                );
            }

            CardInstance sourceCard = state
                .Player(actor)
                .FindCardAnywhere(pending.SourceCardInstanceId);

            if (sourceCard == null || sourceCard.Zone != CardZone.DrawHand)
            {
                return CommandResult.Reject(
                    CommandRejectionReason.AbilitySelectionCannotBeCancelled
                );
            }

            bool closed = state.TryClosePendingSelection(requestId);

            if (!closed)
            {
                throw new InvalidOperationException(
                    "Validated ability selection could not be closed."
                );
            }

            state.LastEvent = $"{actor} đã hủy việc sử dụng " + $"{pending.Ability.Id}.";

            return CommandResult.Success();
        }

        private AbilityDefinition FindTargetedPlayAbility(
            MatchState state,
            CardInstance card,
            CardDefinition definition
        )
        {
            foreach (AbilityDefinition ability in definition.Abilities)
            {
                if (
                    ability.Trigger != AbilityTrigger.Play
                    || ability.RequiredTargets.Count == 0
                )
                {
                    continue;
                }

                if (
                    !AbilityConditionEvaluator.IsSatisfied(
                        state,
                        card.Owner,
                        ability.Condition
                    )
                )
                {
                    continue;
                }

                if (
                    ability.OncePerRound
                    && state.RoundHistory.WasAbilityUsedThisRound(
                        card.InstanceId,
                        ability.Id
                    )
                )
                {
                    continue;
                }

                return ability;
            }

            return null;
        }

        private CommandResult CommitSummon(
            MatchState state,
            SeatId actor,
            CardInstance card,
            CardDefinition definition,
            AbilityDefinition selectedAbility,
            AbilityTargetSelection selectedTargets
        )
        {
            PlayerState player = state.Player(actor);

            int manaCost = CardCostCalculator.GetEffectiveCost(state, card, definition);

            bool summoned = player.TrySummonDrawHandCard(card, manaCost);

            if (!summoned)
            {
                return CommandResult.Reject(CommandRejectionReason.ActiveRosterFull);
            }

            CompleteNonPassAction(
                state,
                actor,
                $"{actor} đã triệu hồi " + $"{definition.DisplayName} vào Reserve."
            );

            GameEventBatch eventBatch = GameEventBatch.From(
                GameEvent.FromCard(GameEventType.UnitPlayed, state.RoundNumber, card),
                GameEvent.FromCard(GameEventType.UnitSummoned, state.RoundNumber, card)
            );

            EnqueueAndResolveAbilities(
                state,
                eventBatch,
                card.InstanceId,
                selectedAbility?.Id,
                selectedTargets
            );

            UpdateOutcome(state);

            if (state.IsFinished)
            {
                state.Phase = MatchPhase.Finished;
            }

            return CommandResult.Success();
        }

        private void EnqueueAndResolveAbilities(
            MatchState state,
            GameEventBatch eventBatch,
            ulong selectedSourceCardId = 0,
            string selectedAbilityId = null,
            AbilityTargetSelection selectedTargets = null
        )
        {
            if (eventBatch != null && eventBatch.Events.Count > 0)
            {
                triggeredAbilityQueue.Enqueue(state, eventBatch);
            }

            ResolveQueuedAbilities(
                state,
                selectedSourceCardId,
                selectedAbilityId,
                selectedTargets
            );
        }

        private void ResolveQueuedAbilities(
            MatchState state,
            ulong selectedSourceCardId,
            string selectedAbilityId,
            AbilityTargetSelection selectedTargets
        )
        {
            const int maximumResolutions = 128;

            int resolvedCount = 0;
            bool suppliedSelectionConsumed = false;

            while (triggeredAbilityQueue.TryPeek(out TriggeredAbility next))
            {
                resolvedCount++;

                if (resolvedCount > maximumResolutions)
                {
                    triggeredAbilityQueue.Clear();

                    throw new InvalidOperationException(
                        "Ability resolution exceeded its safety limit."
                    );
                }

                AbilityTargetSelection executionSelection = null;

                if (next.Ability.RequiredTargets.Count > 0)
                {
                    bool hasEnoughTargets = abilityTargetValidator.HasEnoughValidTargets(
                        state,
                        next.ListenerOwner,
                        next.ListenerCardInstanceId,
                        next.Ability.RequiredTargets
                    );

                    if (!hasEnoughTargets)
                    {
                        triggeredAbilityQueue.TryDequeue(out TriggeredAbility skipped);

                        continue;
                    }

                    bool matchesSuppliedSelection =
                        !suppliedSelectionConsumed
                        && selectedTargets != null
                        && next.ListenerCardInstanceId == selectedSourceCardId
                        && string.Equals(
                            next.Ability.Id,
                            selectedAbilityId,
                            StringComparison.Ordinal
                        );

                    if (matchesSuppliedSelection)
                    {
                        executionSelection = selectedTargets;

                        suppliedSelectionConsumed = true;
                    }
                    else
                    {
                        bool opened = state.TryOpenPendingSelection(next);

                        if (!opened)
                        {
                            throw new InvalidOperationException(
                                "Queued ability could not open " + "target selection."
                            );
                        }

                        state.LastEvent =
                            $"{next.ListenerOwner} đang chọn mục tiêu "
                            + $"cho {next.Ability.Id}.";

                        return;
                    }
                }

                triggeredAbilityQueue.TryDequeue(out TriggeredAbility executing);

                GameEventBatch emittedEvents = abilityEffectExecutor.Execute(
                    state,
                    executing,
                    executionSelection
                );

                if (emittedEvents.Events.Count > 0)
                {
                    triggeredAbilityQueue.Enqueue(state, emittedEvents);
                }
            }

            // Include every reaction emitted by this chain before adjudicating lethal damage.
            UpdateOutcome(state);
            if (state.IsFinished)
            {
                state.Phase = MatchPhase.Finished;
            }
        }

        #endregion
    }
}
