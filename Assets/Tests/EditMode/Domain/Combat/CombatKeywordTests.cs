using System.Collections.Generic;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class CombatKeywordTests
    {
        [Test]
        public void CannotBlockUnit_IsRejectedAtomically()
        {
            CardDefinition attackerDefinition = Unit("attacker", 4, 2, UnitKeyword.None);

            CardDefinition blockerDefinition = Unit(
                "blocker",
                4,
                2,
                UnitKeyword.CannotBlock
            );

            MatchState state = CreateReadyState();

            CardInstance attacker = AddReserve(state.Host, 1, attackerDefinition);

            CardInstance blocker = AddReserve(state.Guest, 10, blockerDefinition);

            MatchRulesEngine engine = Engine(attackerDefinition, blockerDefinition);

            Assert.IsTrue(
                engine
                    .TryDeclareAttack(
                        state,
                        SeatId.Host,
                        new[] { attacker.InstanceId, 0UL, 0UL }
                    )
                    .Accepted
            );

            CommandResult result = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new[] { blocker.InstanceId, 0UL, 0UL }
            );

            Assert.IsFalse(result.Accepted);

            Assert.AreEqual(
                CommandRejectionReason.CardCannotBlock,
                result.RejectionReason
            );

            Assert.AreEqual(CardZone.Reserve, blocker.Zone);

            Assert.AreEqual(0, state.Guest.Board.Count);
        }

        [Test]
        public void Fearsome_RequiresThreeCurrentPower()
        {
            CardDefinition fearsome = Unit("fearsome", 5, 2, UnitKeyword.Fearsome);

            CardDefinition weak = Unit("weak", 4, 2, UnitKeyword.None);

            CardDefinition strong = Unit("strong", 4, 3, UnitKeyword.None);

            MatchState state = CreateReadyState();

            CardInstance attacker = AddReserve(state.Host, 1, fearsome);

            CardInstance weakBlocker = AddReserve(state.Guest, 10, weak);

            CardInstance strongBlocker = AddReserve(state.Guest, 11, strong);

            MatchRulesEngine engine = Engine(fearsome, weak, strong);

            engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { attacker.InstanceId, 0UL, 0UL }
            );

            CommandResult weakResult = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new[] { weakBlocker.InstanceId, 0UL, 0UL }
            );

            Assert.IsFalse(weakResult.Accepted);

            Assert.AreEqual(
                CommandRejectionReason.FearsomeBlockerTooWeak,
                weakResult.RejectionReason
            );

            CommandResult strongResult = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new[] { strongBlocker.InstanceId, 0UL, 0UL }
            );

            Assert.IsTrue(strongResult.Accepted);
        }

        [Test]
        public void LifestealAgainstUnit_HealsActualDamage()
        {
            CardDefinition lifesteal = Unit("lifesteal", 5, 5, UnitKeyword.Lifesteal);

            CardDefinition blockerDefinition = Unit("blocker", 2, 1, UnitKeyword.None);

            MatchState state = CreateReadyState();
            state.Host.ApplyNexusDamage(5);

            CardInstance attacker = AddReserve(state.Host, 1, lifesteal);

            CardInstance blocker = AddReserve(state.Guest, 10, blockerDefinition);

            MatchRulesEngine engine = Engine(lifesteal, blockerDefinition);

            engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { attacker.InstanceId, 0UL, 0UL }
            );

            CommandResult result = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new[] { blocker.InstanceId, 0UL, 0UL }
            );

            Assert.IsTrue(result.Accepted);

            // Five Power hits a two-health unit.
            Assert.AreEqual(17, state.Host.NexusHealth);

            Assert.AreEqual(2, result.Resolution.Steps[0].GuestCard.DamageTaken);
        }

        [Test]
        public void LifestealAgainstNexus_HealsDamageDealt()
        {
            CardDefinition lifesteal = Unit("lifesteal", 4, 3, UnitKeyword.Lifesteal);

            MatchState state = CreateReadyState();
            state.Host.ApplyNexusDamage(5);

            CardInstance attacker = AddReserve(state.Host, 1, lifesteal);

            MatchRulesEngine engine = Engine(lifesteal);

            engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { attacker.InstanceId, 0UL, 0UL }
            );

            CommandResult result = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new ulong[MatchState.BoardSlotCount]
            );

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(17, state.Guest.NexusHealth);
            Assert.AreEqual(18, state.Host.NexusHealth);
        }

        [Test]
        public void Ephemeral_DiesAfterDirectStrike()
        {
            CardDefinition ephemeral = Unit("ephemeral", 4, 3, UnitKeyword.Ephemeral);

            MatchState state = CreateReadyState();

            CardInstance attacker = AddReserve(state.Host, 1, ephemeral);

            MatchRulesEngine engine = Engine(ephemeral);

            engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { attacker.InstanceId, 0UL, 0UL }
            );

            CommandResult result = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new ulong[MatchState.BoardSlotCount]
            );

            Assert.IsTrue(result.Accepted);

            Assert.AreEqual(CardZone.Graveyard, attacker.Zone);

            CardCombatResolution resolution = result.Resolution.Steps[0].HostCard;

            Assert.IsTrue(resolution.Died);
            Assert.IsTrue(resolution.DiedFromEphemeral);
            Assert.AreEqual(0, resolution.DamageTaken);
        }

        [Test]
        public void RoundEnd_ExpiresEphemeralAndRunsDeathTriggers()
        {
            AbilityDefinition lastBreath = Ability(
                "ephemeral_last_breath",
                AbilityTrigger.Death,
                new EffectDefinition(EffectKind.Draw, EffectTarget.SourceOwner, amount: 1)
            );

            AbilityDefinition allyDeath = Ability(
                "watch_ally_death",
                AbilityTrigger.AllyDeath,
                new EffectDefinition(
                    EffectKind.Damage,
                    EffectTarget.EnemyNexus,
                    amount: 1
                )
            );

            CardDefinition ephemeralDefinition = Unit(
                "ephemeral",
                3,
                2,
                UnitKeyword.Ephemeral,
                lastBreath
            );

            CardDefinition watcherDefinition = Unit(
                "watcher",
                3,
                2,
                UnitKeyword.None,
                allyDeath
            );

            CardDefinition plainDefinition = Unit("plain", 3, 2, UnitKeyword.None);

            MatchState state = CreateReadyState();

            CardInstance ephemeral = AddReserve(state.Host, 1, ephemeralDefinition);

            Assert.IsTrue(state.Host.TryMoveReserveCardToBoard(ephemeral, 0));

            AddReserve(state.Host, 2, watcherDefinition);

            state.Host.Deck.Add(
                new CardInstance(
                    100,
                    plainDefinition.Id,
                    SeatId.Host,
                    CardZone.Deck,
                    plainDefinition.BaseHealth
                )
            );

            MatchRulesEngine engine = Engine(
                ephemeralDefinition,
                watcherDefinition,
                plainDefinition
            );

            engine.TryPass(state, SeatId.Host);

            CommandResult result = engine.TryPass(state, SeatId.Guest);

            Assert.IsTrue(result.Accepted);
            Assert.IsNotNull(result.RoundTransition);

            Assert.AreEqual(CardZone.Graveyard, ephemeral.Zone);

            Assert.AreEqual(1, state.Host.DrawHand.Count);
            Assert.AreEqual(19, state.Guest.NexusHealth);
            Assert.AreEqual(2, state.RoundNumber);
        }

        [Test]
        public void AttackAndSupport_TriggerInSlotOrder()
        {
            AbilityDefinition attackAbility = Ability(
                "attack_buff",
                AbilityTrigger.Attack,
                new EffectDefinition(EffectKind.Buff, EffectTarget.Source, amount: 2)
            );

            AbilityDefinition supportAbility = Ability(
                "support_buff",
                AbilityTrigger.Support,
                new EffectDefinition(
                    EffectKind.Buff,
                    EffectTarget.TriggerSubject,
                    amount: 1,
                    secondaryAmount: 1
                )
            );

            CardDefinition supporterDefinition = Unit(
                "supporter",
                3,
                2,
                UnitKeyword.None,
                attackAbility,
                supportAbility
            );

            CardDefinition allyDefinition = Unit("ally", 3, 2, UnitKeyword.None);

            MatchState state = CreateReadyState();

            CardInstance supporter = AddReserve(state.Host, 1, supporterDefinition);

            CardInstance ally = AddReserve(state.Host, 2, allyDefinition);

            MatchRulesEngine engine = Engine(supporterDefinition, allyDefinition);

            CommandResult result = engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { supporter.InstanceId, ally.InstanceId, 0UL }
            );

            Assert.IsTrue(result.Accepted);

            Assert.AreEqual(4, supporter.GetDamage(supporterDefinition));

            Assert.AreEqual(3, ally.GetDamage(allyDefinition));

            Assert.AreEqual(4, ally.CurrentHealth);
        }

        [Test]
        public void RoundStart_TriggersActiveRosterAbility()
        {
            AbilityDefinition roundStart = Ability(
                "round_start_damage",
                AbilityTrigger.RoundStart,
                new EffectDefinition(
                    EffectKind.Damage,
                    EffectTarget.EnemyNexus,
                    amount: 1
                )
            );

            CardDefinition watcher = Unit("watcher", 3, 2, UnitKeyword.None, roundStart);

            MatchState state = CreateReadyState();

            AddReserve(state.Host, 1, watcher);

            MatchRulesEngine engine = Engine(watcher);

            engine.TryPass(state, SeatId.Host);
            CommandResult result = engine.TryPass(state, SeatId.Guest);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(2, state.RoundNumber);
            Assert.AreEqual(19, state.Guest.NexusHealth);
            Assert.AreEqual(MatchPhase.Priority, state.Phase);
        }

        [Test]
        public void Fearsome_BuffedTwoPowerBlockerCanBlock()
        {
            CardDefinition fearsomeDefinition = Unit(
                "fearsome",
                5,
                2,
                UnitKeyword.Fearsome
            );

            CardDefinition blockerDefinition = Unit("blocker", 4, 2, UnitKeyword.None);

            MatchState state = CreateReadyState();

            CardInstance attacker = AddReserve(state.Host, 1, fearsomeDefinition);

            CardInstance blocker = AddReserve(state.Guest, 10, blockerDefinition);

            blocker.ApplyBuff(
                blockerDefinition,
                damageAmount: 1,
                healthAmount: 0,
                EffectDuration.Permanent
            );

            Assert.AreEqual(3, blocker.GetDamage(blockerDefinition));

            MatchRulesEngine engine = Engine(fearsomeDefinition, blockerDefinition);

            CommandResult attackResult = engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { attacker.InstanceId, 0UL, 0UL }
            );

            Assert.IsTrue(attackResult.Accepted);

            CommandResult blockResult = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new[] { blocker.InstanceId, 0UL, 0UL }
            );

            Assert.IsTrue(blockResult.Accepted);

            Assert.AreEqual(CardZone.Reserve, blocker.Zone);

            Assert.IsNotNull(blockResult.Resolution);
        }

        [Test]
        public void CannotBlock_RejectionDoesNotMutateCombatState()
        {
            CardDefinition attackerDefinition = Unit("attacker", 5, 3, UnitKeyword.None);

            CardDefinition blockerDefinition = Unit(
                "cannot_block",
                4,
                2,
                UnitKeyword.CannotBlock
            );

            MatchState state = CreateReadyState();

            CardInstance attacker = AddReserve(state.Host, 1, attackerDefinition);

            CardInstance blocker = AddReserve(state.Guest, 10, blockerDefinition);

            MatchRulesEngine engine = Engine(attackerDefinition, blockerDefinition);

            CommandResult attackResult = engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { attacker.InstanceId, 0UL, 0UL }
            );

            Assert.IsTrue(attackResult.Accepted);

            int attackerHealthBefore = attacker.CurrentHealth;

            int blockerHealthBefore = blocker.CurrentHealth;

            int hostNexusHealthBefore = state.Host.NexusHealth;

            int guestNexusHealthBefore = state.Guest.NexusHealth;

            CommandResult blockResult = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new[] { blocker.InstanceId, 0UL, 0UL }
            );

            Assert.IsFalse(blockResult.Accepted);

            Assert.AreEqual(
                CommandRejectionReason.CardCannotBlock,
                blockResult.RejectionReason
            );

            Assert.IsNull(blockResult.Resolution);

            Assert.AreEqual(attackerHealthBefore, attacker.CurrentHealth);

            Assert.AreEqual(blockerHealthBefore, blocker.CurrentHealth);

            Assert.AreEqual(hostNexusHealthBefore, state.Host.NexusHealth);

            Assert.AreEqual(guestNexusHealthBefore, state.Guest.NexusHealth);

            Assert.AreEqual(CardZone.Board, attacker.Zone);

            Assert.AreEqual(CardZone.Reserve, blocker.Zone);

            Assert.AreEqual(1, state.Host.Board.Count);

            Assert.AreEqual(0, state.Guest.Board.Count);

            Assert.AreEqual(MatchPhase.BlockDeclaration, state.Phase);

            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);
        }

        [Test]
        public void Lifesteal_DoesNotExceedMaximumNexusHealth()
        {
            CardDefinition lifestealDefinition = Unit(
                "lifesteal",
                4,
                3,
                UnitKeyword.Lifesteal
            );

            MatchState state = CreateReadyState();

            state.Host.ApplyNexusDamage(1);

            Assert.AreEqual(19, state.Host.NexusHealth);

            CardInstance attacker = AddReserve(state.Host, 1, lifestealDefinition);

            MatchRulesEngine engine = Engine(lifestealDefinition);

            CommandResult attackResult = engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { attacker.InstanceId, 0UL, 0UL }
            );

            Assert.IsTrue(attackResult.Accepted);

            CommandResult combatResult = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new ulong[MatchState.BoardSlotCount]
            );

            Assert.IsTrue(combatResult.Accepted);

            Assert.AreEqual(17, state.Guest.NexusHealth);

            Assert.AreEqual(20, state.Host.NexusHealth);
        }

        [Test]
        public void Ephemeral_BlockedStrikeRunsDeathAndAllyDeath()
        {
            AbilityDefinition lastBreath = Ability(
                "ephemeral_last_breath",
                AbilityTrigger.Death,
                new EffectDefinition(EffectKind.Draw, EffectTarget.SourceOwner, amount: 1)
            );

            AbilityDefinition allyDeath = Ability(
                "watch_ephemeral_death",
                AbilityTrigger.AllyDeath,
                new EffectDefinition(
                    EffectKind.Damage,
                    EffectTarget.EnemyNexus,
                    amount: 1
                )
            );

            CardDefinition ephemeralDefinition = Unit(
                "ephemeral",
                4,
                2,
                UnitKeyword.Ephemeral,
                lastBreath
            );

            CardDefinition watcherDefinition = Unit(
                "watcher",
                3,
                1,
                UnitKeyword.None,
                allyDeath
            );

            CardDefinition blockerDefinition = Unit("blocker", 5, 1, UnitKeyword.None);

            CardDefinition drawnCardDefinition = Unit(
                "drawn_card",
                3,
                2,
                UnitKeyword.None
            );

            MatchState state = CreateReadyState();

            CardInstance ephemeral = AddReserve(state.Host, 1, ephemeralDefinition);

            AddReserve(state.Host, 2, watcherDefinition);

            CardInstance blocker = AddReserve(state.Guest, 10, blockerDefinition);

            state.Host.Deck.Add(
                new CardInstance(
                    100,
                    drawnCardDefinition.Id,
                    SeatId.Host,
                    CardZone.Deck,
                    drawnCardDefinition.BaseHealth
                )
            );

            MatchRulesEngine engine = Engine(
                ephemeralDefinition,
                watcherDefinition,
                blockerDefinition,
                drawnCardDefinition
            );

            CommandResult attackResult = engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { ephemeral.InstanceId, 0UL, 0UL }
            );

            Assert.IsTrue(attackResult.Accepted);

            CommandResult combatResult = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new[] { blocker.InstanceId, 0UL, 0UL }
            );

            Assert.IsTrue(combatResult.Accepted);

            Assert.AreEqual(CardZone.Graveyard, ephemeral.Zone);

            Assert.AreEqual(3, blocker.CurrentHealth);

            Assert.AreEqual(CardZone.Reserve, blocker.Zone);

            Assert.AreEqual(1, state.Host.DrawHand.Count);

            Assert.AreEqual(drawnCardDefinition.Id, state.Host.DrawHand[0].DefinitionId);

            Assert.AreEqual(19, state.Guest.NexusHealth);

            Assert.AreEqual(1, state.RoundHistory.CurrentRoundDeaths.Count);

            CardCombatResolution resolution = combatResult.Resolution.Steps[0].HostCard;

            Assert.IsTrue(resolution.Died);

            Assert.IsTrue(resolution.DiedFromEphemeral);

            Assert.AreEqual(1, resolution.DamageTaken);
        }

        [Test]
        public void Support_WithEmptyRightSlotDoesNotTrigger()
        {
            AbilityDefinition supportAbility = Ability(
                "support_buff",
                AbilityTrigger.Support,
                new EffectDefinition(
                    EffectKind.Buff,
                    EffectTarget.TriggerSubject,
                    amount: 1,
                    secondaryAmount: 1
                )
            );

            CardDefinition supporterDefinition = Unit(
                "supporter",
                3,
                2,
                UnitKeyword.None,
                supportAbility
            );

            CardDefinition distantAllyDefinition = Unit(
                "distant_ally",
                3,
                2,
                UnitKeyword.None
            );

            MatchState state = CreateReadyState();

            CardInstance supporter = AddReserve(state.Host, 1, supporterDefinition);

            CardInstance distantAlly = AddReserve(state.Host, 2, distantAllyDefinition);

            MatchRulesEngine engine = Engine(supporterDefinition, distantAllyDefinition);

            CommandResult result = engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { supporter.InstanceId, 0UL, distantAlly.InstanceId }
            );

            Assert.IsTrue(result.Accepted);

            Assert.AreEqual(2, distantAlly.GetDamage(distantAllyDefinition));

            Assert.AreEqual(3, distantAlly.CurrentHealth);

            Assert.AreEqual(0, distantAlly.PermanentDamageModifier);

            Assert.AreEqual(0, distantAlly.PermanentHealthModifier);
        }

        [Test]
        public void RoundStart_BoardCardTriggers()
        {
            AbilityDefinition roundStartAbility = Ability(
                "round_start_damage",
                AbilityTrigger.RoundStart,
                new EffectDefinition(
                    EffectKind.Damage,
                    EffectTarget.EnemyNexus,
                    amount: 1
                )
            );

            CardDefinition watcherDefinition = Unit(
                "board_watcher",
                3,
                2,
                UnitKeyword.None,
                roundStartAbility
            );

            MatchState state = CreateReadyState();

            CardInstance watcher = AddReserve(state.Host, 1, watcherDefinition);

            bool movedToBoard = state.Host.TryMoveReserveCardToBoard(watcher, 0);

            Assert.IsTrue(movedToBoard);

            Assert.AreEqual(CardZone.Board, watcher.Zone);

            MatchRulesEngine engine = Engine(watcherDefinition);

            CommandResult firstPass = engine.TryPass(state, SeatId.Host);

            Assert.IsTrue(firstPass.Accepted);

            CommandResult secondPass = engine.TryPass(state, SeatId.Guest);

            Assert.IsTrue(secondPass.Accepted);

            Assert.AreEqual(2, state.RoundNumber);

            Assert.AreEqual(19, state.Guest.NexusHealth);

            // Board survivors return to Reserve before RoundStart.
            Assert.AreEqual(CardZone.Reserve, watcher.Zone);

            Assert.IsTrue(state.Host.Reserve.Contains(watcher));

            Assert.AreEqual(MatchPhase.Priority, state.Phase);
        }

        [Test]
        public void RoundStart_DrawHandCardDoesNotTrigger()
        {
            AbilityDefinition roundStartAbility = Ability(
                "hidden_round_start_damage",
                AbilityTrigger.RoundStart,
                new EffectDefinition(
                    EffectKind.Damage,
                    EffectTarget.EnemyNexus,
                    amount: 1
                )
            );

            CardDefinition watcherDefinition = Unit(
                "hand_watcher",
                3,
                2,
                UnitKeyword.None,
                roundStartAbility
            );

            MatchState state = CreateReadyState();

            var handWatcher = new CardInstance(
                1,
                watcherDefinition.Id,
                SeatId.Host,
                CardZone.DrawHand,
                watcherDefinition.BaseHealth
            );

            state.Host.DrawHand.Add(handWatcher);

            MatchRulesEngine engine = Engine(watcherDefinition);

            CommandResult firstPass = engine.TryPass(state, SeatId.Host);

            Assert.IsTrue(firstPass.Accepted);

            CommandResult secondPass = engine.TryPass(state, SeatId.Guest);

            Assert.IsTrue(secondPass.Accepted);

            Assert.AreEqual(2, state.RoundNumber);

            Assert.AreEqual(20, state.Guest.NexusHealth);

            Assert.AreEqual(CardZone.DrawHand, handWatcher.Zone);

            Assert.IsTrue(state.Host.DrawHand.Contains(handWatcher));

            Assert.AreEqual(MatchPhase.Priority, state.Phase);
        }

        [Test]
        public void RoundEnd_DoesNotExpireReserveEphemeral()
        {
            CardDefinition ephemeral = Unit(
                "reserve_ephemeral",
                3,
                2,
                UnitKeyword.Ephemeral
            );

            MatchState state = CreateReadyState();

            CardInstance reserveCard = AddReserve(state.Guest, 10, ephemeral);

            MatchRulesEngine engine = Engine(ephemeral);

            Assert.IsTrue(engine.TryPass(state, SeatId.Host).Accepted);

            CommandResult roundEnd = engine.TryPass(state, SeatId.Guest);

            Assert.IsTrue(roundEnd.Accepted);
            Assert.IsNotNull(roundEnd.RoundTransition);

            Assert.AreEqual(CardZone.Reserve, reserveCard.Zone);

            Assert.IsTrue(state.Guest.Reserve.Contains(reserveCard));

            Assert.IsFalse(state.Guest.Graveyard.Contains(reserveCard));
        }

        [Test]
        public void SupportBuff_IsUsedForCombatButDoesNotPersistInGraveyard()
        {
            AbilityDefinition supportAbility = Ability(
                "quy_cau_support",
                AbilityTrigger.Support,
                new EffectDefinition(
                    EffectKind.Buff,
                    EffectTarget.TriggerSubject,
                    amount: 2,
                    secondaryAmount: 0,
                    duration: EffectDuration.WhileOnBoard
                )
            );

            CardDefinition quyCauDefinition = Unit(
                "QuyCau",
                health: 2,
                damage: 3,
                UnitKeyword.None,
                supportAbility
            );

            CardDefinition maDaDefinition = Unit(
                "MaDa",
                health: 1,
                damage: 3,
                UnitKeyword.CannotBlock | UnitKeyword.Ephemeral
            );

            MatchState state = CreateReadyState();

            CardInstance quyCau = AddReserve(state.Host, 1, quyCauDefinition);
            CardInstance maDa = AddReserve(state.Host, 2, maDaDefinition);

            MatchRulesEngine engine = Engine(quyCauDefinition, maDaDefinition);

            CommandResult attack = engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { quyCau.InstanceId, maDa.InstanceId, 0UL }
            );

            Assert.IsTrue(attack.Accepted);
            Assert.AreEqual(5, maDa.GetDamage(maDaDefinition));

            CommandResult combat = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new ulong[MatchState.BoardSlotCount]
            );

            Assert.IsTrue(combat.Accepted);
            Assert.IsNotNull(combat.Resolution);
            Assert.AreEqual(2, combat.Resolution.Steps.Count);
            Assert.AreEqual(5, combat.Resolution.Steps[1].HostCard.DamageBefore);

            Assert.AreEqual(CardZone.Graveyard, maDa.Zone);
            Assert.AreEqual(0, maDa.BoardDamageModifier);
            Assert.AreEqual(3, maDa.GetDamage(maDaDefinition));

            Assert.AreEqual(1, state.RoundHistory.CurrentRoundDeaths.Count);
            Assert.AreEqual(3, state.RoundHistory.CurrentRoundDeaths[0].DamageAtDeath);
        }

        [Test]
        public void AdjacentBlockerSupport_BuffsRightBlockerBeforeCombat_ThenEndsInReserve()
        {
            AbilityDefinition supportAbility = Ability(
                "quy_cau_support",
                AbilityTrigger.Support,
                new EffectDefinition(
                    EffectKind.Buff,
                    EffectTarget.TriggerSubject,
                    amount: 2,
                    secondaryAmount: 0,
                    duration: EffectDuration.WhileOnBoard
                )
            );

            CardDefinition attackerDefinition = Unit("attacker", 7, 1, UnitKeyword.None);
            CardDefinition supporterDefinition = Unit(
                "QuyCau",
                5,
                3,
                UnitKeyword.None,
                supportAbility
            );
            CardDefinition supportedDefinition = Unit("ally", 5, 1, UnitKeyword.None);

            MatchState state = CreateReadyState();
            CardInstance firstAttacker = AddReserve(state.Host, 1, attackerDefinition);
            CardInstance secondAttacker = AddReserve(state.Host, 2, attackerDefinition);
            CardInstance supporter = AddReserve(state.Guest, 3, supporterDefinition);
            CardInstance supported = AddReserve(state.Guest, 4, supportedDefinition);
            MatchRulesEngine engine = Engine(
                attackerDefinition,
                supporterDefinition,
                supportedDefinition
            );

            Assert.IsTrue(engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new[] { firstAttacker.InstanceId, secondAttacker.InstanceId, 0UL }
            ).Accepted);

            CommandResult combat = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new[] { supporter.InstanceId, supported.InstanceId, 0UL }
            );

            Assert.IsTrue(combat.Accepted);
            Assert.AreEqual(3, combat.Resolution.Steps[1].GuestCard.DamageBefore);

            // The Support buff lasts only while on the board: back in Reserve after
            // combat, the unit shows its base damage even though the round continues.
            Assert.AreEqual(CardZone.Reserve, supported.Zone);
            Assert.AreEqual(0, supported.BoardDamageModifier);
            Assert.AreEqual(1, supported.GetDamage(supportedDefinition));
        }

        private static MatchState CreateReadyState()
        {
            var state = new MatchState(SeatId.Host);

            state.Host.InitializeMana(10);
            state.Guest.InitializeMana(10);

            state.HostMulliganDone = true;
            state.GuestMulliganDone = true;

            state.BeginFirstRound();

            return state;
        }

        private static CardInstance AddReserve(
            PlayerState player,
            ulong instanceId,
            CardDefinition definition
        )
        {
            var card = new CardInstance(
                instanceId,
                definition.Id,
                player.Seat,
                CardZone.Reserve,
                definition.BaseHealth
            );

            player.Reserve.Add(card);
            return card;
        }

        private static MatchRulesEngine Engine(params CardDefinition[] definitions)
        {
            var result = new Dictionary<string, CardDefinition>();

            foreach (CardDefinition definition in definitions)
            {
                result.Add(definition.Id, definition);
            }

            return new MatchRulesEngine(result);
        }

        private static CardDefinition Unit(
            string id,
            int health,
            int damage,
            UnitKeyword keywords,
            params AbilityDefinition[] abilities
        )
        {
            return new CardDefinition(id, id, health, damage, 1, keywords, abilities);
        }

        private static AbilityDefinition Ability(
            string id,
            AbilityTrigger trigger,
            EffectDefinition effect
        )
        {
            return new AbilityDefinition(id, trigger, new[] { effect });
        }
    }
}
