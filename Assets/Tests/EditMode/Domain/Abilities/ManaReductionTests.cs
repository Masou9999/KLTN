using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    /// <summary>Checks actual mana deltas, hidden hand effects and summon payment.</summary>
    public sealed class ManaReductionTests
    {
        #region Fixture

        private MatchState state;
        private Dictionary<string, CardDefinition> definitions;
        private MatchRulesEngine rules;

        [SetUp]
        public void Setup()
        {
            var play = new AbilityDefinition("ma_ga_play", AbilityTrigger.Play,
                new[] { new EffectDefinition(EffectKind.ReduceCost,
                    EffectTarget.PrimarySelection, amount: 1) },
                requiredTargets: new[] { new AbilityTargetRequirement(
                    AbilityTargetSlot.Primary, TargetRelation.Ally,
                    AbilityTargetZone.DrawHand, excludeSource: true) },
                missingTargetPolicy: MissingTargetPolicy.SkipAbility);
            definitions = new Dictionary<string, CardDefinition>
            {
                { "MaGa", new CardDefinition("MaGa", "Ma Gà", 1, 2, 1,
                    abilities: new[] { play }) },
                { "MaTroi", new CardDefinition("MaTroi", "Ma Trơi", 5, 5, 10,
                    passiveRules: new UnitPassiveRules(
                        costReductionPerAlliedDeathThisGame: 1)) },
                { "plain", new CardDefinition("plain", "Đồng minh", 3, 2, 3) },
                { "ephemeral", new CardDefinition("ephemeral", "Tạm thời", 1, 1,
                    1, UnitKeyword.Ephemeral) }
            };
            var deck = Enumerable.Repeat(definitions["plain"],
                DeckRules.RequiredCardCount).ToArray();
            state = new MatchFactory(new SeededRandomSource(731)).Create(deck, deck, 0);
            state.Host.Deck.Clear();
            state.Guest.Deck.Clear();
            state.HostMulliganDone = state.GuestMulliganDone = true;
            state.BeginFirstRound();
            state.Host.InitializeMana(10);
            state.Guest.InitializeMana(10);
            rules = new MatchRulesEngine(definitions, new SeededRandomSource(731));
        }

        private CardInstance Add(string id, CardZone zone = CardZone.DrawHand,
            SeatId seat = SeatId.Host)
        {
            var card = new CardInstance(state.AllocateCardInstanceId(), id, seat,
                zone, definitions[id].BaseHealth);
            PlayerState player = state.Player(seat);
            if (zone == CardZone.DrawHand) player.DrawHand.Add(card);
            else if (zone == CardZone.Reserve) player.Reserve.Add(card);
            else if (zone == CardZone.Board) player.Board.Add(card);
            else if (zone == CardZone.Deck) player.Deck.Add(card);
            else player.Graveyard.Add(card);
            return card;
        }

        private void Kill(CardInstance victim)
        {
            var ability = new AbilityDefinition("sacrifice", AbilityTrigger.Play,
                new[] { new EffectDefinition(EffectKind.Kill, EffectTarget.Source) });
            new AbilityEffectExecutor(definitions).Execute(state,
                new TriggeredAbility(victim.InstanceId, victim.Owner, ability,
                    GameEvent.FromCard(GameEventType.UnitPlayed, state.RoundNumber,
                        victim)), null);
        }

        #endregion

        #region Targeted Reduction

        [TestCase(false)]
        [TestCase(true)]
        public void MultiStepAbility_CompletesBeforeAlliedDeathCostNotification(bool revive)
        {
            var ability = new AbilityDefinition("multi_step", AbilityTrigger.Play,
                new[]
                {
                    new EffectDefinition(EffectKind.Kill, EffectTarget.PrimarySelection),
                    new EffectDefinition(revive ? EffectKind.Revive : EffectKind.Kill,
                        revive ? EffectTarget.PrimarySelection : EffectTarget.SecondarySelection),
                });
            var source = Add("plain", CardZone.Reserve);
            var victim = Add("plain", CardZone.Reserve);
            var enemy = Add("plain", CardZone.Reserve, SeatId.Guest);
            var passive = Add("MaTroi");
            new AbilityEffectExecutor(definitions).Execute(state,
                new TriggeredAbility(source.InstanceId, source.Owner, ability,
                    GameEvent.FromCard(GameEventType.UnitPlayed, state.RoundNumber, source)),
                new AbilityTargetSelection(new[] { victim.InstanceId },
                    new[] { enemy.InstanceId }));
            var events = state.TakeAbilityResolution().Events;
            CollectionAssert.AreEqual(new[] { "multi_step", "multi_step",
                CardCostCalculator.AlliedDeathReductionId },
                events.Select(e => e.AbilityId).ToArray(),
                "The source must finish both steps before the hand passive interrupts staging.");
            Assert.AreEqual(10, events[2].TargetBefore.Cost);
            Assert.Greater(events[0].ExecutionId, 0);
            Assert.AreEqual(events[0].ExecutionId, events[1].ExecutionId);
            Assert.AreEqual(0, events[2].ExecutionId);
            Assert.AreEqual(9, events[2].TargetAfter.Cost);
            Assert.AreEqual(9, CardCostCalculator.GetEffectiveCost(state, passive,
                definitions["MaTroi"]));
            Assert.AreEqual(revive ? CardZone.Reserve : CardZone.Graveyard, victim.Zone);
            Assert.AreEqual(revive ? CardZone.Reserve : CardZone.Graveyard, enemy.Zone);
            Assert.IsTrue(events.Zip(events.Skip(1), (a, b) => a.Sequence < b.Sequence)
                .All(ordered => ordered), "Publication sequence must remain monotonic.");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(10)]
        public void MultiDeathAbility_CapturesEachHandDeltaBeforePublishing(int initialCost)
        {
            var source = Add("plain", CardZone.Reserve);
            var first = Add("plain", CardZone.Reserve);
            var second = Add("plain", CardZone.Reserve);
            var hands = new[] { Add("MaTroi"), Add("MaTroi") };
            foreach (var hand in hands)
                hand.ApplyCostReduction(definitions["MaTroi"], 10 - initialCost,
                    EffectDuration.Permanent);
            var ability = new AbilityDefinition("two_deaths", AbilityTrigger.Play,
                new[]
                {
                    new EffectDefinition(EffectKind.Kill, EffectTarget.PrimarySelection),
                    new EffectDefinition(EffectKind.Kill, EffectTarget.SecondarySelection),
                });
            new AbilityEffectExecutor(definitions).Execute(state,
                new TriggeredAbility(source.InstanceId, source.Owner, ability,
                    GameEvent.FromCard(GameEventType.UnitPlayed, 1, source)),
                new AbilityTargetSelection(new[] { first.InstanceId },
                    new[] { second.InstanceId }));
            var events = state.TakeAbilityResolution().Events;
            Assert.IsTrue(events.Take(2).All(e => e.AbilityId == "two_deaths"));
            foreach (var hand in hands)
            {
                var reductions = events.Skip(2)
                    .Where(e => e.TargetAfter.InstanceId == hand.InstanceId).ToArray();
                Assert.AreEqual(System.Math.Min(2, initialCost), reductions.Length);
                for (int i = 0; i < reductions.Length; i++)
                {
                    Assert.AreEqual(initialCost - i, reductions[i].TargetBefore.Cost);
                    Assert.AreEqual(initialCost - i - 1, reductions[i].TargetAfter.Cost);
                }
            }
        }

        [Test]
        public void RepeatedAbilityExecutions_HaveSeparateGroupingIdentities()
        {
            var source = Add("plain", CardZone.Reserve);
            var victim = Add("plain", CardZone.Reserve);
            var ability = new AbilityDefinition("repeat", AbilityTrigger.Play, new[]
            {
                new EffectDefinition(EffectKind.Kill, EffectTarget.PrimarySelection),
                new EffectDefinition(EffectKind.Revive, EffectTarget.PrimarySelection),
            });
            var executor = new AbilityEffectExecutor(definitions);
            for (int i = 0; i < 2; i++)
                executor.Execute(state,
                    new TriggeredAbility(source.InstanceId, source.Owner, ability,
                        GameEvent.FromCard(GameEventType.UnitPlayed, 1, source)),
                    new AbilityTargetSelection(new[] { victim.InstanceId }, null));
            var events = state.TakeAbilityResolution().Events;
            Assert.AreEqual(4, events.Count);
            Assert.AreEqual(events[0].ExecutionId, events[1].ExecutionId);
            Assert.AreEqual(events[2].ExecutionId, events[3].ExecutionId);
            Assert.AreNotEqual(events[0].ExecutionId, events[2].ExecutionId);
        }

        [TestCase(3, 2)]
        [TestCase(0, 0)]
        public void MaGa_EmitsOnlyActualDelta(int before, int after)
        {
            CardInstance source = Add("MaGa");
            CardInstance target = Add("plain");
            target.ApplyCostReduction(definitions["plain"], 3 - before,
                EffectDuration.Permanent);
            Assert.IsTrue(rules.TrySummonUnit(state, SeatId.Host,
                source.InstanceId).Accepted);
            Assert.IsNotNull(state.PendingSelection);
            Assert.IsTrue(rules.TrySubmitAbilitySelection(state, SeatId.Host,
                state.PendingSelection.RequestId, new AbilityTargetSelection(
                    new[] { target.InstanceId }, null)).Accepted);
            Assert.AreEqual(after, target.GetCost(definitions["plain"]));
            AbilityResolution resolution = state.TakeAbilityResolution();
            if (before == after)
            {
                Assert.IsNull(resolution);
                return;
            }
            Assert.AreEqual(1, resolution.Events.Count);
            Assert.AreEqual(before, resolution.Events[0].TargetBefore.Cost);
            Assert.AreEqual(after, resolution.Events[0].TargetAfter.Cost);
            Assert.AreEqual(target.InstanceId,
                resolution.Events[0].TargetAfter.InstanceId);
            Assert.AreEqual(EffectDuration.Permanent, resolution.Events[0].Duration);
            Assert.AreEqual(1, state.Journal.Entries.Count(e =>
                e.TextFor(SeatId.Host).Contains("giảm")));
            StringAssert.DoesNotContain("Đồng minh", state.Journal.Entries.Last()
                .TextFor(SeatId.Guest));
        }

        [Test]
        public void MaGa_NoOtherHandCard_SkipsEffect()
        {
            CardInstance source = Add("MaGa");
            Add("plain", CardZone.Reserve);
            Assert.IsTrue(rules.TrySummonUnit(state, SeatId.Host,
                source.InstanceId).Accepted);
            Assert.IsNull(state.PendingSelection);
            Assert.IsNull(state.TakeAbilityResolution());
            Assert.AreEqual(CardZone.Reserve, source.Zone);
        }

        [Test]
        public void MaGa_CancelRefundsManaAndPreservesCosts()
        {
            CardInstance source = Add("MaGa");
            CardInstance target = Add("plain");
            Assert.IsTrue(rules.TrySummonUnit(state, SeatId.Host,
                source.InstanceId).Accepted);
            Assert.IsTrue(rules.TryCancelAbilitySelection(state, SeatId.Host,
                state.PendingSelection.RequestId).Accepted);
            Assert.AreEqual(10, state.Host.Mana);
            Assert.AreEqual(CardZone.DrawHand, source.Zone);
            Assert.AreEqual(3, target.GetCost(definitions["plain"]));
            Assert.IsNull(state.TakeAbilityResolution());
        }

        #endregion

        #region Historical Passive

        [TestCase(CardZone.DrawHand, 9)]
        [TestCase(CardZone.Reserve, 10)]
        [TestCase(CardZone.Board, 10)]
        [TestCase(CardZone.Deck, 10)]
        [TestCase(CardZone.Graveyard, 10)]
        public void MaTroi_DiscountsOnlyDrawHand(CardZone zone, int cost)
        {
            CardInstance card = Add("MaTroi", zone);
            Kill(Add("plain", CardZone.Reserve));
            Assert.AreEqual(cost, CardCostCalculator.GetEffectiveCost(state, card,
                definitions["MaTroi"]));
            AbilityResolution resolution = state.TakeAbilityResolution();
            Assert.AreEqual(zone == CardZone.DrawHand ? 1 : 0,
                resolution.Events.Count(e => e.Kind == EffectKind.ReduceCost));
        }

        [Test]
        public void MaTroi_RepeatedAlliedDeathsCaptureEachDeltaAndHideJournal()
        {
            CardInstance card = Add("MaTroi");
            Kill(Add("plain", CardZone.Reserve));
            Kill(Add("plain", CardZone.Reserve));
            var reductions = state.TakeAbilityResolution().Events
                .Where(e => e.Kind == EffectKind.ReduceCost).ToArray();
            Assert.AreEqual(2, reductions.Length);
            Assert.AreEqual(10, reductions[0].TargetBefore.Cost);
            Assert.AreEqual(9, reductions[0].TargetAfter.Cost);
            Assert.AreEqual(9, reductions[1].TargetBefore.Cost);
            Assert.AreEqual(8, reductions[1].TargetAfter.Cost);
            Assert.Less(reductions[0].Sequence, reductions[1].Sequence);
            Assert.AreEqual(card.InstanceId, reductions[1].Source.InstanceId);
            Assert.AreEqual(2, state.Journal.Entries.Count(e =>
                e.TextFor(SeatId.Host)?.Contains("Ma Trơi") == true));
            Assert.IsFalse(state.Journal.Entries.Any(e =>
                e.TextFor(SeatId.Guest)?.Contains("Ma Trơi") == true));
        }

        [Test]
        public void MaTroi_EnemyDeathDoesNotDiscountOrAnimate()
        {
            CardInstance card = Add("MaTroi");
            Kill(Add("plain", CardZone.Reserve, SeatId.Guest));
            Assert.AreEqual(10, CardCostCalculator.GetEffectiveCost(state, card,
                definitions["MaTroi"]));
            Assert.IsFalse(state.TakeAbilityResolution().Events
                .Any(e => e.Kind == EffectKind.ReduceCost));
        }

        [Test]
        public void MaTroi_ZeroCostSuppressesLaterReduction()
        {
            CardInstance card = Add("MaTroi");
            card.ApplyCostReduction(definitions["MaTroi"], 9,
                EffectDuration.Permanent);
            Kill(Add("plain", CardZone.Reserve));
            Kill(Add("plain", CardZone.Reserve));
            var reductions = state.TakeAbilityResolution().Events
                .Where(e => e.Kind == EffectKind.ReduceCost).ToArray();
            Assert.AreEqual(1, reductions.Length);
            Assert.AreEqual(1, reductions[0].TargetBefore.Cost);
            Assert.AreEqual(0, reductions[0].TargetAfter.Cost);
        }

        [Test]
        public void MaTroi_LaterDrawUsesHistoryWithoutRetroactiveAnimation()
        {
            CardInstance card = Add("MaTroi", CardZone.Deck);
            Kill(Add("plain", CardZone.Reserve));
            state.TakeAbilityResolution();
            state.Host.DrawOne();
            Assert.AreEqual(CardZone.DrawHand, card.Zone);
            Assert.AreEqual(9, CardCostCalculator.GetEffectiveCost(state, card,
                definitions["MaTroi"]));
            Assert.IsNull(state.TakeAbilityResolution());
        }

        [Test]
        public void MaTroi_SummonPaysHandCostThenLosesHandOnlyDiscount()
        {
            CardInstance card = Add("MaTroi");
            Kill(Add("plain", CardZone.Reserve));
            state.TakeAbilityResolution();
            Assert.IsTrue(rules.TrySummonUnit(state, SeatId.Host,
                card.InstanceId).Accepted);
            Assert.AreEqual(1, state.Host.Mana);
            Assert.AreEqual(CardZone.Reserve, card.Zone);
            Assert.AreEqual(10, CardCostCalculator.GetEffectiveCost(state, card,
                definitions["MaTroi"]));
            Assert.IsNull(state.TakeAbilityResolution());
        }

        [Test]
        public void Expiry_RecordsDeathBeforeItsHandReduction()
        {
            Add("MaTroi");
            CardInstance ephemeral = Add("ephemeral", CardZone.Reserve);
            Assert.IsTrue(state.Host.TryMoveReserveCardToBoard(ephemeral, 0));
            Assert.IsTrue(rules.TryPass(state, SeatId.Host).Accepted);
            Assert.IsTrue(rules.TryPass(state, SeatId.Guest).Accepted);
            AbilityResolution resolution = state.TakeAbilityResolution();
            Assert.AreEqual(2, resolution.Events.Count);
            Assert.IsTrue(resolution.Events[0].IsDeathOnly);
            Assert.AreEqual(ephemeral.InstanceId,
                resolution.Events[0].TargetBefore.InstanceId);
            Assert.AreEqual(EffectKind.ReduceCost, resolution.Events[1].Kind);
            Assert.Less(resolution.Events[0].Sequence, resolution.Events[1].Sequence);
            Assert.AreEqual(2, state.RoundNumber);
        }

        #endregion
    }
}
