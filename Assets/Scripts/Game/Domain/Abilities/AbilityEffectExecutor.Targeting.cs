using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public sealed partial class AbilityEffectExecutor
    {
        #region Targeting and Death Events

        private IReadOnlyList<CardInstance> ResolveCardTargets(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect
        )
        {
            var result = new List<CardInstance>();

            switch (effect.Target)
            {
                case EffectTarget.Source:
                    AddCardById(state, triggeredAbility.ListenerCardInstanceId, result);
                    break;

                case EffectTarget.TriggerSubject:
                    AddCardById(
                        state,
                        triggeredAbility.TriggerEvent.SubjectCardInstanceId,
                        result
                    );
                    break;

                case EffectTarget.PrimarySelection:
                    AddSelectedCards(state, selection?.PrimaryTargets, result);
                    break;

                case EffectTarget.SecondarySelection:
                    AddSelectedCards(state, selection?.SecondaryTargets, result);
                    break;

                case EffectTarget.AllUnits:
                    AddAllActiveCards(state.Host, result);

                    AddAllActiveCards(state.Guest, result);
                    break;

                case EffectTarget.WeakestAllies:
                    AddWeakestCards(
                        state.Player(triggeredAbility.ListenerOwner),
                        effect.Count,
                        result
                    );
                    break;

                case EffectTarget.WeakestEnemies:
                    AddWeakestCards(
                        state.Player(triggeredAbility.ListenerOwner.Opponent()),
                        effect.Count,
                        result
                    );
                    break;
            }

            return result.AsReadOnly();
        }

        private void AddWeakestCards(
            PlayerState player,
            int count,
            List<CardInstance> result
        )
        {
            var candidates = new List<CardInstance>();

            AddAllActiveCards(player, candidates);

            candidates.Sort(CompareWeakness);

            int takeCount = Math.Min(Math.Max(0, count), candidates.Count);

            for (int i = 0; i < takeCount; i++)
            {
                AddUnique(candidates[i], result);
            }
        }

        private int CompareWeakness(CardInstance left, CardInstance right)
        {
            definitionsById.TryGetValue(
                left.DefinitionId,
                out CardDefinition leftDefinition
            );

            definitionsById.TryGetValue(
                right.DefinitionId,
                out CardDefinition rightDefinition
            );

            int damageComparison = left.GetDamage(leftDefinition)
                .CompareTo(right.GetDamage(rightDefinition));

            if (damageComparison != 0)
            {
                return damageComparison;
            }

            int healthComparison = left.CurrentHealth.CompareTo(right.CurrentHealth);

            if (healthComparison != 0)
            {
                return healthComparison;
            }

            return left.InstanceId.CompareTo(right.InstanceId);
        }

        private static void AddAllActiveCards(
            PlayerState player,
            List<CardInstance> result
        )
        {
            foreach (CardInstance card in player.Reserve)
            {
                AddUnique(card, result);
            }

            foreach (CardInstance card in player.Board)
            {
                AddUnique(card, result);
            }
        }

        private static void AddSelectedCards(
            MatchState state,
            IReadOnlyList<ulong> instanceIds,
            List<CardInstance> result
        )
        {
            if (instanceIds == null)
            {
                return;
            }

            foreach (ulong instanceId in instanceIds)
            {
                AddCardById(state, instanceId, result);
            }
        }

        private static void AddCardById(
            MatchState state,
            ulong instanceId,
            List<CardInstance> result
        )
        {
            if (instanceId == 0)
            {
                return;
            }

            CardInstance card = state.Host.FindCardAnywhere(instanceId);

            if (card == null)
            {
                card = state.Guest.FindCardAnywhere(instanceId);
            }

            AddUnique(card, result);
        }

        private static void AddUnique(CardInstance card, List<CardInstance> result)
        {
            if (card == null)
            {
                return;
            }

            if (result.Exists(item => item.InstanceId == card.InstanceId))
            {
                return;
            }

            result.Add(card);
        }

        private GameEventBatch MoveDeadCardsAndBuildEvents(MatchState state)
        {
            var deadCards = new List<CardInstance>();

            AddDeadActiveCards(state.Host, deadCards);

            AddDeadActiveCards(state.Guest, deadCards);

            deadCards.Sort((left, right) => left.InstanceId.CompareTo(right.InstanceId));

            var deathEvents = new List<GameEvent>();

            foreach (CardInstance deadCard in deadCards)
            {
                if (
                    !definitionsById.TryGetValue(
                        deadCard.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    continue;
                }

                bool moved = state
                    .Player(deadCard.Owner)
                    .TryMoveDeadActiveCardToGraveyard(deadCard);

                if (!moved)
                {
                    continue;
                }

                int damageAtDeath = deadCard.GetDamage(definition);

                int maximumHealthAtDeath = deadCard.GetMaximumHealth(definition);

                state.RoundHistory.RecordDeath(
                    deadCard,
                    damageAtDeath,
                    maximumHealthAtDeath
                );
                state.Journal.PassiveCosts(state, definitionsById, deadCard.Owner);

                deathEvents.Add(
                    GameEvent.FromCard(
                        GameEventType.UnitDied,
                        state.RoundNumber,
                        deadCard
                    )
                );
            }

            return new GameEventBatch(deathEvents);
        }

        private static void AddDeadActiveCards(
            PlayerState player,
            List<CardInstance> result
        )
        {
            foreach (CardInstance card in player.Reserve)
            {
                if (card.IsDead)
                {
                    result.Add(card);
                }
            }

            foreach (CardInstance card in player.Board)
            {
                if (card.IsDead)
                {
                    result.Add(card);
                }
            }
        }

        #endregion
    }
}
