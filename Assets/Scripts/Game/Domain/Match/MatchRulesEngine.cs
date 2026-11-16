using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Host-authoritative facade for validating player intents and advancing the
    /// deterministic match state machine. The partial files group commands by use case;
    /// all gameplay mutations still pass through this single rules boundary.
    /// </summary>
    public sealed partial class MatchRulesEngine
    {
        #region Constants

        private const int MaximumMana = 10;

        /// <summary>
        /// Minimum current Power required to block a Fearsome attacker. Presentation
        /// uses the same value for immediate drag validation; the host still validates
        /// the submitted command authoritatively.
        /// </summary>
        public const int FearsomeMinimumBlockerDamage = 3;

        #endregion

        #region Fields

        private readonly IReadOnlyDictionary<string, CardDefinition> definitionsById;

        private readonly AbilityTargetValidator abilityTargetValidator;

        private readonly TriggeredAbilityQueue triggeredAbilityQueue;

        private readonly AbilityEffectExecutor abilityEffectExecutor;

        private readonly UnitPassiveRuleResolver unitPassiveRuleResolver;

        #endregion

        #region Construction

        public MatchRulesEngine(
            IReadOnlyDictionary<string, CardDefinition> definitionsById,
            IRandomSource random = null
        )
        {
            this.definitionsById =
                definitionsById
                ?? throw new ArgumentNullException(nameof(definitionsById));

            abilityTargetValidator = new AbilityTargetValidator();

            var triggerResolver = new AbilityTriggerResolver(definitionsById);

            triggeredAbilityQueue = new TriggeredAbilityQueue(triggerResolver);

            abilityEffectExecutor = new AbilityEffectExecutor(definitionsById, random);

            unitPassiveRuleResolver = new UnitPassiveRuleResolver(definitionsById);
        }

        #endregion
    }
}
