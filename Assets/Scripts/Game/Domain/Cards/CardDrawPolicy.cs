using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Chooses which deck card a player draws. Only cards whose
    /// <see cref="CardDefinition.DrawCost"/> is at most the player's max mana plus
    /// <see cref="CostAllowance"/> can be drawn. Cards the player can already afford are
    /// favoured: weight 4 at or below max mana, 2 at +1, 1 at +2 (i.e. 1 : 0.5 : 0.25).
    /// When none remain, the limit widens (+3, +4, ...) until the cheapest remaining
    /// cards qualify, so a draw never fails while the deck still has cards.
    /// </summary>
    public static class CardDrawPolicy
    {
        #region Constants

        public const int CostAllowance = 2;

        // Trọng số nguyên để dùng IRandomSource.Next: <= mana : +1 : +2 = 4 : 2 : 1.
        public const int AffordableWeight = 4;
        public const int OneAboveWeight = 2;
        public const int StretchWeight = 1;

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
            int totalWeight = 0;

            for (int i = 0; i < deck.Count; i++)
            {
                totalWeight += WeightOf(DrawCostOf(deck[i], definitionOf), maxMana, limit);
            }

            int roll = random.Next(0, totalWeight);

            for (int i = 0; i < deck.Count; i++)
            {
                roll -= WeightOf(DrawCostOf(deck[i], definitionOf), maxMana, limit);

                if (roll < 0)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("Weighted draw did not select a card.");
        }

        /// <summary>
        /// Relative chance of a card with <paramref name="drawCost"/> being drawn; 0 when it
        /// is above <paramref name="limit"/>. Cards above max mana + 2 only qualify after the
        /// limit widened, when they are all equally the cheapest left.
        /// </summary>
        public static int WeightOf(int drawCost, int maxMana, int limit)
        {
            if (drawCost > limit)
            {
                return 0;
            }

            if (drawCost <= maxMana)
            {
                return AffordableWeight;
            }

            return drawCost == maxMana + 1 ? OneAboveWeight : StretchWeight;
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
