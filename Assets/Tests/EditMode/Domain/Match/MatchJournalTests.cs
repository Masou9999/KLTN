using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class MatchJournalTests
    {
        private MatchState state;
        private Dictionary<string, CardDefinition> definitions;
        private CardInstance source;

        [SetUp]
        public void Setup()
        {
            state = new MatchState { HostMulliganDone = true, GuestMulliganDone = true };
            state.BeginFirstRound();
            definitions = new Dictionary<string, CardDefinition>
            {
                { "source", new CardDefinition("source", "Nguồn", 5, 3, 0) },
                { "secret", new CardDefinition("secret", "Lá bí mật", 3, 2, 5) }
            };
            source = new CardInstance(1, "source", SeatId.Host, CardZone.Reserve, 5);
            state.Host.Reserve.Add(source);
        }

        private void Execute(params EffectDefinition[] effects)
        {
            var ability = new AbilityDefinition("journal_test", AbilityTrigger.Summon, effects);
            new AbilityEffectExecutor(definitions).Execute(state, new TriggeredAbility(source.InstanceId,
                source.Owner, ability, GameEvent.FromCard(GameEventType.UnitSummoned, state.RoundNumber, source)), null);
        }

        [Test]
        public void Draw_RecordsOnlySuccessfulDraws_AndRedactsOpponentIdentity()
        {
            state.Host.Deck.Add(new CardInstance(2, "secret", SeatId.Host, CardZone.Deck, 3));
            Execute(new EffectDefinition(EffectKind.Draw, EffectTarget.SourceOwner, amount: 2));
            Assert.AreEqual(1, state.Journal.Entries.Count);
            StringAssert.Contains("Lá bí mật", state.Journal.Entries[0].TextFor(SeatId.Host));
            StringAssert.DoesNotContain("Lá bí mật", state.Journal.Entries[0].TextFor(SeatId.Guest));
            Assert.IsNull(state.TakeAbilityResolution(), "Journal-only effects must not change animation batches.");
        }

        [Test]
        public void KillAndRevive_PreserveIntermediateStateAndCausalOrder()
        {
            Execute(new EffectDefinition(EffectKind.Kill, EffectTarget.Source),
                new EffectDefinition(EffectKind.Revive, EffectTarget.StrongestDeadAlly));
            Assert.AreEqual(2, state.Journal.Entries.Count);
            StringAssert.Contains("tiêu diệt", state.Journal.Entries[0].TextFor(SeatId.Host));
            StringAssert.Contains("hồi sinh", state.Journal.Entries[1].TextFor(SeatId.Host));
            Assert.AreEqual(CardZone.Reserve, source.Zone);
            Assert.Greater(state.Journal.Entries[1].Sequence, state.Journal.Entries[0].Sequence);
        }

        [Test]
        public void Healing_UsesActualCappedAmount_AndOmitsNoResult()
        {
            state.Host.ApplyNexusDamage(1);
            Execute(new EffectDefinition(EffectKind.Heal, EffectTarget.AlliedNexus, amount: 5));
            Execute(new EffectDefinition(EffectKind.Heal, EffectTarget.AlliedNexus, amount: 5));
            Assert.AreEqual(1, state.Journal.Entries.Count);
            StringAssert.Contains("hồi 1 máu", state.Journal.Entries[0].TextFor(SeatId.Host));
            StringAssert.Contains("19 → 20", state.Journal.Entries[0].TextFor(SeatId.Host));
        }

        [Test]
        public void BuffAndCostReduction_AreLoggedWithoutEnteringAnimationPipeline()
        {
            Execute(new EffectDefinition(EffectKind.Buff, EffectTarget.Source, 2, 1, duration: EffectDuration.ThisRound));
            StringAssert.Contains("trong vòng này", state.Journal.Entries[0].TextFor(SeatId.Host));
            Assert.IsNull(state.TakeAbilityResolution());
        }

        [Test]
        public void TwoWeakestKills_RecordBothDeathsWithoutTechnicalLabels()
        {
            state.Guest.Reserve.Add(new CardInstance(2, "secret", SeatId.Guest, CardZone.Reserve, 3));
            state.Guest.Reserve.Add(new CardInstance(3, "secret", SeatId.Guest, CardZone.Reserve, 3));
            Execute(new EffectDefinition(EffectKind.Kill, EffectTarget.WeakestEnemies, count: 2));
            Assert.AreEqual(2, state.Journal.Entries.Count);
            string text = string.Join(" ", state.Journal.Entries.Select(e => e.TextFor(SeatId.Guest)));
            StringAssert.Contains("Lá bí mật của bạn", text);
            StringAssert.DoesNotContain("#", text);
            StringAssert.DoesNotContain("(dự bị)", text);
            StringAssert.DoesNotContain("sát thương", text);
            Assert.AreEqual(2, state.Guest.Graveyard.Count);
        }

        [Test]
        public void DrainingPublicationOrAnimation_DoesNotEraseHistory()
        {
            Execute(new EffectDefinition(EffectKind.Damage, EffectTarget.EnemyNexus, amount: 1));
            Assert.AreEqual(1, state.Journal.TakePending().Count);
            Assert.AreEqual(0, state.Journal.TakePending().Count);
            state.TakeAbilityResolution();
            Assert.AreEqual(1, state.Journal.Entries.Count);
        }

        [Test]
        public void Copy_RedactsNewCardFromOpponent()
        {
            Execute(new EffectDefinition(EffectKind.Copy, EffectTarget.None, cardDefinitionId: "secret"));
            Assert.AreEqual(1, state.Journal.Entries.Count);
            StringAssert.Contains("Lá bí mật", state.Journal.Entries[0].TextFor(SeatId.Host));
            StringAssert.DoesNotContain("Lá bí mật", state.Journal.Entries[0].TextFor(SeatId.Guest));
        }

        [Test]
        public void CombatAndEphemeral_HaveDifferentCategories()
        {
            var card = AbilityCardState.Capture(source, definitions["source"]);
            state.Journal.CombatDamage(1, card, null, 20, 17, SeatId.Guest);
            state.Journal.Ephemeral(1, card, false);
            Assert.AreEqual(MatchLogCategory.Combat, state.Journal.Entries[0].Category);
            Assert.AreEqual(MatchLogCategory.Ability, state.Journal.Entries[1].Category);
        }

        [Test]
        public void ActualCombat_LogsStrikeThenLifestealThenEphemeral()
        {
            definitions["source"] = new CardDefinition("source", "Ma Lai", 5, 3, 0,
                keywords: UnitKeyword.Ephemeral | UnitKeyword.Lifesteal);
            state.Host.ApplyNexusDamage(5);
            var engine = new MatchRulesEngine(definitions);
            Assert.IsTrue(engine.TryDeclareAttack(state, SeatId.Host, new ulong[] { 1, 0, 0 }).Accepted);
            Assert.IsTrue(engine.TryDeclareBlock(state, SeatId.Guest, new ulong[] { 0, 0, 0 }).Accepted);
            Assert.AreEqual(3, state.Journal.Entries.Count);
            Assert.AreEqual(MatchLogCategory.Combat, state.Journal.Entries[0].Category);
            StringAssert.Contains("Hút máu", state.Journal.Entries[1].TextFor(SeatId.Host));
            StringAssert.Contains("15 → 18", state.Journal.Entries[1].TextFor(SeatId.Host));
            StringAssert.Contains("chết do hiệu ứng Phù Du", state.Journal.Entries[2].TextFor(SeatId.Host));
        }

        [Test]
        public void LethalSummon_LogsVictimsDeathAbilityAfterTheKill()
        {
            var breath = new AbilityDefinition("last_breath", AbilityTrigger.Death, new[] {
                new EffectDefinition(EffectKind.Draw, EffectTarget.SourceOwner, amount: 1),
                new EffectDefinition(EffectKind.Heal, EffectTarget.AlliedNexus, amount: 2) });
            definitions["victim"] = new CardDefinition("victim", "Ma Mặt Mâm", 1, 1, 0, abilities: new[] { breath });
            var kill = new AbilityDefinition("summon_kill", AbilityTrigger.Summon, new[] {
                new EffectDefinition(EffectKind.Kill, EffectTarget.WeakestEnemies, count: 2) });
            definitions["source"] = new CardDefinition("source", "Ma Trành", 5, 3, 0, abilities: new[] { kill });
            state.Host.Reserve.Remove(source); source.MoveTo(CardZone.DrawHand); state.Host.DrawHand.Add(source);
            state.Guest.Reserve.Add(new CardInstance(2, "victim", SeatId.Guest, CardZone.Reserve, 1));
            state.Guest.Deck.Add(new CardInstance(3, "secret", SeatId.Guest, CardZone.Deck, 3));
            state.Guest.ApplyNexusDamage(2);
            Assert.IsTrue(new MatchRulesEngine(definitions).TrySummonUnit(state, SeatId.Host, 1).Accepted);
            Assert.AreEqual(3, state.Journal.Entries.Count);
            StringAssert.Contains("tiêu diệt", state.Journal.Entries[0].TextFor(SeatId.Host));
            StringAssert.Contains("Trăn trối", state.Journal.Entries[1].TextFor(SeatId.Host));
            StringAssert.Contains("rút", state.Journal.Entries[1].TextFor(SeatId.Host));
            StringAssert.Contains("hồi 2 máu", state.Journal.Entries[2].TextFor(SeatId.Host));
            StringAssert.DoesNotContain("Lá bí mật", state.Journal.Entries[1].TextFor(SeatId.Host));
        }

        [Test]
        public void PassiveHandDiscount_IsPrivate_AndUsesEffectivePrice()
        {
            definitions["source"] = new CardDefinition("source", "Ma Trơi", 5, 3, 5,
                passiveRules: new UnitPassiveRules(costReductionPerAlliedDeathThisGame: 1));
            state.Host.Reserve.Remove(source); source.MoveTo(CardZone.DrawHand); state.Host.DrawHand.Add(source);
            var dead = new CardInstance(9, "secret", SeatId.Host, CardZone.Graveyard, 0);
            state.RoundHistory.RecordDeath(dead, 2, 3);
            state.Journal.PassiveCosts(state, definitions, SeatId.Host);
            StringAssert.Contains("5 → 4", state.Journal.Entries[0].TextFor(SeatId.Host));
            Assert.IsNull(state.Journal.Entries[0].TextFor(SeatId.Guest));
            Execute(new EffectDefinition(EffectKind.ReduceCost, EffectTarget.Source, amount: 1));
            StringAssert.Contains("4 → 3", state.Journal.Entries[1].TextFor(SeatId.Host));
            StringAssert.DoesNotContain("Ma Trơi", state.Journal.Entries[1].TextFor(SeatId.Guest));
            StringAssert.DoesNotContain("4 → 3", state.Journal.Entries[1].TextFor(SeatId.Guest));
        }
    }
}
