using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public static class DeckRules
    {
        public const int RequiredCardCount = 30;

        public static void Validate(
            IReadOnlyList<CardDefinition> deck,
            string deckName = "Deck"
        )
        {
            if (deck == null)
            {
                throw new ArgumentNullException(nameof(deck));
            }

            if (deck.Count != RequiredCardCount)
            {
                throw new InvalidOperationException(
                    $"{deckName} must contain exactly "
                        + $"{RequiredCardCount} cards, but contains {deck.Count}."
                );
            }

            var copiesByDefinitionId = new Dictionary<string, int>(
                StringComparer.Ordinal
            );

            foreach (CardDefinition definition in deck)
            {
                if (definition == null)
                {
                    throw new InvalidOperationException(
                        $"{deckName} contains a null card definition."
                    );
                }

                copiesByDefinitionId.TryGetValue(definition.Id, out int currentCopies);

                int newCopies = currentCopies + 1;

                copiesByDefinitionId[definition.Id] = newCopies;

                int maximumCopies = definition.MaximumCopiesPerDeck;

                if (maximumCopies > 0 && newCopies > maximumCopies)
                {
                    throw new InvalidOperationException(
                        $"{deckName} contains {newCopies} copies of "
                            + $"'{definition.Id}', but the maximum is "
                            + $"{maximumCopies}."
                    );
                }
            }
        }
    }

    /// <summary>
    /// Temporary deterministic deck generation used until player deck
    /// selection is implemented.
    /// </summary>
    public static class DefaultDeckBuilder
    {
        public static IReadOnlyList<CardDefinition> Build(
            IReadOnlyList<CardDefinition> catalog
        )
        {
            if (catalog == null || catalog.Count == 0)
            {
                throw new ArgumentException(
                    "At least one card definition is required.",
                    nameof(catalog)
                );
            }

            var candidates = new List<CardDefinition>(catalog);

            candidates.Sort(
                (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id)
            );

            var copiesByDefinitionId = new Dictionary<string, int>(
                StringComparer.Ordinal
            );

            var deck = new List<CardDefinition>(DeckRules.RequiredCardCount);

            int candidateIndex = 0;
            int consecutiveRejectedCandidates = 0;

            while (deck.Count < DeckRules.RequiredCardCount)
            {
                CardDefinition definition = candidates[candidateIndex % candidates.Count];

                candidateIndex++;

                if (definition == null)
                {
                    throw new InvalidOperationException(
                        "Card catalog contains a null definition."
                    );
                }

                copiesByDefinitionId.TryGetValue(definition.Id, out int currentCopies);

                int maximumCopies = definition.MaximumCopiesPerDeck;

                if (maximumCopies > 0 && currentCopies >= maximumCopies)
                {
                    consecutiveRejectedCandidates++;

                    if (consecutiveRejectedCandidates >= candidates.Count)
                    {
                        throw new InvalidOperationException(
                            "The catalog cannot produce a legal "
                                + $"{DeckRules.RequiredCardCount}-card deck."
                        );
                    }

                    continue;
                }

                consecutiveRejectedCandidates = 0;

                deck.Add(definition);

                copiesByDefinitionId[definition.Id] = currentCopies + 1;
            }

            DeckRules.Validate(deck, "Default deck");

            return deck.AsReadOnly();
        }
    }
}
