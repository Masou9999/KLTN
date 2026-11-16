using System.Collections.Generic;
using System.Linq;
using KLTN.Game.Domain;
using NUnit.Framework;

namespace KLTN.Game.Tests
{
    /// <summary>Death entitlements survive revival, independent of the living instance.</summary>
    public sealed class DeathCompanionTests
    {
        #region Fixture

        private CardDefinition companion;
        private CardDefinition ephemeral;
        private CardDefinition ordinary;
        private Dictionary<string, CardDefinition> definitions;
        private MatchState state;
        private UnitPassiveRuleResolver resolver;

        [SetUp]
        public void SetUp()
        {
            companion = new CardDefinition("companion", "Companion", 1, 3, 2,
                UnitKeyword.Ephemeral | UnitKeyword.CannotBlock,
                passiveRules: new UnitPassiveRules(
                    joinsAttackFromGraveyard: true, requiresEphemeralAttacker: true));
            ephemeral = new CardDefinition("ephemeral", "Ephemeral", 3, 1, 1,
                UnitKeyword.Ephemeral);
            ordinary = new CardDefinition("ordinary", "Ordinary", 3, 1, 1);
            definitions = new[] { companion, ephemeral, ordinary }
                .ToDictionary(definition => definition.Id);
            var deck = Enumerable.Repeat(ordinary, DeckRules.RequiredCardCount).ToArray();
            state = new MatchFactory(new SeededRandomSource(17)).Create(deck, deck, 0);
            state.HostMulliganDone = state.GuestMulliganDone = true;
            state.BeginFirstRound();
            resolver = new UnitPassiveRuleResolver(definitions);
        }

        private CardInstance Add(CardDefinition definition, SeatId owner = SeatId.Host)
        {
            var card = new CardInstance(state.AllocateCardInstanceId(), definition.Id,
                owner, CardZone.Reserve, definition.BaseHealth);
            state.Player(owner).Reserve.Add(card);
            return card;
        }

        private void Kill(CardInstance card)
        {
            card.Kill();
            Assert.IsTrue(state.Player(card.Owner).TryMoveDeadActiveCardToGraveyard(card));
            state.RoundHistory.RecordDeath(card, companion.BaseDamage,
                companion.BaseHealth);
        }

        private void KillAndRevive(CardInstance target)
        {
            CardInstance source = Add(ordinary);
            var ability = new AbilityDefinition("kill_then_revive", AbilityTrigger.Play,
                new[]
                {
                    new EffectDefinition(EffectKind.Kill, EffectTarget.PrimarySelection),
                    new EffectDefinition(EffectKind.Revive, EffectTarget.PrimarySelection),
                });
            var triggered = new TriggeredAbility(source.InstanceId, source.Owner, ability,
                GameEvent.FromCard(GameEventType.UnitPlayed, state.RoundNumber, source));
            new AbilityEffectExecutor(definitions).Execute(state, triggered,
                new AbilityTargetSelection(new[] { target.InstanceId }, null));
            state.Host.Reserve.Remove(source);
        }

        #endregion

        #region Death And Revival

        [Test]
        public void RoundEnd_PublishesDeathBeforeRoundStart_WithoutExpiringReserve()
        {
            CardInstance board = Add(companion);
            CardInstance reserve = Add(companion);
            state.Host.TryMoveReserveCardToBoard(board, 0);
            var engine = new MatchRulesEngine(definitions, new SeededRandomSource(19));
            Assert.IsTrue(engine.TryPass(state, SeatId.Host).Accepted);
            Assert.IsTrue(engine.TryPass(state, SeatId.Guest).Accepted);
            AbilityResolution resolution = state.TakeAbilityResolution();
            Assert.AreEqual(1, resolution.Events.Count);
            Assert.IsTrue(resolution.Events[0].IsDeathOnly);
            Assert.AreEqual(CardZone.Board, resolution.Events[0].TargetBefore.Zone);
            Assert.AreEqual(CardZone.Graveyard, resolution.Events[0].TargetAfter.Zone);
            Assert.AreEqual(CardZone.Reserve, reserve.Zone);
            Assert.IsFalse(reserve.IsDead);
            Assert.AreEqual(1, state.RoundHistory.AllDeaths.Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KillThenRevive_PreservesDeathAndLivingCard(bool onBoard)
        {
            CardInstance target = Add(companion);
            if (onBoard) Assert.IsTrue(state.Host.TryMoveReserveCardToBoard(target, 1));
            KillAndRevive(target);
            Assert.AreEqual(CardZone.Reserve, target.Zone);
            Assert.IsFalse(target.IsDead);
            Assert.AreEqual(1, state.RoundHistory.AllDeaths.Count);
            AbilityResolution animation = state.TakeAbilityResolution();
            Assert.AreEqual(EffectKind.Kill, animation.Events[0].Kind);
            Assert.AreEqual(onBoard ? CardZone.Board : CardZone.Reserve,
                animation.Events[0].TargetBefore.Zone);
            Assert.AreEqual(EffectKind.Revive, animation.Events[1].Kind);

            CardInstance attacker = Add(ephemeral);
            Assert.IsTrue(state.Host.TryMoveReserveCardToBoard(attacker, 0));
            var attackers = new[] { attacker, null, null };
            resolver.JoinAttackFromGraveyard(state, SeatId.Host, attackers);
            Assert.IsNotNull(attackers[1]);
            Assert.AreNotEqual(target.InstanceId, attackers[1].InstanceId);
            Assert.AreEqual(CardZone.Reserve, target.Zone);
            Assert.IsFalse(target.IsDead);
            Assert.IsNull(attackers[2]);
            resolver.JoinAttackFromGraveyard(state, SeatId.Host, attackers);
            Assert.IsNull(attackers[2], "A consumed death cannot create another copy.");
        }

        [Test]
        public void ReserveDeath_ReusesDeadInstance_AndNewDeathCanJoinAgain()
        {
            CardInstance target = Add(companion);
            Kill(target);
            CardInstance attacker = Add(ephemeral);
            state.Host.TryMoveReserveCardToBoard(attacker, 0);
            var attackers = new[] { attacker, null, null };
            resolver.JoinAttackFromGraveyard(state, SeatId.Host, attackers);
            Assert.AreSame(target, attackers[1]);
            Kill(target);
            attackers[1] = null;
            resolver.JoinAttackFromGraveyard(state, SeatId.Host, attackers);
            Assert.AreSame(target, attackers[1]);
            Assert.IsNull(attackers[2]);
        }

        [Test]
        public void NoEphemeralOrFullBoard_DoesNotConsumeDeathAcrossRounds()
        {
            CardInstance target = Add(companion);
            KillAndRevive(target);
            CardInstance attacker = Add(ordinary);
            state.Host.TryMoveReserveCardToBoard(attacker, 0);
            var attackers = new[] { attacker, null, null };
            resolver.JoinAttackFromGraveyard(state, SeatId.Host, attackers);
            Assert.IsNull(attackers[1]);
            state.Host.ReturnBoardCardsToReserve();
            state.Host.Reserve.Remove(attacker);
            attacker = Add(ephemeral);
            state.Host.TryMoveReserveCardToBoard(attacker, 0);
            CardInstance second = Add(ordinary);
            CardInstance third = Add(ordinary);
            state.Host.TryMoveReserveCardToBoard(second, 1);
            state.Host.TryMoveReserveCardToBoard(third, 2);
            resolver.JoinAttackFromGraveyard(state, SeatId.Host,
                new[] { attacker, second, third });
            Assert.IsFalse(state.RoundHistory.HasJoinedAttack(state.RoundHistory.AllDeaths[0]));
            state.RoundHistory.AdvanceToRound(2);
            state.Host.Board.Remove(third);
            attackers = new[] { attacker, second, null };
            resolver.JoinAttackFromGraveyard(state, SeatId.Host, attackers);
            Assert.AreEqual(companion.Id, attackers[2].DefinitionId);
        }

        [Test]
        public void MultipleDeaths_CreateExactlyOneCompanionPerDeath()
        {
            CardInstance target = Add(companion);
            KillAndRevive(target);
            KillAndRevive(target);
            CardInstance enemy = Add(companion, SeatId.Guest);
            Kill(enemy);
            CardInstance attacker = Add(ephemeral);
            state.Host.TryMoveReserveCardToBoard(attacker, 0);
            var attackers = new[] { attacker, null, null };
            resolver.JoinAttackFromGraveyard(state, SeatId.Host, attackers);
            Assert.IsNotNull(attackers[1]);
            Assert.IsNotNull(attackers[2]);
            Assert.AreNotEqual(attackers[1].InstanceId, attackers[2].InstanceId);
            Assert.AreEqual(CardZone.Reserve, target.Zone);
            Assert.IsFalse(state.RoundHistory.HasJoinedAttack(state.RoundHistory.AllDeaths[2]));
        }

        #endregion
    }
}
