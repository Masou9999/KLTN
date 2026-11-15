using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public sealed partial class AbilityEffectExecutor
    {
        #region Basic Effects

        private void ExecuteDamage(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect
        )
        {
            if (effect.Target == EffectTarget.AlliedNexus)
            {
                int healthBefore = state.Player(triggeredAbility.ListenerOwner).NexusHealth;
                state
                    .Player(triggeredAbility.ListenerOwner)
                    .ApplyNexusDamage(effect.Amount);

                RecordNexusEffect(
                    state,
                    triggeredAbility,
                    EffectKind.Damage,
                    triggeredAbility.ListenerOwner,
                    healthBefore
                );

                return;
            }

            if (effect.Target == EffectTarget.EnemyNexus)
            {
                SeatId targetOwner = triggeredAbility.ListenerOwner.Opponent();
                int healthBefore = state.Player(targetOwner).NexusHealth;
                state
                    .Player(targetOwner)
                    .ApplyNexusDamage(effect.Amount);

                RecordNexusEffect(
                    state,
                    triggeredAbility,
                    EffectKind.Damage,
                    targetOwner,
                    healthBefore
                );

                return;
            }

            foreach (
                CardInstance target in ResolveCardTargets(
                    state,
                    triggeredAbility,
                    selection,
                    effect
                )
            )
            {
                AbilityCardState before = CaptureCard(target, state);
                target.ApplyDamage(effect.Amount);

                if (before.Health != target.CurrentHealth)
                {
                    RecordCardEffect(
                        state,
                        triggeredAbility,
                        EffectKind.Damage,
                        before,
                        CaptureCard(target, state)
                    );
                }
            }
        }

        private void ExecuteHeal(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect
        )
        {
            if (effect.Target == EffectTarget.AlliedNexus)
            {
                int healthBefore = state.Player(triggeredAbility.ListenerOwner).NexusHealth;
                state.Player(triggeredAbility.ListenerOwner).HealNexus(effect.Amount);
                RecordNexusEffect(state, triggeredAbility, EffectKind.Heal,
                    triggeredAbility.ListenerOwner, healthBefore);

                return;
            }

            if (effect.Target == EffectTarget.EnemyNexus)
            {
                int healthBefore = state.Player(triggeredAbility.ListenerOwner.Opponent()).NexusHealth;
                state
                    .Player(triggeredAbility.ListenerOwner.Opponent())
                    .HealNexus(effect.Amount);
                RecordNexusEffect(state, triggeredAbility, EffectKind.Heal,
                    triggeredAbility.ListenerOwner.Opponent(), healthBefore);

                return;
            }

            foreach (
                CardInstance target in ResolveCardTargets(
                    state,
                    triggeredAbility,
                    selection,
                    effect
                )
            )
            {
                if (
                    definitionsById.TryGetValue(
                        target.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    var before = CaptureCard(target, state);
                    target.Heal(definition, effect.Amount);
                    if (before.Health != target.CurrentHealth)
                        RecordJournalCard(state, triggeredAbility, effect.Kind, before, CaptureCard(target, state));
                }
            }
        }

        private void ExecuteKill(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect
        )
        {
            foreach (
                CardInstance target in ResolveCardTargets(
                    state,
                    triggeredAbility,
                    selection,
                    effect
                )
            )
            {
                AbilityCardState before = CaptureCard(target, state);
                target.Kill();

                if (before.Health > 0)
                {
                    RecordCardEffect(
                        state,
                        triggeredAbility,
                        effect.Kind,
                        before,
                        CaptureCard(target, state)
                    );
                }
            }
        }

        private void ExecuteDraw(MatchState state, TriggeredAbility triggeredAbility, int amount)
        {
            PlayerState player = state.Player(triggeredAbility.ListenerOwner);

            for (int i = 0; i < Math.Max(0, amount); i++)
            {
                if (!player.DrawOne())
                {
                    break;
                }
                RecordJournalCard(state, triggeredAbility, EffectKind.Draw, null,
                    CaptureCard(player.DrawHand[player.DrawHand.Count - 1]));
            }
        }

        private void ExecuteBuff(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect
        )
        {
            foreach (
                CardInstance target in ResolveCardTargets(
                    state,
                    triggeredAbility,
                    selection,
                    effect
                )
            )
            {
                if (
                    !definitionsById.TryGetValue(
                        target.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    continue;
                }

                var before = CaptureCard(target, state);
                target.ApplyBuff(
                    definition,
                    effect.Amount,
                    effect.SecondaryAmount,
                    effect.Duration
                );
                if (before.Damage != target.GetDamage(definition) || before.MaximumHealth != target.GetMaximumHealth(definition))
                    RecordJournalCard(state, triggeredAbility, effect.Kind, before, CaptureCard(target, state), effect.Duration);
            }
        }

        private void ExecuteReduceCost(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect
        )
        {
            foreach (
                CardInstance target in ResolveCardTargets(
                    state,
                    triggeredAbility,
                    selection,
                    effect
                )
            )
            {
                if (
                    !definitionsById.TryGetValue(
                        target.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    continue;
                }

                var before = CaptureCard(target, state);
                target.ApplyCostReduction(definition, effect.Amount, effect.Duration);
                if (before.Cost != CardCostCalculator.GetEffectiveCost(state, target, definition))
                    RecordJournalCard(state, triggeredAbility, effect.Kind, before, CaptureCard(target, state), effect.Duration);
            }
        }

        private void ExecuteReturnToDrawHand(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect
        )
        {
            foreach (
                CardInstance target in ResolveCardTargets(
                    state,
                    triggeredAbility,
                    selection,
                    effect
                )
            )
            {
                if (
                    !definitionsById.TryGetValue(
                        target.DefinitionId,
                        out CardDefinition definition
                    )
                )
                {
                    continue;
                }

                var before = CaptureCard(target, state);
                if (state.Player(target.Owner).TryReturnCardToDrawHand(target, definition))
                    RecordJournalCard(state, triggeredAbility, effect.Kind, before, CaptureCard(target, state));
            }
        }

        private void ExecuteReturnToReserve(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect
        )
        {
            foreach (
                CardInstance target in ResolveCardTargets(
                    state,
                    triggeredAbility,
                    selection,
                    effect
                )
            )
            {
                if (target.Zone == CardZone.Board)
                {
                    var before = CaptureCard(target, state);
                    if (state.Player(target.Owner).TryMoveBoardCardToReserve(target))
                        RecordJournalCard(state, triggeredAbility, effect.Kind, before, CaptureCard(target, state));
                }
            }
        }

        private void ExecuteKillAllUnits(
            MatchState state,
            TriggeredAbility triggeredAbility
        )
        {
            var targets = new List<CardInstance>();

            AddAllActiveCards(state.Host, targets);

            AddAllActiveCards(state.Guest, targets);

            foreach (CardInstance target in targets)
            {
                AbilityCardState before = CaptureCard(target, state);
                target.Kill();

                if (before.Health > 0)
                {
                    RecordCardEffect(
                        state,
                        triggeredAbility,
                        EffectKind.KillAllUnits,
                        before,
                        CaptureCard(target, state)
                    );
                }
            }
        }

        private void ExecuteHalfNexus(
            MatchState state,
            TriggeredAbility triggeredAbility,
            EffectDefinition effect
        )
        {
            SeatId targetOwner;

            switch (effect.Target)
            {
                case EffectTarget.AlliedNexus:
                    targetOwner = triggeredAbility.ListenerOwner;
                    break;

                case EffectTarget.EnemyNexus:
                    targetOwner = triggeredAbility.ListenerOwner.Opponent();
                    break;

                default:
                    throw new InvalidOperationException(
                        "HalfNexus must target AlliedNexus " + "or EnemyNexus."
                    );
            }

            PlayerState target = state.Player(targetOwner);

            int healthBefore = target.NexusHealth;
            int damage = target.NexusHealth / 2;

            target.ApplyNexusDamage(damage);

            RecordNexusEffect(
                state,
                triggeredAbility,
                EffectKind.HalfNexus,
                targetOwner,
                healthBefore
            );
        }

        #endregion
    }
}
