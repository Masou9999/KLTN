using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class WeightedDeckBuilderTests
    {
        private static readonly string[] Starter =
        {
            "MaDa", "MaDoi", "MaGa", "MaLon", "QuyMotDo", "VongNhi", "OngKe", "QuyCau",
        };

        private static readonly Dictionary<string, int> Weights = new Dictionary<string, int>
        {
            { "MaDa", 3 }, { "MaDoi", 3 }, { "MaGa", 3 }, { "MaLon", 3 },
            { "QuyMotDo", 3 }, { "VongNhi", 3 }, { "OngKe", 3 }, { "QuyCau", 3 },
            { "MaMatMam", 3 }, { "MaLai", 3 }, { "ThienLinhCai", 2 }, { "AmBinh", 2 },
            { "MaCo", 2 }, { "MaCangSung", 2 }, { "MaTroi", 2 }, { "ThanTrung", 2 },
            { "LinhMieu", 1 }, { "MaTranh", 1 }, { "QuyDaXoa", 1 },
        };

        [Test]
        public void Build_StarterSet_Has30CardsAndEveryOwnedCard()
        {
            IReadOnlyList<CardDefinition> deck = Build(Starter);

            Dictionary<string, int> counts = Count(deck);

            Assert.AreEqual(30, deck.Count);
            Assert.AreEqual(Starter.Length, counts.Count);

            foreach (string id in Starter)
            {
                Assert.GreaterOrEqual(counts[id], 1, id);
                Assert.LessOrEqual(counts[id], 4, id);
            }
        }

        [Test]
        public void Build_FullCollection_WeakCardsHaveAtLeastAsManyCopiesAsStrongCards()
        {
            var all = new List<string>(Weights.Keys);

            Dictionary<string, int> counts = Count(Build(all));

            int minimumWeak = int.MaxValue;
            int maximumStrong = 0;

            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (Weights[pair.Key] == 3)
                {
                    minimumWeak = Math.Min(minimumWeak, pair.Value);
                }
                else if (Weights[pair.Key] == 1)
                {
                    maximumStrong = Math.Max(maximumStrong, pair.Value);
                }
            }

            Assert.AreEqual(Weights.Count, counts.Count);
            Assert.GreaterOrEqual(minimumWeak, maximumStrong);
            Assert.AreEqual(1, maximumStrong);
        }

        [Test]
        public void Build_RespectsCardSpecificLimit()
        {
            var owned = new List<string>(Starter) { "AmBinh" };

            Dictionary<string, int> counts = Count(Build(owned));

            Assert.AreEqual(1, counts["AmBinh"]);
        }

        [Test]
        public void Build_NeverAddsCardsOutsideOwnedSet()
        {
            var owned = new List<string>(Starter) { "QuyDaXoa" };

            foreach (CardDefinition definition in Build(owned))
            {
                Assert.Contains(definition.Id, owned);
            }
        }

        [Test]
        public void Build_IsDeterministicForSameOwnedSet()
        {
            var forward = new List<string>(Starter);
            var reversed = new List<string>(Starter);
            reversed.Reverse();

            CollectionAssert.AreEqual(Count(Build(forward)), Count(Build(reversed)));
        }

        [Test]
        public void Build_ThrowsWhenOwnedCardsCannotFillDeck()
        {
            Assert.Throws<InvalidOperationException>(() => Build(new[] { "MaDa", "MaGa" }));
        }

        private static IReadOnlyList<CardDefinition> Build(IEnumerable<string> ids)
        {
            var definitions = new List<CardDefinition>();

            foreach (string id in ids)
            {
                definitions.Add(
                    new CardDefinition(id, id, 1, 1, 1, maximumCopiesPerDeck: id == "AmBinh" ? 1 : 0)
                );
            }

            return WeightedDeckBuilder.Build(definitions, id => Weights[id]);
        }

        private static Dictionary<string, int> Count(IReadOnlyList<CardDefinition> deck)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (CardDefinition definition in deck)
            {
                counts.TryGetValue(definition.Id, out int current);
                counts[definition.Id] = current + 1;
            }

            return counts;
        }
    }
}
