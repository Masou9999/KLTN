using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Builds a legal deck from the cards a player owns. Weaker cards get a larger
    /// weight, so they receive more copies than stronger cards. Every owned card gets
    /// at least one copy. Leftover copies go to the cards furthest below their quota;
    /// ties go to the higher weight, then the lower power score, then the card ID, so the
    /// result never depends on how a card happens to be named unless everything else ties.
    /// The result is grouped by card; draws pick from it through CardDrawPolicy.
    /// </summary>
    public static class WeightedDeckBuilder
    {
        public const int DefaultMaximumCopiesPerCard = 4;

        public static IReadOnlyList<CardDefinition> Build(
            IReadOnlyList<CardDefinition> ownedCards,
            Func<string, int> weightOf,
            int deckSize = DeckRules.RequiredCardCount,
            int maximumCopiesPerCard = DefaultMaximumCopiesPerCard,
            Func<string, float> powerOf = null
        )
        {
            if (ownedCards == null || ownedCards.Count == 0)
            {
                throw new ArgumentException(
                    "At least one owned card is required.",
                    nameof(ownedCards)
                );
            }

            if (weightOf == null)
            {
                throw new ArgumentNullException(nameof(weightOf));
            }

            if (maximumCopiesPerCard < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCopiesPerCard));
            }

            List<Entry> entries = CreateEntries(
                ownedCards,
                weightOf,
                powerOf,
                maximumCopiesPerCard
            );

            if (entries.Count > deckSize)
            {
                throw new InvalidOperationException(
                    $"Cannot give one copy to each of {entries.Count} owned cards "
                        + $"in a {deckSize}-card deck."
                );
            }

            int capacity = 0;

            foreach (Entry entry in entries)
            {
                capacity += entry.Limit;
            }

            if (capacity < deckSize)
            {
                throw new InvalidOperationException(
                    $"Owned cards can provide at most {capacity} copies, "
                        + $"but the deck needs {deckSize}."
                );
            }

            AssignQuotas(entries, deckSize);
            Distribute(entries, deckSize);

            var deck = new List<CardDefinition>(deckSize);

            foreach (Entry entry in entries)
            {
                for (int i = 0; i < entry.Copies; i++)
                {
                    deck.Add(entry.Definition);
                }
            }

            if (deckSize == DeckRules.RequiredCardCount)
            {
                DeckRules.Validate(deck, "Weighted deck");
            }

            return deck.AsReadOnly();
        }

        #region Algorithm

        private sealed class Entry
        {
            public CardDefinition Definition;
            public int Weight;
            public float Power;
            public int Limit;
            public double Quota;
            public int Copies;
        }

        private static List<Entry> CreateEntries(
            IReadOnlyList<CardDefinition> ownedCards,
            Func<string, int> weightOf,
            Func<string, float> powerOf,
            int maximumCopiesPerCard
        )
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<Entry>(ownedCards.Count);

            foreach (CardDefinition definition in ownedCards)
            {
                if (definition == null)
                {
                    throw new ArgumentException("Owned cards cannot contain null values.");
                }

                if (!seen.Add(definition.Id))
                {
                    continue;
                }

                int limit = maximumCopiesPerCard;

                if (definition.MaximumCopiesPerDeck > 0)
                {
                    limit = Math.Min(limit, definition.MaximumCopiesPerDeck);
                }

                entries.Add(
                    new Entry
                    {
                        Definition = definition,
                        Weight = Math.Max(1, weightOf(definition.Id)),
                        Power = powerOf?.Invoke(definition.Id) ?? 0f,
                        Limit = limit,
                    }
                );
            }

            // Deterministic order: same owned set always yields the same copy counts.
            entries.Sort(
                (left, right) =>
                    StringComparer.Ordinal.Compare(left.Definition.Id, right.Definition.Id)
            );

            return entries;
        }

        private static void AssignQuotas(List<Entry> entries, int deckSize)
        {
            int totalWeight = 0;

            foreach (Entry entry in entries)
            {
                totalWeight += entry.Weight;
            }

            foreach (Entry entry in entries)
            {
                entry.Quota = (double)deckSize * entry.Weight / totalWeight;
                entry.Copies = Clamp((int)Math.Floor(entry.Quota), 1, entry.Limit);
            }
        }

        private static void Distribute(List<Entry> entries, int deckSize)
        {
            int total = 0;

            foreach (Entry entry in entries)
            {
                total += entry.Copies;
            }

            // Largest remainder: give extra copies to the cards furthest below quota.
            // Ties favour the higher weight, then the lower power score (weaker card).
            while (total < deckSize)
            {
                Entry best = null;

                foreach (Entry entry in entries)
                {
                    if (entry.Copies >= entry.Limit)
                    {
                        continue;
                    }

                    if (best == null || CompareForAdd(entry, best) > 0)
                    {
                        best = entry;
                    }
                }

                best.Copies++;
                total++;
            }

            // Remove copies from cards furthest above quota.
            // Ties remove from the lower weight, then the higher power score (stronger card).
            while (total > deckSize)
            {
                Entry best = null;

                foreach (Entry entry in entries)
                {
                    if (entry.Copies <= 1)
                    {
                        continue;
                    }

                    if (best == null || CompareForRemove(entry, best) > 0)
                    {
                        best = entry;
                    }
                }

                best.Copies--;
                total--;
            }
        }

        private static int CompareForAdd(Entry left, Entry right)
        {
            int deficit = (left.Quota - left.Copies).CompareTo(right.Quota - right.Copies);

            if (deficit != 0)
            {
                return deficit;
            }

            int weight = left.Weight.CompareTo(right.Weight);

            if (weight != 0)
            {
                return weight;
            }

            return right.Power.CompareTo(left.Power);
        }

        private static int CompareForRemove(Entry left, Entry right)
        {
            int excess = (left.Copies - left.Quota).CompareTo(right.Copies - right.Quota);

            if (excess != 0)
            {
                return excess;
            }

            int weight = right.Weight.CompareTo(left.Weight);

            if (weight != 0)
            {
                return weight;
            }

            return left.Power.CompareTo(right.Power);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        #endregion
    }
}
