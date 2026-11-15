using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Interprets data-driven effect operations. It never switches on card identity;
    /// definitions select an operation kind, targets and duration at authoring time.
    /// </summary>
    public sealed partial class AbilityEffectExecutor
    {
        #region Fields

        private readonly IReadOnlyDictionary<string, CardDefinition> definitionsById;
        private readonly IRandomSource random;

        #endregion

        #region Construction

        public AbilityEffectExecutor(
            IReadOnlyDictionary<string, CardDefinition> definitionsById,
            IRandomSource random = null
        )
        {
            this.definitionsById =
                definitionsById
                ?? throw new ArgumentNullException(nameof(definitionsById));

            this.random = random ?? new SeededRandomSource(0);
        }

        #endregion

        #region Execution

        /// <summary>
        /// Resolves every effect in definition order and moves newly dead units after
        /// each operation so Death and AllyDeath triggers observe deterministic state.
        /// </summary>
        public GameEventBatch Execute(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection
        )
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (triggeredAbility == null)
            {
                throw new ArgumentNullException(nameof(triggeredAbility));
            }

            var emittedEvents = new List<GameEvent>();

            foreach (EffectDefinition effect in triggeredAbility.Ability.Effects)
            {
                ExecuteEffect(state, triggeredAbility, selection, effect, emittedEvents);

                GameEventBatch deathEvents = MoveDeadCardsAndBuildEvents(state);

                foreach (GameEvent deathEvent in deathEvents.Events)
                {
                    emittedEvents.Add(deathEvent);
                }
            }

            return new GameEventBatch(emittedEvents);
        }

        private void ExecuteEffect(
            MatchState state,
            TriggeredAbility triggeredAbility,
            AbilityTargetSelection selection,
            EffectDefinition effect,
            List<GameEvent> emittedEvents
        )
        {
            switch (effect.Kind)
            {
                case EffectKind.Damage:
                    ExecuteDamage(state, triggeredAbility, selection, effect);
                    return;

                case EffectKind.Heal:
                    ExecuteHeal(state, triggeredAbility, selection, effect);
                    return;

                case EffectKind.Kill:
                case EffectKind.Sacrifice:
                    ExecuteKill(state, triggeredAbility, selection, effect);
                    return;

                case EffectKind.Draw:
                    ExecuteDraw(state, triggeredAbility, effect.Amount);
                    return;

                case EffectKind.Buff:
                    ExecuteBuff(state, triggeredAbility, selection, effect);
                    return;

                case EffectKind.Revive:
                    ExecuteRevive(
                        state,
                        triggeredAbility,
                        selection,
                        effect,
                        emittedEvents
                    );
                    return;

                case EffectKind.Copy:
                    ExecuteCopy(state, triggeredAbility, selection, effect);
                    return;

                case EffectKind.ReduceCost:
                    ExecuteReduceCost(state, triggeredAbility, selection, effect);
                    return;

                case EffectKind.ReturnToDrawHand:
                    ExecuteReturnToDrawHand(state, triggeredAbility, selection, effect);
                    return;

                case EffectKind.ReturnToReserve:
                    ExecuteReturnToReserve(state, triggeredAbility, selection, effect);
                    return;

                case EffectKind.KillAllUnits:
                    ExecuteKillAllUnits(state, triggeredAbility);
                    return;

                case EffectKind.HalfNexus:
                    ExecuteHalfNexus(state, triggeredAbility, effect);
                    return;

                case EffectKind.Summon:
                    ExecuteSummon(state, triggeredAbility, effect, emittedEvents);
                    return;

                case EffectKind.CounterSpell:
                    throw new InvalidOperationException(
                        "CounterSpell requires the spell stack, "
                            + "which has not been implemented yet."
                    );

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(effect.Kind),
                        effect.Kind,
                        "Unknown effect kind."
                    );
            }
        }

        #endregion
    }
}
