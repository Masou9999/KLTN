using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public interface IRandomSource
    {
        int Next(int minInclusive, int maxExclusive);
    }

    public sealed class SeededRandomSource : IRandomSource
    {
        private readonly Random random;

        public SeededRandomSource(int seed)
        {
            random = new Random(seed);
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            return random.Next(minInclusive, maxExclusive);
        }
    }

    /// <summary>
    /// Creates a match from two legal decks. Decks are not shuffled: every draw (opening
    /// hand, mulligan, round draw, draw effects) picks a random eligible card through
    /// <see cref="CardDrawPolicy"/> using that seat's own random source.
    /// </summary>
    public sealed class MatchFactory
    {
        private readonly IRandomSource hostDrawRandom;
        private readonly IRandomSource guestDrawRandom;

        /// <summary>Both seats draw from one shared random source (tests and sandboxes).</summary>
        public MatchFactory(IRandomSource random)
            : this(random, random) { }

        public MatchFactory(IRandomSource hostDrawRandom, IRandomSource guestDrawRandom)
        {
            this.hostDrawRandom =
                hostDrawRandom ?? throw new ArgumentNullException(nameof(hostDrawRandom));
            this.guestDrawRandom =
                guestDrawRandom ?? throw new ArgumentNullException(nameof(guestDrawRandom));
        }

        public MatchState Create(
            IReadOnlyList<CardDefinition> hostDeckDefinitions,
            IReadOnlyList<CardDefinition> guestDeckDefinitions,
            int openingHandSize,
            SeatId firstSeat = SeatId.Host
        )
        {
            DeckRules.Validate(hostDeckDefinitions, "Host deck");

            DeckRules.Validate(guestDeckDefinitions, "Guest deck");

            if (openingHandSize < 0 || openingHandSize > DeckRules.RequiredCardCount)
            {
                throw new ArgumentOutOfRangeException(nameof(openingHandSize));
            }

            var state = new MatchState(firstSeat);

            state.Host.InitializeMana(1);
            state.Guest.InitializeMana(1);

            ulong nextInstanceId = 1;

            BuildDeck(state.Host, hostDeckDefinitions, ref nextInstanceId);

            BuildDeck(state.Guest, guestDeckDefinitions, ref nextInstanceId);

            state.EnsureNextCardInstanceIdAtLeast(nextInstanceId);

            Func<string, CardDefinition> definitionOf = CreateLookup(
                hostDeckDefinitions,
                guestDeckDefinitions
            );

            state.Host.ConfigureDraws(hostDrawRandom, definitionOf);
            state.Guest.ConfigureDraws(guestDrawRandom, definitionOf);

            DrawCards(state.Host, openingHandSize);
            DrawCards(state.Guest, openingHandSize);

            return state;
        }

        private static void BuildDeck(
            PlayerState player,
            IReadOnlyList<CardDefinition> definitions,
            ref ulong nextInstanceId
        )
        {
            foreach (CardDefinition definition in definitions)
            {
                player.Deck.Add(
                    new CardInstance(
                        nextInstanceId++,
                        definition.Id,
                        player.Seat,
                        CardZone.Deck,
                        definition.BaseHealth
                    )
                );
            }
        }

        private static Func<string, CardDefinition> CreateLookup(
            IReadOnlyList<CardDefinition> hostDeck,
            IReadOnlyList<CardDefinition> guestDeck
        )
        {
            var byId = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);

            foreach (CardDefinition definition in hostDeck)
            {
                byId[definition.Id] = definition;
            }

            foreach (CardDefinition definition in guestDeck)
            {
                byId[definition.Id] = definition;
            }

            return id => byId.TryGetValue(id, out CardDefinition found) ? found : null;
        }

        private static void DrawCards(PlayerState player, int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                if (!player.DrawOne())
                {
                    return;
                }
            }
        }
    }
}
