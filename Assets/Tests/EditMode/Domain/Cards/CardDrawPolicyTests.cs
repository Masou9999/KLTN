using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class CardDrawPolicyTests
    {
        #region Policy Tests

        [Test]
        public void PickIndex_OnlyPicksCardsWithinMaxManaPlusTwo()
        {
            List<CardInstance> deck = Deck("c1", "c3", "c4", "c9");

            for (int seed = 0; seed < 200; seed++)
            {
                int index = CardDrawPolicy.PickIndex(
                    deck, 1, Lookup, new SeededRandomSource(seed));

                Assert.LessOrEqual(Lookup(deck[index].DefinitionId).DrawCost, 3);
            }
        }

        [Test]
        public void PickIndex_CanPickEveryEligibleCard()
        {
            List<CardInstance> deck = Deck("c1", "c3", "c9");
            var picked = new HashSet<string>();

            for (int seed = 0; seed < 200; seed++)
            {
                int index = CardDrawPolicy.PickIndex(
                    deck, 1, Lookup, new SeededRandomSource(seed));

                picked.Add(deck[index].DefinitionId);
            }

            CollectionAssert.AreEquivalent(new[] { "c1", "c3" }, picked);
        }

        [Test]
        public void PickIndex_WidensToCheapestRemainingWhenNothingIsEligible()
        {
            List<CardInstance> deck = Deck("c9", "c6", "c6b", "c9");

            for (int seed = 0; seed < 200; seed++)
            {
                int index = CardDrawPolicy.PickIndex(
                    deck, 1, Lookup, new SeededRandomSource(seed));

                Assert.AreEqual(6, Lookup(deck[index].DefinitionId).DrawCost);
            }
        }

        [TestCase(2, false)]
        [TestCase(3, true)]
        public void DrawCostOverride_GatesExpensiveCardByOverride(int maxMana, bool expected)
        {
            List<CardInstance> deck = Deck("c1", "troi");
            bool pickedTroi = false;

            for (int seed = 0; seed < 200; seed++)
            {
                int index = CardDrawPolicy.PickIndex(
                    deck, maxMana, Lookup, new SeededRandomSource(seed));

                pickedTroi |= deck[index].DefinitionId == "troi";
            }

            Assert.AreEqual(expected, pickedTroi);
        }

        [Test]
        public void PickIndex_EmptyDeckReturnsMinusOne()
        {
            Assert.AreEqual(
                -1,
                CardDrawPolicy.PickIndex(
                    new List<CardInstance>(), 1, Lookup, new SeededRandomSource(1)));
        }

        [TestCase(3, 4, 6, 4)]
        [TestCase(4, 4, 6, 4)]
        [TestCase(5, 4, 6, 2)]
        [TestCase(6, 4, 6, 1)]
        [TestCase(7, 4, 6, 0)]
        [TestCase(9, 4, 9, 1)]
        public void WeightOf_FavoursAffordableCards(
            int drawCost, int maxMana, int limit, int expected)
        {
            Assert.AreEqual(expected, CardDrawPolicy.WeightOf(drawCost, maxMana, limit));
        }

        [Test]
        public void PickIndex_AffordableCardIsDrawnAboutFourTimesAsOftenAsStretchCard()
        {
            List<CardInstance> deck = Deck("c1", "c3");
            int affordable = 0;
            const int Samples = 4000;
            var random = new SeededRandomSource(42);

            for (int i = 0; i < Samples; i++)
            {
                if (deck[CardDrawPolicy.PickIndex(deck, 1, Lookup, random)].DefinitionId == "c1")
                {
                    affordable++;
                }
            }

            // Trọng số 4 : 1 => kỳ vọng 80%.
            Assert.That(affordable / (double)Samples, Is.InRange(0.75, 0.85));
        }

        #endregion

        #region Match Tests

        [Test]
        public void OpeningHands_OnlyContainCardsWithinManaLimit()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                MatchState state = new MatchFactory(
                    new SeededRandomSource(seed + 1),
                    new SeededRandomSource(seed + 2)
                ).Create(MixedDeck(), MixedDeck(), 4);

                foreach (CardInstance card in state.Host.DrawHand.Concat(state.Guest.DrawHand))
                {
                    Assert.LessOrEqual(Lookup(card.DefinitionId).DrawCost, 3);
                }
            }
        }

        [Test]
        public void SameSeeds_ProduceSameHands()
        {
            MatchState first = Create(15, 16);
            MatchState second = Create(15, 16);

            CollectionAssert.AreEqual(Ids(first.Host.DrawHand), Ids(second.Host.DrawHand));
            CollectionAssert.AreEqual(Ids(first.Guest.DrawHand), Ids(second.Guest.DrawHand));
        }

        [Test]
        public void SeatStreams_AreIndependent()
        {
            MatchState baseline = Create(15, 16);
            MatchState otherGuest = Create(15, 99);

            CollectionAssert.AreEqual(
                Ids(baseline.Host.DrawHand),
                Ids(otherGuest.Host.DrawHand));
        }

        [Test]
        public void Mulligan_KeepsHandSizeAndDoesNotRedrawReturnedCards()
        {
            MatchState state = Create(15, 16);
            ulong[] replaced = state.Host.DrawHand.Select(card => card.InstanceId).ToArray();

            state.Host.PerformMulligan(replaced);

            Assert.AreEqual(4, state.Host.DrawHand.Count);
            Assert.AreEqual(DeckRules.RequiredCardCount - 4, state.Host.Deck.Count);

            foreach (CardInstance card in state.Host.DrawHand)
            {
                CollectionAssert.DoesNotContain(replaced, card.InstanceId);
                Assert.LessOrEqual(Lookup(card.DefinitionId).DrawCost, 3);
            }
        }

        #endregion

        #region Test Data and Helpers

        private static readonly Dictionary<string, CardDefinition> Definitions =
            new Dictionary<string, CardDefinition>
            {
                { "c1", new CardDefinition("c1", "Cost 1", 1, 1, 1) },
                { "c3", new CardDefinition("c3", "Cost 3", 1, 1, 3) },
                { "c4", new CardDefinition("c4", "Cost 4", 1, 1, 4) },
                { "c6", new CardDefinition("c6", "Cost 6", 1, 1, 6) },
                { "c6b", new CardDefinition("c6b", "Cost 6 B", 1, 1, 6) },
                { "c9", new CardDefinition("c9", "Cost 9", 1, 1, 9) },
                {
                    "troi",
                    new CardDefinition("troi", "Troi", 5, 5, 10, drawCostOverride: 5)
                },
            };

        private static CardDefinition Lookup(string id)
        {
            return Definitions.TryGetValue(id, out CardDefinition definition)
                ? definition
                : null;
        }

        private static List<CardInstance> Deck(params string[] ids)
        {
            var deck = new List<CardInstance>();

            for (int i = 0; i < ids.Length; i++)
            {
                deck.Add(new CardInstance((ulong)(i + 1), ids[i], SeatId.Host,
                    CardZone.Deck, 1));
            }

            return deck;
        }

        private static List<CardDefinition> MixedDeck()
        {
            var deck = new List<CardDefinition>();
            string[] ids = { "c1", "c3", "c4", "c6", "c6b", "c9", "troi" };

            while (deck.Count < DeckRules.RequiredCardCount)
            {
                deck.Add(Definitions[ids[deck.Count % ids.Length]]);
            }

            return deck;
        }

        private static MatchState Create(int hostSeed, int guestSeed)
        {
            return new MatchFactory(
                new SeededRandomSource(hostSeed),
                new SeededRandomSource(guestSeed)
            ).Create(MixedDeck(), MixedDeck(), 4);
        }

        private static List<string> Ids(IEnumerable<CardInstance> cards)
        {
            return cards.Select(card => card.DefinitionId).ToList();
        }

        #endregion
    }
}
