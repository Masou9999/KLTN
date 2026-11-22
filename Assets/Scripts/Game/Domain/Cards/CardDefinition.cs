using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Immutable card definition shared by all card instances.
    /// </summary>
    public sealed class CardDefinition
    {
        #region Properties

        public string Id { get; }
        public string DisplayName { get; }

        public int BaseHealth { get; }
        public int BaseDamage { get; }
        public int Cost { get; }

        public int MaximumCopiesPerDeck { get; }

        /// <summary>
        /// Cost used only by <see cref="CardDrawPolicy"/> to decide when the card may be
        /// drawn. Equals <see cref="Cost"/> unless the card overrides it (e.g. a unit that
        /// becomes cheaper during the match).
        /// </summary>
        public int DrawCost { get; }

        public UnitKeyword Keywords { get; }
        public IReadOnlyList<AbilityDefinition> Abilities { get; }

        public UnitPassiveRules PassiveRules { get; }

        public string RulesText { get; }

        #endregion

        #region Construction

        public CardDefinition(
            string id,
            string displayName,
            int baseHealth,
            int baseDamage,
            int cost,
            UnitKeyword keywords = UnitKeyword.None,
            IReadOnlyList<AbilityDefinition> abilities = null,
            string rulesText = "",
            UnitPassiveRules passiveRules = null,
            int maximumCopiesPerDeck = 0,
            int drawCostOverride = 0
        )
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "Card definition ID is required.",
                    nameof(id)
                );
            }

            Id = id;
            DisplayName = displayName ?? id;

            BaseHealth = Math.Max(0, baseHealth);
            BaseDamage = Math.Max(0, baseDamage);
            Cost = Math.Max(0, cost);

            Keywords = keywords;
            Abilities = CopyAbilities(abilities);
            RulesText = rulesText ?? string.Empty;

            if (maximumCopiesPerDeck < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCopiesPerDeck),
                    "Maximum copies per deck cannot be negative."
                );
            }

            MaximumCopiesPerDeck = maximumCopiesPerDeck;

            if (drawCostOverride < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(drawCostOverride),
                    "Draw cost override cannot be negative."
                );
            }

            // 0 nghĩa là không ghi đè: lọc rút bài theo mana thật của lá.
            DrawCost = drawCostOverride > 0 ? drawCostOverride : Cost;

            PassiveRules = passiveRules ?? UnitPassiveRules.None;
        }

        private static IReadOnlyList<AbilityDefinition> CopyAbilities(
            IReadOnlyList<AbilityDefinition> abilities
        )
        {
            if (abilities == null || abilities.Count == 0)
            {
                return Array.Empty<AbilityDefinition>();
            }

            var copy = new AbilityDefinition[abilities.Count];

            for (int i = 0; i < abilities.Count; i++)
            {
                if (abilities[i] == null)
                {
                    throw new ArgumentException(
                        "Abilities cannot contain null values.",
                        nameof(abilities)
                    );
                }

                copy[i] = abilities[i];
            }

            return Array.AsReadOnly(copy);
        }

        #endregion
    }
}
