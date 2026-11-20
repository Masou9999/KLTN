using System.Collections.Generic;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class ExtendedAbilityEffectTests
    {
        [Test]
        public void AllocateCardInstanceId_SkipsExistingIds()
        {
            MatchState state = CreateState();

            state.Host.DrawHand.Add(Card(1, "unit", SeatId.Host, CardZone.DrawHand, 3));

            state.Guest.Graveyard.Add(
                Card(2, "unit", SeatId.Guest, CardZone.Graveyard, 0)
            );

            Assert.AreEqual(3, state.AllocateCardInstanceId());
        }

        [Test]
        public void ReduceCost_IsUsedWhenSummoning()
        {
            MatchState state = CreateState();
            CardDefinition definition = Unit("source", health: 3, damage: 2, cost: 3);

            var definitions = Definitions(definition);

            CardInstance source = Card(
                1,
                definition.Id,
                SeatId.Host,
                CardZone.DrawHand,
                definition.BaseHealth
            );

            state.Host.DrawHand.Add(source);

            Execute(
                state,
                definitions,
                source,
                new EffectDefinition(
                    EffectKind.ReduceCost,
                    EffectTarget.Source,
                    amount: 2
                )
            );

            Assert.AreEqual(1, source.GetCost(definition));

            var engine = new MatchRulesEngine(definitions);

            CommandResult result = engine.TrySummonUnit(
                state,
                SeatId.Host,
                source.InstanceId
            );

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(9, state.Host.Mana);
        }

        [Test]
        public void ThisRoundCostReduction_IsCleared()
        {
            CardDefinition definition = Unit("unit", cost: 3);

            CardInstance card = Card(
                1,
                definition.Id,
                SeatId.Host,
                CardZone.DrawHand,
                definition.BaseHealth
            );

            card.ApplyCostReduction(definition, 2, EffectDuration.ThisRound);

            Assert.AreEqual(1, card.GetCost(definition));

            card.ClearRoundModifiers(definition);

            Assert.AreEqual(3, card.GetCost(definition));
        }

        [Test]
        public void ReturnToDrawHand_ResetsCardToBase()
        {
            MatchState state = CreateState();
            CardDefinition definition = Unit("source", health: 4, damage: 3, cost: 2);

            var definitions = Definitions(definition);

            CardInstance source = AddDeadCard(state, definition, 1);

            source.ApplyCostReduction(definition, 1, EffectDuration.Permanent);

            Execute(
                state,
                definitions,
                source,
                new EffectDefinition(EffectKind.ReturnToDrawHand, EffectTarget.Source)
            );

            Assert.AreEqual(CardZone.DrawHand, source.Zone);

            Assert.AreEqual(definition.BaseHealth, source.CurrentHealth);

            Assert.AreEqual(definition.Cost, source.GetCost(definition));

            CollectionAssert.Contains(state.Host.DrawHand, source);

            CollectionAssert.DoesNotContain(state.Host.Graveyard, source);
        }

        [Test]
        public void ReturnToReserve_MovesBoardCardWithoutReset()
        {
            MatchState state = CreateState();
            CardDefinition definition = Unit("source", health: 4);

            var definitions = Definitions(definition);

            CardInstance source = Card(1, definition.Id, SeatId.Host, CardZone.Board, 2);

            source.MoveTo(CardZone.Board, 0);
            state.Host.Board.Add(source);

            Execute(
                state,
                definitions,
                source,
                new EffectDefinition(EffectKind.ReturnToReserve, EffectTarget.Source)
            );

            Assert.AreEqual(CardZone.Reserve, source.Zone);

            Assert.AreEqual(2, source.CurrentHealth);

            CollectionAssert.Contains(state.Host.Reserve, source);
        }

        [Test]
        public void ReviveStrongestDeadAlly_UsesDamageThenHealth()
        {
            MatchState state = CreateState();

            CardDefinition sourceDefinition = Unit("source");

            CardDefinition weakDefinition = Unit("weak", health: 5, damage: 2);

            CardDefinition strongDefinition = Unit("strong", health: 3, damage: 5);

            var definitions = Definitions(
                sourceDefinition,
                weakDefinition,
                strongDefinition
            );

            CardInstance source = AddReserveCard(state.Host, 1, sourceDefinition);

            CardInstance weak = AddDeadCard(state, weakDefinition, 2);

            CardInstance strong = AddDeadCard(state, strongDefinition, 3);

            GameEventBatch emitted = Execute(
                state,
                definitions,
                source,
                new EffectDefinition(EffectKind.Revive, EffectTarget.StrongestDeadAlly)
            );

            CollectionAssert.Contains(state.Host.Reserve, strong);

            CollectionAssert.Contains(state.Host.Graveyard, weak);

            Assert.AreEqual(strongDefinition.BaseHealth, strong.CurrentHealth);

            Assert.AreEqual(1, emitted.Events.Count);

            Assert.AreEqual(GameEventType.UnitSummoned, emitted.Events[0].Type);
        }

        [Test]
        public void ReviveStrongestDeadAlly_ConsidersPreviousRoundDeaths()
        {
            MatchState state = CreateState();

            CardDefinition sourceDefinition = Unit("linh_mieu");

            CardDefinition oldStrongDefinition = Unit(
                "ma_tranh",
                health: 6,
                damage: 8
            );

            CardDefinition currentWeakDefinition = Unit(
                "current_weak",
                health: 2,
                damage: 2
            );

            var definitions = Definitions(
                sourceDefinition,
                oldStrongDefinition,
                currentWeakDefinition
            );

            CardInstance source = AddReserveCard(state.Host, 1, sourceDefinition);

            CardInstance oldStrong = AddDeadCard(state, oldStrongDefinition, 2);

            state.RoundHistory.AdvanceToRound(2);
            state.RoundNumber = 2;

            CardInstance currentWeak = AddDeadCard(state, currentWeakDefinition, 3);

            Execute(
                state,
                definitions,
                source,
                new EffectDefinition(EffectKind.Revive, EffectTarget.StrongestDeadAlly)
            );

            CollectionAssert.Contains(state.Host.Reserve, oldStrong);
            CollectionAssert.Contains(state.Host.Graveyard, currentWeak);
            Assert.AreEqual(CardZone.Reserve, oldStrong.Zone);
        }

        [Test]
        public void CopyRandomDeadAlly_CreatesNewHandInstance()
        {
            MatchState state = CreateState();

            CardDefinition sourceDefinition = Unit("source");

            CardDefinition deadDefinition = Unit("dead");

            var definitions = Definitions(sourceDefinition, deadDefinition);

            CardInstance source = AddReserveCard(state.Host, 1, sourceDefinition);

            CardInstance dead = AddDeadCard(state, deadDefinition, 2);

            Execute(
                state,
                definitions,
                source,
                new EffectDefinition(EffectKind.Copy, EffectTarget.RandomDeadAlly),
                seed: 7
            );

            Assert.AreEqual(1, state.Host.DrawHand.Count);

            CardInstance copy = state.Host.DrawHand[0];

            Assert.AreEqual(dead.DefinitionId, copy.DefinitionId);

            Assert.AreNotEqual(dead.InstanceId, copy.InstanceId);

            CollectionAssert.Contains(state.Host.Graveyard, dead);

            AbilityResolution resolution = state.TakeAbilityResolution();
            Assert.AreEqual(1, resolution.Events.Count);
            Assert.AreEqual(EffectKind.Copy, resolution.Events[0].Kind);
            Assert.AreEqual(CardZone.DrawHand, resolution.Events[0].TargetAfter.Zone);
            Assert.AreEqual(copy.InstanceId, resolution.Events[0].TargetAfter.InstanceId);
            Assert.IsNull(resolution.Events[0].TargetBefore);
            Assert.Greater(resolution.Events[0].ExecutionId, 0);
        }

        [Test]
        public void CopyRandomDeadAlly_WithoutDeath_DoesNotEmitCreation()
        {
            MatchState state = CreateState();
            CardDefinition definition = Unit("source");
            CardInstance source = AddReserveCard(state.Host, 1, definition);
            Execute(state, Definitions(definition), source,
                new EffectDefinition(EffectKind.Copy, EffectTarget.RandomDeadAlly));
            Assert.IsEmpty(state.Host.DrawHand);
            Assert.IsNull(state.TakeAbilityResolution());
        }

        [Test]
        public void Summon_StopsAtActiveRosterCapacity()
        {
            MatchState state = CreateState();

            CardDefinition sourceDefinition = Unit("source");

            CardDefinition tokenDefinition = Unit("token");

            var definitions = Definitions(sourceDefinition, tokenDefinition);

            CardInstance source = AddReserveCard(state.Host, 1, sourceDefinition);

            GameEventBatch emitted = Execute(
                state,
                definitions,
                source,
                new EffectDefinition(
                    EffectKind.Summon,
                    EffectTarget.SourceOwner,
                    count: 5,
                    cardDefinitionId: tokenDefinition.Id
                )
            );

            Assert.AreEqual(
                MatchState.MaximumActiveRosterSize,
                state.Host.ActiveRosterCount
            );

            Assert.AreEqual(3, emitted.Events.Count);

            foreach (GameEvent gameEvent in emitted.Events)
            {
                Assert.AreEqual(GameEventType.UnitSummoned, gameEvent.Type);
            }
        }

        [Test]
        public void KillAllUnits_MovesBothSidesToGraveyard()
        {
            MatchState state = CreateState();

            CardDefinition definition = Unit("unit");

            var definitions = Definitions(definition);

            CardInstance host = AddReserveCard(state.Host, 1, definition);

            AddReserveCard(state.Guest, 2, definition);

            GameEventBatch emitted = Execute(
                state,
                definitions,
                host,
                new EffectDefinition(EffectKind.KillAllUnits, EffectTarget.AllUnits)
            );

            Assert.AreEqual(0, state.Host.Reserve.Count);
            Assert.AreEqual(0, state.Guest.Reserve.Count);

            Assert.AreEqual(1, state.Host.Graveyard.Count);
            Assert.AreEqual(1, state.Guest.Graveyard.Count);

            Assert.AreEqual(2, emitted.Events.Count);
        }

        [Test]
        public void HalfNexus_RoundsRemainingHealthUp()
        {
            MatchState state = CreateState();

            CardDefinition definition = Unit("source");

            var definitions = Definitions(definition);

            CardInstance source = AddReserveCard(state.Host, 1, definition);

            state.Guest.ApplyNexusDamage(1);

            Assert.AreEqual(19, state.Guest.NexusHealth);

            Execute(
                state,
                definitions,
                source,
                new EffectDefinition(EffectKind.HalfNexus, EffectTarget.EnemyNexus)
            );

            Assert.AreEqual(10, state.Guest.NexusHealth);

            AbilityResolution visual = state.TakeAbilityResolution();
            Assert.AreEqual(1, visual.Events.Count);
            Assert.AreEqual(EffectKind.HalfNexus, visual.Events[0].Kind);
            Assert.AreEqual(19, visual.Events[0].NexusHealthBefore);
            Assert.AreEqual(10, visual.Events[0].NexusHealthAfter);
        }

        [Test]
        public void DamageCard_RecordsBeforeAndAfterForPresentation()
        {
            MatchState state = CreateState();
            CardDefinition sourceDefinition = Unit("source");
            CardDefinition targetDefinition = Unit("target", health: 4);
            var definitions = Definitions(sourceDefinition, targetDefinition);

            CardInstance source = AddReserveCard(state.Host, 1, sourceDefinition);
            CardInstance target = AddReserveCard(state.Guest, 2, targetDefinition);

            Execute(
                state,
                definitions,
                source,
                new EffectDefinition(
                    EffectKind.Damage,
                    EffectTarget.WeakestEnemies,
                    amount: 2,
                    count: 1
                )
            );

            AbilityResolution visual = state.TakeAbilityResolution();
            Assert.AreEqual(1, visual.Events.Count);
            Assert.AreEqual(4, visual.Events[0].TargetBefore.Health);
            Assert.AreEqual(2, visual.Events[0].TargetAfter.Health);
            Assert.AreEqual(target.InstanceId, visual.Events[0].TargetAfter.InstanceId);
        }

        private static GameEventBatch Execute(
            MatchState state,
            IReadOnlyDictionary<string, CardDefinition> definitions,
            CardInstance source,
            EffectDefinition effect,
            int seed = 1
        )
        {
            var ability = new AbilityDefinition(
                $"test_{effect.Kind}",
                AbilityTrigger.Summon,
                new[] { effect }
            );

            var triggered = new TriggeredAbility(
                source.InstanceId,
                source.Owner,
                ability,
                GameEvent.FromCard(GameEventType.UnitSummoned, state.RoundNumber, source)
            );

            var executor = new AbilityEffectExecutor(
                definitions,
                new SeededRandomSource(seed)
            );

            return executor.Execute(state, triggered, null);
        }

        private static MatchState CreateState()
        {
            var state = new MatchState(SeatId.Host);

            state.Host.InitializeMana(10);
            state.Guest.InitializeMana(10);

            state.HostMulliganDone = true;
            state.GuestMulliganDone = true;

            state.BeginFirstRound();

            return state;
        }

        private static CardDefinition Unit(
            string id,
            int health = 3,
            int damage = 2,
            int cost = 1
        )
        {
            return new CardDefinition(id, id, health, damage, cost);
        }

        private static Dictionary<string, CardDefinition> Definitions(
            params CardDefinition[] definitions
        )
        {
            var result = new Dictionary<string, CardDefinition>();

            foreach (CardDefinition definition in definitions)
            {
                result.Add(definition.Id, definition);
            }

            return result;
        }

        private static CardInstance Card(
            ulong id,
            string definitionId,
            SeatId owner,
            CardZone zone,
            int health
        )
        {
            return new CardInstance(id, definitionId, owner, zone, health);
        }

        private static CardInstance AddReserveCard(
            PlayerState player,
            ulong id,
            CardDefinition definition
        )
        {
            CardInstance card = Card(
                id,
                definition.Id,
                player.Seat,
                CardZone.Reserve,
                definition.BaseHealth
            );

            player.Reserve.Add(card);
            return card;
        }

        private static CardInstance AddDeadCard(
            MatchState state,
            CardDefinition definition,
            ulong id
        )
        {
            CardInstance card = AddReserveCard(state.Host, id, definition);

            card.Kill();

            Assert.IsTrue(state.Host.TryMoveDeadActiveCardToGraveyard(card));

            state.RoundHistory.RecordDeath(
                card,
                definition.BaseDamage,
                definition.BaseHealth
            );

            return card;
        }
    }
}
