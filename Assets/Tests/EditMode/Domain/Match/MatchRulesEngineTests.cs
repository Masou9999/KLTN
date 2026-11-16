using System.Collections.Generic;
using KLTN.Game.Domain;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class MatchRulesEngineTests
    {
        private Dictionary<string, CardDefinition> definitions;
        private MatchRulesEngine engine;

        [SetUp]
        public void SetUp()
        {
            definitions = new Dictionary<string, CardDefinition>
            {
                ["host_unit"] = new CardDefinition("host_unit", "Host Unit", 4, 3, 1),

                ["guest_unit"] = new CardDefinition("guest_unit", "Guest Unit", 2, 2, 1),

                ["small_unit"] = new CardDefinition("small_unit", "Small Unit", 10, 1, 1),

                ["expensive"] = new CardDefinition(
                    "expensive",
                    "Expensive Unit",
                    5,
                    5,
                    2
                ),
            };

            engine = new MatchRulesEngine(definitions);
        }

        [Test]
        public void FirstPass_TransfersPriorityAndOffersEndRound()
        {
            MatchState state = CreateState();

            CommandResult result = engine.TryPass(state, SeatId.Host);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(1, state.RoundNumber);
            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);
            Assert.AreEqual(1, state.ConsecutivePasses);
            Assert.IsTrue(state.CanEndRound);
        }

        [Test]
        public void SecondConsecutivePass_EndsRoundAndAlternatesToken()
        {
            MatchState state = CreateState();

            engine.TryPass(state, SeatId.Host);
            CommandResult result = engine.TryPass(state, SeatId.Guest);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(2, state.RoundNumber);
            Assert.AreEqual(SeatId.Guest, state.AttackTokenOwner);

            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);
            Assert.AreEqual(0, state.ConsecutivePasses);
            Assert.IsFalse(state.CanEndRound);
            Assert.IsTrue(state.AttackTokenAvailable);
            Assert.AreEqual(MatchPhase.Priority, state.Phase);

            Assert.IsNotNull(result.RoundTransition);

            Assert.AreEqual(1, result.RoundTransition.CompletedRoundNumber);

            Assert.AreEqual(2, result.RoundTransition.NextRoundNumber);

            Assert.AreEqual(SeatId.Guest, result.RoundTransition.NextAttackTokenOwner);
        }

        [Test]
        public void NonPassActionAfterPass_ResetsPassChain()
        {
            MatchState state = CreateState();

            engine.TryPass(state, SeatId.Host);

            CommandResult result = engine.TrySummonUnit(state, SeatId.Guest, 10);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(0, state.ConsecutivePasses);
            Assert.AreEqual(SeatId.Host, state.ActiveSeat);
            Assert.AreEqual(1, state.RoundNumber);
            Assert.IsFalse(state.CanEndRound);

            Assert.AreEqual(0, state.Guest.DrawHand.Count);
            Assert.AreEqual(1, state.Guest.Reserve.Count);
            Assert.AreEqual(CardZone.Reserve, state.Guest.Reserve[0].Zone);
            Assert.AreEqual(0, state.Guest.Mana);
        }

        [Test]
        public void TwoSummonActions_DoNotResolveCombat()
        {
            MatchState state = CreateState();

            CommandResult hostResult = engine.TrySummonUnit(state, SeatId.Host, 1);

            CommandResult guestResult = engine.TrySummonUnit(state, SeatId.Guest, 10);

            Assert.IsTrue(hostResult.Accepted);
            Assert.IsTrue(guestResult.Accepted);

            Assert.IsNull(hostResult.Resolution);
            Assert.IsNull(guestResult.Resolution);

            Assert.AreEqual(1, state.RoundNumber);
            Assert.AreEqual(SeatId.Host, state.ActiveSeat);

            Assert.AreEqual(1, state.Host.Reserve.Count);
            Assert.AreEqual(1, state.Guest.Reserve.Count);

            Assert.AreEqual(0, state.Host.Board.Count);
            Assert.AreEqual(0, state.Guest.Board.Count);

            Assert.AreEqual(20, state.Host.NexusHealth);
            Assert.AreEqual(20, state.Guest.NexusHealth);
        }

        [Test]
        public void Combat_ReturnsSurvivorsBeforePostCombatPriority_ThenPassesStartNextRound()
        {
            MatchState state = CreateState();

            PrepareHostAttack(state, attackSlot: 0);

            CommandResult combatResult = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new ulong[] { 10, 0, 0 }
            );

            Assert.IsTrue(combatResult.Accepted);

            Assert.AreEqual(0, state.Host.Board.Count);
            Assert.AreEqual(0, state.Guest.Board.Count);

            Assert.AreEqual(1, state.Host.Reserve.Count);
            Assert.AreEqual(2, state.Host.Reserve[0].CurrentHealth);

            Assert.AreEqual(MatchPhase.Priority, state.Phase);
            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);

            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);

            CommandResult firstPass = engine.TryPass(state, SeatId.Guest);

            Assert.IsTrue(firstPass.Accepted);
            Assert.IsNull(firstPass.RoundTransition);

            CommandResult secondPass = engine.TryPass(state, SeatId.Host);

            Assert.IsTrue(secondPass.Accepted);
            Assert.IsNotNull(secondPass.RoundTransition);

            Assert.AreEqual(1, secondPass.RoundTransition.CompletedRoundNumber);

            Assert.AreEqual(2, secondPass.RoundTransition.NextRoundNumber);

            Assert.AreEqual(0, state.RoundHistory.CurrentRoundDeaths.Count);

            Assert.AreEqual(1, state.RoundHistory.PreviousRoundDeaths.Count);

            Assert.AreEqual(
                SeatId.Guest,
                state.RoundHistory.PreviousRoundDeaths[0].Owner
            );

            Assert.AreEqual(
                SeatId.Guest,
                secondPass.RoundTransition.NextAttackTokenOwner
            );

            Assert.AreEqual(0, state.Host.Board.Count);
            Assert.AreEqual(0, state.Guest.Board.Count);

            Assert.AreEqual(1, state.Host.Reserve.Count);
            Assert.AreEqual(2, state.Host.Reserve[0].CurrentHealth);

            Assert.AreEqual(1, state.Guest.Graveyard.Count);

            Assert.AreEqual(2, state.RoundNumber);
            Assert.AreEqual(SeatId.Guest, state.AttackTokenOwner);
            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);
            Assert.IsTrue(state.AttackTokenAvailable);
            Assert.AreEqual(MatchPhase.Priority, state.Phase);
        }

        [Test]
        public void DeclareAttack_MovesCardsToSlotsAndBeginsBlocking()
        {
            MatchState state = CreateState();

            engine.TrySummonUnit(state, SeatId.Host, 1);

            // Guest returns priority to Host.
            engine.TryPass(state, SeatId.Guest);

            CommandResult result = engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new ulong[] { 0, 1, 0 }
            );

            Assert.IsTrue(result.Accepted);

            Assert.AreEqual(0, state.Host.Reserve.Count);
            Assert.AreEqual(1, state.Host.Board.Count);

            CardInstance attacker = state.Host.FindBoardCard(1);

            Assert.IsNotNull(attacker);
            Assert.AreEqual(1UL, attacker.InstanceId);
            Assert.AreEqual(CardZone.Board, attacker.Zone);
            Assert.AreEqual(1, attacker.BoardSlotIndex);

            Assert.IsFalse(state.AttackTokenAvailable);
            Assert.AreEqual(MatchPhase.BlockDeclaration, state.Phase);

            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);

            Assert.AreEqual(0, state.ConsecutivePasses);
            Assert.AreEqual(1, state.RoundNumber);
        }

        [Test]
        public void DeclareAttack_ByNonTokenOwner_IsRejected()
        {
            MatchState state = CreateState();

            CardInstance guestCard = state.Guest.FindCardInDrawHand(10);

            Assert.IsTrue(state.Guest.TryMoveDrawHandCardToReserve(guestCard));

            engine.TryPass(state, SeatId.Host);

            CommandResult result = engine.TryDeclareAttack(
                state,
                SeatId.Guest,
                new ulong[] { 10, 0, 0 }
            );

            Assert.IsFalse(result.Accepted);

            Assert.AreEqual(
                CommandRejectionReason.NotAttackTokenOwner,
                result.RejectionReason
            );

            Assert.AreEqual(1, state.Guest.Reserve.Count);
            Assert.AreEqual(0, state.Guest.Board.Count);

            Assert.IsTrue(state.AttackTokenAvailable);
            Assert.AreEqual(MatchPhase.Priority, state.Phase);

            // A rejected action must not break the pass chain.
            Assert.AreEqual(1, state.ConsecutivePasses);
        }

        [Test]
        public void DeclareAttack_WithDuplicateCard_IsAtomicAndRejected()
        {
            MatchState state = CreateState();

            CardInstance card = state.Host.FindCardInDrawHand(1);

            Assert.IsTrue(state.Host.TryMoveDrawHandCardToReserve(card));

            CommandResult result = engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new ulong[] { 1, 0, 1 }
            );

            Assert.IsFalse(result.Accepted);

            Assert.AreEqual(
                CommandRejectionReason.InvalidAttackDeclaration,
                result.RejectionReason
            );

            Assert.AreEqual(1, state.Host.Reserve.Count);
            Assert.AreEqual(0, state.Host.Board.Count);
            Assert.IsTrue(state.AttackTokenAvailable);
        }

        [Test]
        public void DeclareAttack_WithoutCards_IsRejected()
        {
            MatchState state = CreateState();

            CommandResult result = engine.TryDeclareAttack(
                state,
                SeatId.Host,
                new ulong[] { 0, 0, 0 }
            );

            Assert.IsFalse(result.Accepted);

            Assert.AreEqual(
                CommandRejectionReason.InvalidAttackDeclaration,
                result.RejectionReason
            );

            Assert.AreEqual(MatchPhase.Priority, state.Phase);
            Assert.IsTrue(state.AttackTokenAvailable);
        }

        [Test]
        public void RejectedAction_DoesNotBreakPassChain()
        {
            MatchState state = CreateState();

            engine.TryPass(state, SeatId.Host);

            state.Guest.DrawHand.Clear();
            state.Guest.DrawHand.Add(
                new CardInstance(11, "expensive", SeatId.Guest, CardZone.DrawHand, 5)
            );

            CommandResult result = engine.TrySummonUnit(state, SeatId.Guest, 11);

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(
                CommandRejectionReason.InsufficientMana,
                result.RejectionReason
            );

            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);
            Assert.AreEqual(1, state.ConsecutivePasses);
            Assert.IsTrue(state.CanEndRound);
        }

        [Test]
        public void Summon_WhenActiveRosterIsFull_IsRejected()
        {
            MatchState state = CreateState();

            for (ulong i = 0; i < MatchState.MaximumActiveRosterSize; i++)
            {
                state.Host.Reserve.Add(
                    new CardInstance(
                        300 + i,
                        "small_unit",
                        SeatId.Host,
                        CardZone.Reserve,
                        10
                    )
                );
            }

            int manaBefore = state.Host.Mana;

            CommandResult result = engine.TrySummonUnit(state, SeatId.Host, 1);

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(
                CommandRejectionReason.ActiveRosterFull,
                result.RejectionReason
            );

            Assert.AreEqual(manaBefore, state.Host.Mana);
            Assert.AreEqual(1, state.Host.DrawHand.Count);
            Assert.AreEqual(4, state.Host.Reserve.Count);
            Assert.AreEqual(SeatId.Host, state.ActiveSeat);
            Assert.AreEqual(0, state.ConsecutivePasses);
        }

        [Test]
        public void EveryCompletedRound_DrawsForBothPlayersAndRefillsMana()
        {
            MatchState state = CreateState();

            int hostDeckBefore = state.Host.Deck.Count;
            int hostHandBefore = state.Host.DrawHand.Count;
            int guestDeckBefore = state.Guest.Deck.Count;
            int guestHandBefore = state.Guest.DrawHand.Count;

            engine.TryPass(state, SeatId.Host);
            engine.TryPass(state, SeatId.Guest);

            Assert.AreEqual(2, state.RoundNumber);
            Assert.AreEqual(2, state.Host.Mana);
            Assert.AreEqual(hostHandBefore + 1, state.Host.DrawHand.Count);
            Assert.AreEqual(guestHandBefore + 1, state.Guest.DrawHand.Count);
            Assert.AreEqual(hostDeckBefore - 1, state.Host.Deck.Count);
            Assert.AreEqual(guestDeckBefore - 1, state.Guest.Deck.Count);
            Assert.AreEqual(2, state.Guest.Mana);

            engine.TryPass(state, SeatId.Guest);
            engine.TryPass(state, SeatId.Host);

            Assert.AreEqual(3, state.RoundNumber);
            Assert.AreEqual(3, state.Host.Mana);
            Assert.AreEqual(hostHandBefore + 2, state.Host.DrawHand.Count);
            Assert.AreEqual(guestHandBefore + 2, state.Guest.DrawHand.Count);
            Assert.AreEqual(3, state.Guest.Mana);

            Assert.AreEqual(hostDeckBefore - 2, state.Host.Deck.Count);
            Assert.AreEqual(guestDeckBefore - 2, state.Guest.Deck.Count);
        }

        [Test]
        public void NewRound_WithOneEmptyDeck_StillDrawsForOtherPlayer()
        {
            MatchState state = CreateState();
            state.Host.Deck.Clear();
            int hostHandBefore = state.Host.DrawHand.Count;
            int guestHandBefore = state.Guest.DrawHand.Count;

            Assert.IsTrue(engine.TryPass(state, SeatId.Host).Accepted);
            Assert.IsTrue(engine.TryPass(state, SeatId.Guest).Accepted);

            Assert.AreEqual(2, state.RoundNumber);
            Assert.AreEqual(hostHandBefore, state.Host.DrawHand.Count);
            Assert.AreEqual(0, state.Host.Deck.Count);
            Assert.AreEqual(guestHandBefore + 1, state.Guest.DrawHand.Count);
            Assert.IsFalse(state.IsFinished);
        }

        [Test]
        public void FinishedMatch_DoesNotAdvanceRoundOrDraw()
        {
            MatchState state = CreateState();
            int hostHandBefore = state.Host.DrawHand.Count;
            int guestHandBefore = state.Guest.DrawHand.Count;
            int hostDeckBefore = state.Host.Deck.Count;
            int guestDeckBefore = state.Guest.Deck.Count;

            Assert.IsTrue(engine.TryPass(state, SeatId.Host).Accepted);
            Assert.IsTrue(engine.TrySurrender(state, SeatId.Guest).Accepted);
            Assert.IsFalse(engine.TryPass(state, SeatId.Guest).Accepted);

            Assert.AreEqual(1, state.RoundNumber);
            Assert.AreEqual(hostHandBefore, state.Host.DrawHand.Count);
            Assert.AreEqual(guestHandBefore, state.Guest.DrawHand.Count);
            Assert.AreEqual(hostDeckBefore, state.Host.Deck.Count);
            Assert.AreEqual(guestDeckBefore, state.Guest.Deck.Count);
        }

        [Test]
        public void WrongActor_IsRejectedWithoutChangingPriority()
        {
            MatchState state = CreateState();

            CommandResult result = engine.TryPass(state, SeatId.Guest);

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(CommandRejectionReason.NotYourTurn, result.RejectionReason);

            Assert.AreEqual(SeatId.Host, state.ActiveSeat);
            Assert.AreEqual(0, state.ConsecutivePasses);
        }

        [Test]
        public void CommandOutsidePriorityPhase_IsRejected()
        {
            MatchState state = CreateState();
            state.Phase = MatchPhase.BlockDeclaration;

            CommandResult result = engine.TryPass(state, SeatId.Host);

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(CommandRejectionReason.InvalidPhase, result.RejectionReason);

            Assert.AreEqual(0, state.ConsecutivePasses);
            Assert.AreEqual(1, state.RoundNumber);
        }

        [Test]
        public void DeclareBlock_OnAttackerSlot_ResolvesUnitCombat()
        {
            MatchState state = CreateState();

            PrepareHostAttack(state, attackSlot: 0);

            CommandResult result = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new ulong[] { 10, 0, 0 }
            );

            Assert.IsTrue(result.Accepted);
            Assert.IsNotNull(result.Resolution);
            Assert.AreEqual(1, result.Resolution.Steps.Count);

            CombatStep step = result.Resolution.Steps[0];

            Assert.IsTrue(step.IsUnitCombat);

            Assert.AreEqual(4, step.HostCard.HealthBefore);
            Assert.AreEqual(2, step.HostCard.HealthAfter);
            Assert.AreEqual(2, step.HostCard.DamageTaken);
            Assert.IsFalse(step.HostCard.Died);

            Assert.AreEqual(2, step.GuestCard.HealthBefore);
            Assert.AreEqual(0, step.GuestCard.HealthAfter);
            Assert.AreEqual(2, step.GuestCard.DamageTaken);
            Assert.IsTrue(step.GuestCard.Died);

            Assert.AreEqual(20, state.Host.NexusHealth);
            Assert.AreEqual(20, state.Guest.NexusHealth);

            Assert.AreEqual(0, state.Host.Board.Count);
            Assert.AreEqual(0, state.Guest.Board.Count);

            Assert.AreEqual(1, state.Host.Reserve.Count);
            Assert.AreEqual(2, state.Host.Reserve[0].CurrentHealth);

            Assert.AreEqual(1, state.Guest.Graveyard.Count);

            Assert.AreEqual(1, state.RoundHistory.CurrentRoundDeaths.Count);

            DeathRecord death = state.RoundHistory.CurrentRoundDeaths[0];

            Assert.AreEqual(SeatId.Guest, death.Owner);

            Assert.AreEqual(10UL, death.CardInstanceId);

            Assert.AreEqual(MatchPhase.Priority, state.Phase);
            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);
            Assert.IsFalse(state.AttackTokenAvailable);
        }

        [Test]
        public void EmptyBlockDeclaration_AllowsDirectNexusAttack()
        {
            MatchState state = CreateState();

            PrepareHostAttack(state, attackSlot: 1);

            CommandResult result = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new ulong[] { 0, 0, 0 }
            );

            Assert.IsTrue(result.Accepted);
            Assert.IsNotNull(result.Resolution);
            Assert.AreEqual(1, result.Resolution.Steps.Count);

            CombatStep step = result.Resolution.Steps[0];

            Assert.IsTrue(step.IsDirectAttack);
            Assert.IsNotNull(step.HostCard);
            Assert.IsNull(step.GuestCard);

            Assert.AreEqual(3, step.GuestNexusDamageTaken);
            Assert.AreEqual(17, state.Guest.NexusHealth);

            // Defender's unused unit stays in Reserve.
            Assert.AreEqual(1, state.Guest.Reserve.Count);

            // Defender does not attack the Host Nexus.
            Assert.AreEqual(20, state.Host.NexusHealth);

            Assert.AreEqual(MatchPhase.Priority, state.Phase);
            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);
        }

        [Test]
        public void BlockerOnSlotWithoutAttacker_IsRejectedAtomically()
        {
            MatchState state = CreateState();

            PrepareHostAttack(state, attackSlot: 1);

            CommandResult result = engine.TryDeclareBlock(
                state,
                SeatId.Guest,
                new ulong[] { 10, 0, 0 }
            );

            Assert.IsFalse(result.Accepted);

            Assert.AreEqual(
                CommandRejectionReason.BlockerHasNoAttacker,
                result.RejectionReason
            );

            Assert.AreEqual(1, state.Guest.Reserve.Count);
            Assert.AreEqual(0, state.Guest.Board.Count);

            Assert.AreEqual(20, state.Host.NexusHealth);
            Assert.AreEqual(20, state.Guest.NexusHealth);

            Assert.AreEqual(MatchPhase.BlockDeclaration, state.Phase);

            Assert.AreEqual(SeatId.Guest, state.ActiveSeat);
        }

        [Test]
        public void AttackOwner_CannotSubmitBlockDeclaration()
        {
            MatchState state = CreateState();

            PrepareHostAttack(state, attackSlot: 0);

            CommandResult result = engine.TryDeclareBlock(
                state,
                SeatId.Host,
                new ulong[] { 0, 0, 0 }
            );

            Assert.IsFalse(result.Accepted);

            Assert.AreEqual(CommandRejectionReason.NotYourTurn, result.RejectionReason);

            Assert.AreEqual(MatchPhase.BlockDeclaration, state.Phase);
        }

        private static MatchState CreateState()
        {
            var state = new MatchState(SeatId.Host);

            state.Host.InitializeMana(1);
            state.Guest.InitializeMana(1);

            state.Host.DrawHand.Add(
                new CardInstance(1, "host_unit", SeatId.Host, CardZone.DrawHand, 4)
            );

            state.Guest.DrawHand.Add(
                new CardInstance(10, "guest_unit", SeatId.Guest, CardZone.DrawHand, 2)
            );

            for (ulong i = 0; i < 3; i++)
            {
                state.Host.Deck.Add(
                    new CardInstance(
                        100 + i,
                        "small_unit",
                        SeatId.Host,
                        CardZone.Deck,
                        10
                    )
                );

                state.Guest.Deck.Add(
                    new CardInstance(
                        200 + i,
                        "small_unit",
                        SeatId.Guest,
                        CardZone.Deck,
                        10
                    )
                );
            }

            state.HostMulliganDone = true;
            state.GuestMulliganDone = true;
            state.BeginFirstRound();

            return state;
        }

        private void PrepareHostAttack(MatchState state, int attackSlot)
        {
            CommandResult hostSummon = engine.TrySummonUnit(state, SeatId.Host, 1);

            Assert.IsTrue(hostSummon.Accepted);

            CommandResult guestSummon = engine.TrySummonUnit(state, SeatId.Guest, 10);

            Assert.IsTrue(guestSummon.Accepted);

            var attackers = new ulong[MatchState.BoardSlotCount];

            attackers[attackSlot] = 1;

            CommandResult attack = engine.TryDeclareAttack(state, SeatId.Host, attackers);

            Assert.IsTrue(attack.Accepted);
            Assert.AreEqual(MatchPhase.BlockDeclaration, state.Phase);
        }
    }
}
