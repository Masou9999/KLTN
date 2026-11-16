using System.Collections.Generic;
using KLTN.Game.Domain;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class MatchFactoryTests
    {
        #region Creation Tests

        [Test]
        public void Create_DealsOpeningHandToBothPlayers()
        {
            var factory = new MatchFactory(new SeededRandomSource(123));

            IReadOnlyList<CardDefinition> deck = DefaultDeckBuilder.Build(Definitions());

            MatchState state = factory.Create(deck, deck, 4);

            Assert.AreEqual(4, state.Host.DrawHand.Count);
            Assert.AreEqual(4, state.Guest.DrawHand.Count);

            Assert.AreEqual(26, state.Host.Deck.Count);
            Assert.AreEqual(26, state.Guest.Deck.Count);
        }

        [Test]
        public void Create_AssignsUniqueInstanceIds()
        {
            var factory = new MatchFactory(new SeededRandomSource(123));

            IReadOnlyList<CardDefinition> deck = DefaultDeckBuilder.Build(Definitions());

            MatchState state = factory.Create(deck, deck, 4);

            var ids = new HashSet<ulong>();

            AddPlayerCards(state.Host, ids);
            AddPlayerCards(state.Guest, ids);

            Assert.AreEqual(DeckRules.RequiredCardCount * 2, ids.Count);
        }

        [Test]
        public void DamageOnOneInstance_DoesNotChangeAnotherInstance()
        {
            var factory = new MatchFactory(new SeededRandomSource(123));

            IReadOnlyList<CardDefinition> deck = DefaultDeckBuilder.Build(Definitions());

            MatchState state = factory.Create(deck, deck, 4);

            CardInstance hostCard = state.Host.DrawHand[0];
            CardInstance guestCard = state.Guest.DrawHand[0];

            int guestHealthBefore = guestCard.CurrentHealth;

            hostCard.ApplyDamage(1);

            Assert.AreEqual(guestHealthBefore, guestCard.CurrentHealth);
        }

        #endregion

        #region Test Data and Helpers

        private static List<CardDefinition> Definitions()
        {
            return new List<CardDefinition>
            {
                new CardDefinition("ma_co", "Ma Cơ", 3, 2, 3),
                new CardDefinition("ma_da", "Ma Da", 1, 3, 2),
            };
        }

        private static void AddPlayerCards(PlayerState player, HashSet<ulong> ids)
        {
            foreach (CardInstance card in player.Deck)
            {
                Assert.IsTrue(ids.Add(card.InstanceId));
            }

            foreach (CardInstance card in player.DrawHand)
            {
                Assert.IsTrue(ids.Add(card.InstanceId));
            }

            foreach (CardInstance card in player.Board)
            {
                Assert.IsTrue(ids.Add(card.InstanceId));
            }

            foreach (CardInstance card in player.Graveyard)
            {
                Assert.IsTrue(ids.Add(card.InstanceId));
            }
            foreach (CardInstance card in player.Reserve)
            {
                Assert.IsTrue(ids.Add(card.InstanceId));
            }
        }

        #endregion
    }
}
