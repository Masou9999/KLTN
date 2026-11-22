using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Chooses which deck card a player draws. Only cards whose
    /// <see cref="CardDefinition.DrawCost"/> is at most the player's max mana plus
    /// <see cref="CostAllowance"/> can be drawn, uniformly at random. When none remain,
    /// the limit widens (+3, +4, ...) until the cheapest remaining cards qualify, so a
    /// draw never fails while the deck still has cards.
    /// </summary>
    public static class CardDrawPolicy
    {
        #region Constants

        public const int CostAllowance = 2;

        #endregion

        #region Queries

        /// <summary>
        /// Returns the index in <paramref name="deck"/> of the card to draw.
        /// Candidates are visited in deck order, so the same deck and random sequence
        /// always yield the same card.
        /// </summary>
        /// <param name="definitionOf">
        /// Definition lookup; cards with an unknown definition are always drawable.
        /// </param>
        /// <returns>-1 when the deck is empty.</returns>
        public static int PickIndex(
            IReadOnlyList<CardInstance> deck,
            int maxMana,
            Func<string, CardDefinition> definitionOf,
            IRandomSource random
        )
        {
            if (deck == null)
            {
                throw new ArgumentNullException(nameof(deck));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            if (deck.Count == 0)
            {
                return -1;
            }

            int lowestCost = int.MaxValue;

            for (int i = 0; i < deck.Count; i++)
            {
                lowestCost = Math.Min(lowestCost, DrawCostOf(deck[i], definitionOf));
            }

            int limit = DrawLimit(maxMana, lowestCost);
            var candidates = new List<int>(deck.Count);

            for (int i = 0; i < deck.Count; i++)
            {
                if (DrawCostOf(deck[i], definitionOf) <= limit)
                {
                    candidates.Add(i);
                }
            }

            return candidates[random.Next(0, candidates.Count)];
        }

        /// <summary>
        /// Highest draw cost allowed for a player with <paramref name="maxMana"/>,
        /// widened to <paramref name="lowestRemainingCost"/> when nothing cheaper is left.
        /// </summary>
        public static int DrawLimit(int maxMana, int lowestRemainingCost)
        {
            return Math.Max(maxMana + CostAllowance, lowestRemainingCost);
        }

        #endregion

        #region Helpers

        private static int DrawCostOf(
            CardInstance card,
            Func<string, CardDefinition> definitionOf
        )
        {
            CardDefinition definition = definitionOf?.Invoke(card.DefinitionId);

            return definition?.DrawCost ?? 0;
        }

        #endregion
    }
}
