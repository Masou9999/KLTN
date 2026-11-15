using System.Collections.Generic;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class AbilityRuntimeTests
    {
        [TestCase(EffectTarget.AlliedNexus, 4)]
        [TestCase(EffectTarget.EnemyNexus, 4)]
        [TestCase(EffectTarget.AlliedNexus, 0)]
        public void NexusHeal_RecordsActualHealthChange(EffectTarget target, int missingHealth)
        {
            MatchState state = CreateReadyState();
            PlayerState healed = target == EffectTarget.AlliedNexus ? state.Host : state.Guest;
            healed.ApplyNexusDamage(missingHealth);
            var definition = Definition("healer", Ability("heal_test", AbilityTrigger.Play,
                new EffectDefinition(EffectKind.Heal, target, amount: 1)));
            var card = AddHandCard(state.Host, 1, definition.Id, 3);
            var engine = new MatchRulesEngine(new Dictionary<string, CardDefinition> { { definition.Id, definition } });
            Assert.IsTrue(engine.TrySummonUnit(state, SeatId.Host, card.InstanceId).Accepted);
            AbilityResolution resolution = state.TakeAbilityResolution();
            if (missingHealth == 0)
            {
                Assert.IsTrue(resolution == null || resolution.Events.Count == 0);
                return;
            }
            Assert.AreEqual(1, resolution.Events.Count);
            Assert.AreEqual(EffectKind.Heal, resolution.Events[0].Kind);
            Assert.AreEqual(healed.Seat, resolution.Events[0].NexusOwner);
            Assert.AreEqual(16, resolution.Events[0].NexusHealthBefore);
            Assert.AreEqual(17, resolution.Events[0].NexusHealthAfter);
        }

        [Test]
        public void PlayDrawEffect_ExecutesAfterSummon()
        {
            MatchState state = CreateReadyState();

            CardInstance source = AddHandCard(state.Host, 1, "play_draw", 3);

            state.Host.Deck.Add(
                new CardInstance(100, "plain", SeatId.Host, CardZone.Deck, 3)
            );

            var definitions = new Dictionary<string, CardDefinition>
            {
                {
                    "play_draw",
                    Definition(
                        "play_draw",
                        Ability(
                            "play_draw_two",
                            AbilityTrigger.Play,
                            new EffectDefinition(
                                EffectKind.Draw,
                                EffectTarget.SourceOwner,
                                amount: 1
                            )
                        )
                    )
                },
                { "plain", Definition("plain") },
            };

            var engine = new MatchRulesEngine(definitions);

            CommandResult result = engine.TrySummonUnit(
                state,
                SeatId.Host,
                source.InstanceId
            );

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(1, state.Host.Reserve.Count);
            Assert.AreEqual(1, state.Host.DrawHand.Count);
            Assert.AreEqual(0, state.Host.Deck.Count);
        }

        [Test]
        public void AllySummonBuff_ChangesRuntimeStats()
        {
            MatchState state = CreateReadyState();

            var watcher = new CardInstance(
                1,
                "watcher",
                SeatId.Host,
                CardZone.Reserve,
                3
            );

            state.Host.Reserve.Add(watcher);

            CardInstance summoned = AddHandCard(state.Host, 2, "summoned", 3);

            var definitions = new Dictionary<string, CardDefinition>
            {
                {
                    "watcher",
                    Definition(
                        "watcher",
                        Ability(
                            "watch_summon",
                            AbilityTrigger.AllySummon,
                            new EffectDefinition(
                                EffectKind.Buff,
                                EffectTarget.TriggerSubject,
                                amount: 1,
                                secondaryAmount: 1
                            )
                        )
                    )
                },
                { "summoned", Definition("summoned") },
            };

            var engine = new MatchRulesEngine(definitions);

            CommandResult result = engine.TrySummonUnit(
                state,
                SeatId.Host,
                summoned.InstanceId
            );

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(4, summoned.CurrentHealth);

            Assert.AreEqual(3, summoned.GetDamage(definitions["summoned"]));
        }

        [Test]
        public void ConditionalSummon_KillsTwoWeakestEnemiesAfterAlliedDeath()
        {
            MatchState state = CreateReadyState();

            var killWeakest = new AbilityDefinition(
                "ma_tranh_summon",
                AbilityTrigger.Summon,
                new[]
                {
                    new EffectDefinition(
                        EffectKind.Kill,
                        EffectTarget.WeakestEnemies,
                        count: 2
                    ),
                },
                condition: AbilityCondition.AllyDiedThisRound
            );

            var maTranhDefinition = new CardDefinition(
                "ma_tranh",
                "Ma Trành",
                6,
                8,
                8,
                UnitKeyword.Fearsome,
                new[] { killWeakest }
            );

            var allyDefinition = new CardDefinition("ally", "Ally", 2, 2, 1);

            var weakDefinition = new CardDefinition("weak", "Weak", 2, 1, 1);

            var mediumDefinition = new CardDefinition(
                "medium",
                "Medium",
                3,
                2,
                1
            );

            var strongDefinition = new CardDefinition(
                "strong",
                "Strong",
                4,
                5,
                1
            );

            var definitions = new Dictionary<string, CardDefinition>
            {
                { maTranhDefinition.Id, maTranhDefinition },
                { allyDefinition.Id, allyDefinition },
                { weakDefinition.Id, weakDefinition },
                { mediumDefinition.Id, mediumDefinition },
                { strongDefinition.Id, strongDefinition },
            };

            var deadAlly = new CardInstance(
                1,
                allyDefinition.Id,
                SeatId.Host,
                CardZone.Reserve,
                allyDefinition.BaseHealth
            );

            state.Host.Reserve.Add(deadAlly);
            deadAlly.Kill();

            Assert.IsTrue(state.Host.TryMoveDeadActiveCardToGraveyard(deadAlly));

            state.RoundHistory.RecordDeath(
                deadAlly,
                allyDefinition.BaseDamage,
                allyDefinition.BaseHealth
            );

            CardInstance source = AddHandCard(
                state.Host,
                2,
                maTranhDefinition.Id,
                maTranhDefinition.BaseHealth
            );

            var weak = new CardInstance(
                10,
                weakDefinition.Id,
                SeatId.Guest,
                CardZone.Reserve,
                weakDefinition.BaseHealth
            );

            var medium = new CardInstance(
                11,
                mediumDefinition.Id,
                SeatId.Guest,
                CardZone.Reserve,
                mediumDefinition.BaseHealth
            );

            var strong = new CardInstance(
                12,
                strongDefinition.Id,
                SeatId.Guest,
                CardZone.Reserve,
                strongDefinition.BaseHealth
            );

            state.Guest.Reserve.Add(weak);
            state.Guest.Reserve.Add(medium);
            state.Guest.Reserve.Add(strong);

            var engine = new MatchRulesEngine(definitions);

            CommandResult result = engine.TrySummonUnit(
                state,
                SeatId.Host,
                source.InstanceId
            );

            Assert.IsTrue(result.Accepted);
            CollectionAssert.Contains(state.Guest.Graveyard, weak);
            CollectionAssert.Contains(state.Guest.Graveyard, medium);
            CollectionAssert.Contains(state.Guest.Reserve, strong);

            AbilityResolution visual = state.TakeAbilityResolution();
            Assert.AreEqual(2, visual.Events.Count);
            Assert.AreEqual(EffectKind.Kill, visual.Events[0].Kind);
            Assert.AreEqual(weak.InstanceId, visual.Events[0].TargetBefore.InstanceId);
            Assert.AreEqual(medium.InstanceId, visual.Events[1].TargetBefore.InstanceId);
        }

        [Test]
        public void TargetedPlay_OpensSelectionWithoutMutation()
        {
            MatchState state = CreateReadyState();

            CardInstance source = AddHandCard(state.Host, 1, "targeted", 3);

            var alliedTarget = new CardInstance(
                2,
                "plain",
                SeatId.Host,
                CardZone.Reserve,
                3
            );

            state.Host.Reserve.Add(alliedTarget);

            state.Guest.Reserve.Add(
                new CardInstance(10, "plain", SeatId.Guest, CardZone.Reserve, 3)
            );

            Dictionary<string, CardDefinition> definitions = CreateTargetedDefinitions();

            var engine = new MatchRulesEngine(definitions);

            int manaBefore = state.Host.Mana;

            CommandResult result = engine.TrySummonUnit(
                state,
                SeatId.Host,
                source.InstanceId
            );

            Assert.IsTrue(result.Accepted);

            Assert.AreEqual(MatchPhase.AbilitySelection, state.Phase);

            Assert.IsNotNull(state.PendingSelection);

            Assert.AreEqual(manaBefore, state.Host.Mana);

            Assert.AreEqual(CardZone.DrawHand, source.Zone);

            Assert.AreEqual(SeatId.Host, state.ActiveSeat);
        }

        [Test]
        public void InvalidThenValidSelection_IsAtomic()
        {
            MatchState state = CreateReadyState();

            CardInstance source = AddHandCard(state.Host, 1, "targeted", 3);

            var alliedTarget = new CardInstance(
                2,
                "plain",
                SeatId.Host,
                CardZone.Reserve,
                3
            );

            var enemyTarget = new CardInstance(
                10,
                "plain",
                SeatId.Guest,
                CardZone.Reserve,
                3
            );

            state.Host.Reserve.Add(alliedTarget);

            state.Guest.Reserve.Add(enemyTarget);

            Dictionary<string, CardDefinition> definitions = CreateTargetedDefinitions();

            var engine = new MatchRulesEngine(definitions);

            engine.TrySummonUnit(state, SeatId.Host, source.InstanceId);

            ulong requestId = state.PendingSelection.RequestId;

            CommandResult invalid = engine.TrySubmitAbilitySelection(
                state,
                SeatId.Host,
                requestId,
                new AbilityTargetSelection(
                    new[] { enemyTarget.InstanceId },
                    new[] { alliedTarget.InstanceId }
                )
            );

            Assert.IsFalse(invalid.Accepted);
            Assert.IsNotNull(state.PendingSelection);
            Assert.IsFalse(alliedTarget.IsDead);
            Assert.IsFalse(enemyTarget.IsDead);

            CommandResult valid = engine.TrySubmitAbilitySelection(
                state,
                SeatId.Host,
                requestId,
                new AbilityTargetSelection(
                    new[] { alliedTarget.InstanceId },
                    new[] { enemyTarget.InstanceId }
                )
            );

            Assert.IsTrue(valid.Accepted);

            Assert.IsNull(state.PendingSelection);

            Assert.AreEqual(CardZone.Reserve, source.Zone);

            Assert.AreEqual(CardZone.Graveyard, alliedTarget.Zone);

            Assert.AreEqual(CardZone.Graveyard, enemyTarget.Zone);

            Assert.AreEqual(2, state.RoundHistory.CurrentRoundDeaths.Count);

            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);
        }

        [Test]
        public void CombatDeath_ExecutesDeathAndAllyDeath()
        {
            MatchState state = CreateReadyState();

            CardInstance attacker = AddHandCard(state.Host, 1, "attacker", 4);

            CardInstance victim = AddHandCard(state.Guest, 10, "victim", 2);

            var watcher = new CardInstance(
                11,
                "watcher",
                SeatId.Guest,
                CardZone.Reserve,
                3
            );

            state.Guest.Reserve.Add(watcher);

            state.Guest.Deck.Add(
                new CardInstance(100, "plain", SeatId.Guest, CardZone.Deck, 3)
            );

            var definitions = new Dictionary<string, CardDefinition>
            {
                { "attacker", new CardDefinition("attacker", "Attacker", 4, 3, 1) },
                {
                    "victim",
                    Definition(
                        "victim",
                        Ability(
                            "victim_last_breath",
                            AbilityTrigger.Death,
                            new EffectDefinition(
                                EffectKind.Draw,
                                EffectTarget.SourceOwner,
                                amount: 1
                            )
                        )
                    )
                },
                {
                    "watcher",
                    Definition(
                        "watcher",
                        Ability(
                            "watch_ally_death",
                            AbilityTrigger.AllyDeath,
                            new EffectDefinition(
                                EffectKind.Damage,
                                EffectTarget.EnemyNexus,
                                amount: 1
                            )
                        )
                    )
                },
                { "plain", Definition("plain") },
            };

            var engine = new MatchRulesEngine(definitions);

            Assert.IsTrue(
                engine.TrySummonUnit(state, SeatId.Host, attacker.InstanceId).Accepted
            );

            Assert.IsTrue(
                engine.TrySummonUnit(state, SeatId.Guest, victim.InstanceId).Accepted
            );

            Assert.IsTrue(
                engine
                    .TryDeclareAttack(
                        state,
                        SeatId.Host,
                        new[] { attacker.InstanceId, 0UL, 0UL }
                    )
                    .Accepted
            );

            CommandResult combat = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new[] { victim.InstanceId, 0UL, 0UL }
            );

            Assert.IsTrue(combat.Accepted);

            Assert.AreEqual(CardZone.Graveyard, victim.Zone);

            Assert.AreEqual(1, state.Guest.DrawHand.Count);

            Assert.AreEqual(19, state.Host.NexusHealth);

            AbilityResolution visual = state.TakeAbilityResolution();
            Assert.AreEqual(1, visual.Events.Count);
            Assert.AreEqual(EffectKind.Damage, visual.Events[0].Kind);
            Assert.AreEqual(SeatId.Host, visual.Events[0].NexusOwner);
            Assert.AreEqual(20, visual.Events[0].NexusHealthBefore);
            Assert.AreEqual(19, visual.Events[0].NexusHealthAfter);
        }

        [Test]
        public void SimultaneousDeath_MaMatMamAndVongNhiBothResolve()
        {
            // Guest owns the attack token, so Ma Mat Mam is the attacker.
            var state = new MatchState(SeatId.Guest);

            state.Host.InitializeMana(10);
            state.Guest.InitializeMana(10);

            state.HostMulliganDone = true;
            state.GuestMulliganDone = true;
            state.BeginFirstRound();

            // Guest Nexus starts at 19.
            state.Guest.ApplyNexusDamage(1);

            var vongNhi = new CardInstance(
                1,
                "vong_nhi",
                SeatId.Host,
                CardZone.Reserve,
                3
            );

            state.Host.Reserve.Add(vongNhi);

            CardInstance maDa = AddHandCard(state.Host, 2, "ma_da", 1);

            CardInstance maMatMam = AddHandCard(state.Guest, 3, "ma_mat_mam", 1);

            // Ma Mat Mam must have a card available to draw.
            state.Guest.Deck.Add(
                new CardInstance(4, "plain", SeatId.Guest, CardZone.Deck, 3)
            );

            var definitions = new Dictionary<string, CardDefinition>
            {
                {
                    "vong_nhi",
                    new CardDefinition(
                        "vong_nhi",
                        "Vong Nhi",
                        3,
                        0,
                        3,
                        abilities: new[]
                        {
                            Ability(
                                "vong_nhi_ally_death",
                                AbilityTrigger.AllyDeath,
                                new EffectDefinition(
                                    EffectKind.Damage,
                                    EffectTarget.EnemyNexus,
                                    amount: 1
                                )
                            ),
                        }
                    )
                },
                { "ma_da", new CardDefinition("ma_da", "Ma Da", 1, 3, 1) },
                {
                    "ma_mat_mam",
                    new CardDefinition(
                        "ma_mat_mam",
                        "Ma Mat Mam",
                        1,
                        3,
                        2,
                        abilities: new[]
                        {
                            Ability(
                                "ma_mat_mam_last_breath",
                                AbilityTrigger.Death,
                                new EffectDefinition(
                                    EffectKind.Draw,
                                    EffectTarget.SourceOwner,
                                    amount: 1
                                ),
                                new EffectDefinition(
                                    EffectKind.Heal,
                                    EffectTarget.AlliedNexus,
                                    amount: 2
                                )
                            ),
                        }
                    )
                },
                { "plain", Definition("plain") },
            };

            var engine = new MatchRulesEngine(definitions);

            // Guest summons Ma Mat Mam.
            Assert.IsTrue(
                engine.TrySummonUnit(state, SeatId.Guest, maMatMam.InstanceId).Accepted
            );

            // Host summons Ma Da.
            Assert.IsTrue(
                engine.TrySummonUnit(state, SeatId.Host, maDa.InstanceId).Accepted
            );

            // Ma Mat Mam attacks.
            Assert.IsTrue(
                engine
                    .TryDeclareAttack(
                        state,
                        SeatId.Guest,
                        new[] { maMatMam.InstanceId, 0UL, 0UL }
                    )
                    .Accepted
            );

            // Ma Da blocks. Both cards have 1 HP and deal 3 damage.
            CommandResult combat = engine.TryDeclareBlock(
                state,
                SeatId.Host,
                new[] { maDa.InstanceId, 0UL, 0UL }
            );

            Assert.IsTrue(combat.Accepted);

            Assert.AreEqual(CardZone.Graveyard, maMatMam.Zone);

            Assert.AreEqual(CardZone.Graveyard, maDa.Zone);

            // Proves Ma Mat Mam's Death ability drew a card.
            Assert.AreEqual(1, state.Guest.DrawHand.Count);

            Assert.AreEqual(0, state.Guest.Deck.Count);

            // Ma Mat Mam: 19 -> 20.
            // Vong Nhi: 20 -> 19.
            Assert.AreEqual(19, state.Guest.NexusHealth);

            Assert.AreEqual(2, state.RoundHistory.CurrentRoundDeaths.Count);
        }

        private static Dictionary<string, CardDefinition> CreateTargetedDefinitions()
        {
            var primary = new AbilityTargetRequirement(
                AbilityTargetSlot.Primary,
                TargetRelation.Ally,
                AbilityTargetZone.ActiveRoster,
                excludeSource: true
            );

            var secondary = new AbilityTargetRequirement(
                AbilityTargetSlot.Secondary,
                TargetRelation.Enemy,
                AbilityTargetZone.ActiveRoster
            );

            var ability = new AbilityDefinition(
                "targeted_play",
                AbilityTrigger.Play,
                new[]
                {
                    new EffectDefinition(
                        EffectKind.Sacrifice,
                        EffectTarget.PrimarySelection
                    ),
                    new EffectDefinition(
                        EffectKind.Kill,
                        EffectTarget.SecondarySelection
                    ),
                },
                requiredTargets: new[] { primary, secondary }
            );

            return new Dictionary<string, CardDefinition>
            {
                {
                    "targeted",
                    new CardDefinition(
                        "targeted",
                        "Targeted",
                        3,
                        2,
                        1,
                        abilities: new[] { ability }
                    )
                },
                { "plain", Definition("plain") },
            };
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

        private static CardInstance AddHandCard(
            PlayerState player,
            ulong instanceId,
            string definitionId,
            int health
        )
        {
            var card = new CardInstance(
                instanceId,
                definitionId,
                player.Seat,
                CardZone.DrawHand,
                health
            );

            player.DrawHand.Add(card);
            return card;
        }

        private static CardDefinition Definition(
            string id,
            params AbilityDefinition[] abilities
        )
        {
            return new CardDefinition(id, id, 3, 2, 1, abilities: abilities);
        }

        private static AbilityDefinition Ability(
            string id,
            AbilityTrigger trigger,
            params EffectDefinition[] effects
        )
        {
            return new AbilityDefinition(id, trigger, effects);
        }

        [Test]
        public void OptionalTargetedPlayWithoutTargets_SummonsAndPassesPriority()
        {
            MatchState state = CreateReadyState();

            var requirement = new AbilityTargetRequirement(
                AbilityTargetSlot.Primary,
                TargetRelation.Ally,
                AbilityTargetZone.DrawHand,
                excludeSource: true
            );

            var ability = new AbilityDefinition(
                "optional_play",
                AbilityTrigger.Play,
                new[]
                {
                    new EffectDefinition(
                        EffectKind.ReduceCost,
                        EffectTarget.PrimarySelection,
                        amount: 1
                    ),
                },
                requiredTargets: new[] { requirement },
                missingTargetPolicy: MissingTargetPolicy.SkipAbility
            );

            var definition = new CardDefinition(
                "optional_unit",
                "Optional Unit",
                3,
                2,
                1,
                abilities: new[] { ability }
            );

            CardInstance source = AddHandCard(
                state.Host,
                1,
                definition.Id,
                definition.BaseHealth
            );

            var definitions = new Dictionary<string, CardDefinition>
            {
                { definition.Id, definition },
            };

            var engine = new MatchRulesEngine(definitions);

            int manaBefore = state.Host.Mana;

            CommandResult result = engine.TrySummonUnit(
                state,
                SeatId.Host,
                source.InstanceId
            );

            Assert.IsTrue(result.Accepted);
            Assert.IsNull(state.PendingSelection);
            Assert.AreEqual(CardZone.Reserve, source.Zone);
            Assert.AreEqual(manaBefore - 1, state.Host.Mana);
            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);
            Assert.AreEqual(MatchPhase.Priority, state.Phase);
        }

        [Test]
        public void CancelTargetedPlay_AllowsRetryWithNewRequest()
        {
            MatchState state = CreateReadyState();

            CardInstance source = AddHandCard(state.Host, 1, "targeted", 3);

            state.Host.Reserve.Add(
                new CardInstance(2, "plain", SeatId.Host, CardZone.Reserve, 3)
            );

            state.Guest.Reserve.Add(
                new CardInstance(10, "plain", SeatId.Guest, CardZone.Reserve, 3)
            );

            Dictionary<string, CardDefinition> definitions = CreateTargetedDefinitions();

            var engine = new MatchRulesEngine(definitions);

            int manaBefore = state.Host.Mana;

            CommandResult firstAttempt = engine.TrySummonUnit(
                state,
                SeatId.Host,
                source.InstanceId
            );

            Assert.IsTrue(firstAttempt.Accepted);
            Assert.IsNotNull(state.PendingSelection);
            Assert.IsTrue(state.PendingSelection.CanCancel);

            ulong firstRequestId = state.PendingSelection.RequestId;

            CommandResult cancel = engine.TryCancelAbilitySelection(
                state,
                SeatId.Host,
                firstRequestId
            );

            Assert.IsTrue(cancel.Accepted);
            Assert.IsNull(state.PendingSelection);
            Assert.AreEqual(CardZone.DrawHand, source.Zone);
            Assert.AreEqual(manaBefore, state.Host.Mana);
            Assert.AreEqual(SeatId.Host, state.ActiveSeat);

            CommandResult secondAttempt = engine.TrySummonUnit(
                state,
                SeatId.Host,
                source.InstanceId
            );

            Assert.IsTrue(secondAttempt.Accepted);
            Assert.IsNotNull(state.PendingSelection);

            Assert.AreNotEqual(firstRequestId, state.PendingSelection.RequestId);
        }
    }
}
