using System;
using System.Collections.Generic;
using KLTN.Game.Domain;
using NUnit.Framework;

namespace KLTN.Game.ApplicationLayer.Tests
{
    public sealed class MatchTurnTimerTests
    {
        private MatchSession session;
        private MatchCommandService commands;
        private ulong commandId;

        [SetUp]
        public void SetUp()
        {
            session = new MatchSession("timer-test");
            session.TryTakeSeat(1, SeatId.Host);
            session.TryTakeSeat(2, SeatId.Guest);
            session.Seats.Bind(SeatId.Host, "host");
            session.Seats.Bind(SeatId.Guest, "guest");
            var registry = new MatchSessionRegistry(2);
            registry.TryAdd(session);
            registry.Bind(1, session);
            registry.Bind(2, session);
            commands = new MatchCommandService(registry);
            var state = new MatchState { HostMulliganDone = true, GuestMulliganDone = true };
            state.BeginFirstRound();
            session.Start(state, new MatchRulesEngine(new Dictionary<string, CardDefinition>()), true, 0);
            session.TurnTimer.Reconcile(session, 100);
            Ready(SeatId.Host, 100);
            commandId = 0;
        }

        private void Ready(SeatId seat, double now) => session.TurnTimer.MarkPresented(seat, session.TurnTimer.For(seat).Id, now);
        private MatchCommandOutcome Pass(SeatId seat, double now, ulong? id = null) => commands.ExecuteTimed(
            seat == SeatId.Host ? 1UL : 2UL, ++commandId,
            id ?? session.TurnTimer.For(seat).Id, now, (s, actor) => s.Rules.TryPass(s.State, actor));

        [TestCase(119.999, true)]
        [TestCase(120, true)]
        [TestCase(120.299, true)]
        [TestCase(120.3, false)]
        [TestCase(121, false)]
        public void FixedTolerance_IsBounded(double receivedAt, bool accepted)
        {
            Assert.AreEqual(accepted, Pass(SeatId.Host, receivedAt).Accepted);
            Assert.AreEqual(accepted ? SeatId.Guest : SeatId.Host, session.State.ActiveSeat);
        }

        [Test]
        public void LastDecisionOfRound3_ThenFirstOfRound4_ForfeitsBeforeSecondPass()
        {
            for (int round = 1; round < 3; round++)
            {
                session.Rules.TryPass(session.State, session.State.ActiveSeat);
                session.Rules.TryPass(session.State, session.State.ActiveSeat);
            }
            session.Rules.TryPass(session.State, SeatId.Host);
            session.TurnTimer.Reconcile(session, 100);
            Ready(SeatId.Guest, 100);
            ulong oldId = session.TurnTimer.For(SeatId.Guest).Id;
            Assert.IsTrue(session.TurnTimer.Expire(session, SeatId.Guest, 120.3).Accepted);
            Assert.AreEqual(4, session.State.RoundNumber);
            Assert.AreEqual(SeatId.Guest, session.State.ActiveSeat);
            Assert.AreEqual(1, session.State.Guest.ConsecutiveTimeouts);
            Assert.AreNotEqual(oldId, session.TurnTimer.For(SeatId.Guest).Id);
            Ready(SeatId.Guest, 121);
            Assert.IsTrue(session.TurnTimer.Expire(session, SeatId.Guest, 141.3).Accepted);
            Assert.AreEqual(MatchOutcome.HostWon, session.State.Outcome);
            Assert.AreEqual(SeatId.Guest, session.State.TimedOutSeat);
            Assert.AreEqual(4, session.State.RoundNumber);
            Assert.AreEqual(0, session.State.ConsecutivePasses);
            Assert.IsNull(session.TurnTimer.Expire(session, SeatId.Guest, 200));
            Assert.IsTrue(session.TryBeginSettlement());
            Assert.IsFalse(session.TryBeginSettlement());
        }

        [Test]
        public void ManualPass_ClearsOnlyActorsStreak_AndRejectsOldDecision()
        {
            session.TurnTimer.Expire(session, SeatId.Host, 120.3);
            Ready(SeatId.Guest, 121);
            ulong oldId = session.TurnTimer.For(SeatId.Guest).Id;
            Assert.IsTrue(Pass(SeatId.Guest, 122).Accepted);
            Assert.AreEqual(1, session.State.Host.ConsecutiveTimeouts);
            // Guest ends the round and also gets its first decision: same seat, new ID.
            Assert.AreEqual(CommandRejectionReason.StaleDecision, Pass(SeatId.Guest, 123, oldId).Result.RejectionReason);
            Ready(SeatId.Guest, 123);
            Assert.IsTrue(Pass(SeatId.Guest, 124).Accepted);
            Ready(SeatId.Host, 125);
            Assert.IsTrue(Pass(SeatId.Host, 126).Accepted);
            Assert.AreEqual(0, session.State.Host.ConsecutiveTimeouts);
        }

        [Test]
        public void AcceptedCommit_IsNotTimedOutWhileResponseOrAnimationWaits()
        {
            ulong old = session.TurnTimer.For(SeatId.Host).Id;
            Assert.IsTrue(Pass(SeatId.Host, 120.2).Accepted);
            Assert.IsNull(session.TurnTimer.Expire(session, SeatId.Host, 125));
            Assert.AreEqual(0, session.State.Host.ConsecutiveTimeouts);
            Assert.AreEqual(CommandRejectionReason.StaleDecision, Pass(SeatId.Host, 125, old).Result.RejectionReason);
        }

        [Test]
        public void InvalidCommands_DoNotRefreshDeadline_OrCountAsActivity()
        {
            ulong id = session.TurnTimer.For(SeatId.Host).Id;
            for (int i = 0; i < 5; i++)
                Assert.IsFalse(commands.ExecuteTimed(1, ++commandId, id, 119,
                    (s, actor) => s.Rules.TrySummonUnit(s.State, actor, 999)).Accepted);
            Assert.AreEqual(120, session.TurnTimer.For(SeatId.Host).Deadline);
            Assert.IsTrue(session.TurnTimer.Expire(session, SeatId.Host, 120.3).Accepted);
            Assert.AreEqual(1, session.State.Host.ConsecutiveTimeouts);
        }

        [Test]
        public void PauseResume_PreservesRemainingTimeAndDecision()
        {
            ulong id = session.TurnTimer.For(SeatId.Host).Id;
            session.Seats.MarkDisconnected(SeatId.Guest, 0);
            session.TurnTimer.Reconcile(session, 110);
            Assert.IsNull(session.TurnTimer.Expire(session, SeatId.Host, 135));
            session.Seats.TryReconnect("guest", 1000, out _);
            session.TurnTimer.Reconcile(session, 135);
            Assert.AreEqual(id, session.TurnTimer.For(SeatId.Host).Id);
            Assert.AreEqual(145, session.TurnTimer.For(SeatId.Host).Deadline);
            Assert.IsTrue(Pass(SeatId.Host, 145.2).Accepted);
        }

        [Test]
        public void PresentationWait_IsCapped_AndRepeatedReadinessCannotExtendIt()
        {
            Pass(SeatId.Host, 101);
            var window = session.TurnTimer.For(SeatId.Guest);
            Assert.AreEqual(129, window.Deadline);
            Assert.IsTrue(session.TurnTimer.MarkPresented(SeatId.Guest, window.Id, 115));
            Assert.AreEqual(129, window.Deadline, "late readiness cannot grant another 20s");
            Assert.IsFalse(session.TurnTimer.MarkPresented(SeatId.Guest, window.Id, 120));
            Assert.IsTrue(session.TurnTimer.Expire(session, SeatId.Guest, 129.3).Accepted);
        }

        [Test]
        public void ExpiryBeforeLateCommand_DoesNotApplyBoth()
        {
            ulong id = session.TurnTimer.For(SeatId.Host).Id;
            session.TurnTimer.Expire(session, SeatId.Host, 120.3);
            Assert.IsFalse(Pass(SeatId.Host, 120.3, id).Accepted);
            Assert.AreEqual(1, session.State.ConsecutivePasses);
            Assert.AreEqual(1, session.State.Host.ConsecutiveTimeouts);
        }

        [Test]
        public void Mulligan_HasIndependentClocks_TimeoutKeepsHandWithoutStrike()
        {
            var other = new MatchSession("mulligan");
            other.Start(new MatchState(), new MatchRulesEngine(new Dictionary<string, CardDefinition>()), false, 0);
            other.TurnTimer.Reconcile(other, 0);
            other.TurnTimer.MarkPresented(SeatId.Host, other.TurnTimer.For(SeatId.Host).Id, 0);
            other.TurnTimer.MarkPresented(SeatId.Guest, other.TurnTimer.For(SeatId.Guest).Id, 2);
            other.TurnTimer.Expire(other, SeatId.Host, 20.3);
            Assert.IsTrue(other.State.HostMulliganDone);
            Assert.IsFalse(other.State.GuestMulliganDone);
            Assert.AreEqual(22, other.TurnTimer.For(SeatId.Guest).Deadline);
            other.TurnTimer.Expire(other, SeatId.Guest, 22.3);
            Assert.AreEqual(1, other.State.RoundNumber);
            Assert.AreEqual(0, other.State.Host.ConsecutiveTimeouts);
            Assert.AreEqual(0, other.State.Guest.ConsecutiveTimeouts);
            Assert.AreEqual(SeatId.Host, session.State.ActiveSeat, "another match must be isolated");
        }

        private CardInstance SetUpTargeted(AbilityTrigger trigger)
        {
            var ability = new AbilityDefinition("timer_target", trigger,
                new[] { new EffectDefinition(EffectKind.Damage, EffectTarget.PrimarySelection, 1) },
                requiredTargets: new[] { new AbilityTargetRequirement(AbilityTargetSlot.Primary,
                    TargetRelation.Enemy, AbilityTargetZone.ActiveRoster) });
            var definitions = new Dictionary<string, CardDefinition>
            {
                ["targeted"] = new CardDefinition("targeted", "Targeted", 3, 2, 1, abilities: new[] { ability }),
                ["plain"] = new CardDefinition("plain", "Plain", 3, 2, 1),
            };
            var state = new MatchState { HostMulliganDone = true, GuestMulliganDone = true };
            state.BeginFirstRound();
            state.Host.InitializeMana(10);
            var source = new CardInstance(1, "targeted", SeatId.Host, CardZone.DrawHand, 3);
            state.Host.DrawHand.Add(source);
            state.Guest.Reserve.Add(new CardInstance(20, "plain", SeatId.Guest, CardZone.Reserve, 3));
            state.Guest.Reserve.Add(new CardInstance(10, "plain", SeatId.Guest, CardZone.Reserve, 3));
            session = new MatchSession("targeted-test");
            session.TryTakeSeat(1, SeatId.Host);
            session.TryTakeSeat(2, SeatId.Guest);
            session.Start(state, new MatchRulesEngine(definitions), false, 0);
            var registry = new MatchSessionRegistry(1);
            registry.TryAdd(session);
            registry.Bind(1, session);
            registry.Bind(2, session);
            commands = new MatchCommandService(registry);
            session.TurnTimer.Reconcile(session, 100);
            Ready(SeatId.Host, 100);
            return source;
        }

        [Test]
        public void OptionalSelection_OpenCancelRetry_DoesNotRefreshOrClearStrike()
        {
            CardInstance source = SetUpTargeted(AbilityTrigger.Play);
            session.TurnTimer.Expire(session, SeatId.Host, 120.3);
            Pass(SeatId.Guest, 121);
            Pass(SeatId.Guest, 122);
            Ready(SeatId.Host, 123);
            ulong id = session.TurnTimer.For(SeatId.Host).Id;
            double deadline = session.TurnTimer.For(SeatId.Host).Deadline;
            for (int i = 0; i < 2; i++)
            {
                Assert.IsTrue(commands.ExecuteTimed(1, ++commandId, id, 124,
                    (s, seat) => s.Rules.TrySummonUnit(s.State, seat, 1)).Accepted);
                ulong request = session.State.PendingSelection.RequestId;
                Assert.AreEqual(id, session.TurnTimer.For(SeatId.Host).Id);
                Assert.IsTrue(commands.ExecuteTimed(1, ++commandId, id, 125,
                    (s, seat) => s.Rules.TryCancelAbilitySelection(s.State, seat, request), false).Accepted);
            }
            Assert.AreEqual(deadline, session.TurnTimer.For(SeatId.Host).Deadline);
            Assert.AreEqual(1, session.State.Host.ConsecutiveTimeouts);
            Assert.AreEqual(CardZone.DrawHand, source.Zone);
            Assert.IsTrue(session.TurnTimer.Expire(session, SeatId.Host, deadline + 0.3).Accepted);
            Assert.AreEqual(MatchOutcome.GuestWon, session.State.Outcome);
        }

        [Test]
        public void FirstOptionalSelectionTimeout_CancelsUnpaidCardAndPasses()
        {
            CardInstance source = SetUpTargeted(AbilityTrigger.Play);
            ulong id = session.TurnTimer.For(SeatId.Host).Id;
            commands.ExecuteTimed(1, ++commandId, id, 119,
                (s, seat) => s.Rules.TrySummonUnit(s.State, seat, 1));
            Assert.IsTrue(session.TurnTimer.Expire(session, SeatId.Host, 120.3).Accepted);
            Assert.IsNull(session.State.PendingSelection);
            Assert.AreEqual(CardZone.DrawHand, source.Zone);
            Assert.AreEqual(10, session.State.Host.Mana);
            Assert.AreEqual(SeatId.Guest, session.State.ActiveSeat);
            Assert.AreEqual(1, session.State.Host.ConsecutiveTimeouts);
        }

        [Test]
        public void MandatorySelectionTimeout_PicksDeterministicLegalTarget_AndPreservesStrike()
        {
            SetUpTargeted(AbilityTrigger.Summon);
            ulong original = session.TurnTimer.For(SeatId.Host).Id;
            Assert.IsTrue(commands.ExecuteTimed(1, ++commandId, original, 119,
                (s, seat) => s.Rules.TrySummonUnit(s.State, seat, 1)).Accepted);
            Assert.IsFalse(session.State.PendingSelection.CanCancel);
            Assert.AreNotEqual(original, session.TurnTimer.For(SeatId.Host).Id);
            Ready(SeatId.Host, 120);
            Assert.IsTrue(session.TurnTimer.Expire(session, SeatId.Host, 140.3).Accepted);
            Assert.IsNull(session.State.PendingSelection);
            Assert.AreEqual(2, session.State.Guest.FindCardInReserve(10).CurrentHealth);
            Assert.AreEqual(3, session.State.Guest.FindCardInReserve(20).CurrentHealth);
            Assert.AreEqual(1, session.State.Host.ConsecutiveTimeouts);
        }

        [Test]
        public void BlockTimeout_ResolvesNoBlock_ThenSamePlayerGetsFreshPriority()
        {
            SetUpTargeted(AbilityTrigger.Play);
            session.State.Host.DrawHand.Clear();
            session.State.Host.Reserve.Add(new CardInstance(2, "plain", SeatId.Host, CardZone.Reserve, 3));
            ulong id = session.TurnTimer.For(SeatId.Host).Id;
            Assert.IsTrue(commands.ExecuteTimed(1, ++commandId, id, 101,
                (s, seat) => s.Rules.TryDeclareAttack(s.State, seat, new[] { 2UL, 0UL, 0UL })).Accepted);
            Ready(SeatId.Guest, 102);
            ulong blockId = session.TurnTimer.For(SeatId.Guest).Id;
            Assert.IsTrue(session.TurnTimer.Expire(session, SeatId.Guest, 122.3).Accepted);
            Assert.AreEqual(18, session.State.Guest.NexusHealth);
            Assert.AreEqual(MatchPhase.Priority, session.State.Phase);
            Assert.AreEqual(SeatId.Guest, session.State.ActiveSeat);
            Assert.AreNotEqual(blockId, session.TurnTimer.For(SeatId.Guest).Id);
            Assert.AreEqual(1, session.State.Guest.ConsecutiveTimeouts);
        }

        [Test]
        public void DuplicateCommit_CannotActInNextDecisionOfSameSeat()
        {
            Pass(SeatId.Host, 101);
            Assert.IsTrue(Pass(SeatId.Guest, 102).Accepted);
            ulong next = session.TurnTimer.For(SeatId.Guest).Id;
            Assert.AreEqual(CommandRejectionReason.DuplicateCommand,
                commands.ExecuteTimed(2, commandId, next, 103,
                    (s, seat) => s.Rules.TryPass(s.State, seat)).Result.RejectionReason);
            Assert.AreEqual(0, session.State.ConsecutivePasses);
        }

        [Test]
        public void SecondTimeoutInBlock_ForfeitsBeforeCombatDamage()
        {
            SetUpTargeted(AbilityTrigger.Play);
            session.State.Host.DrawHand.Clear();
            session.State.Host.Reserve.Add(new CardInstance(2, "plain", SeatId.Host, CardZone.Reserve, 3));
            session.State.Host.DrawHand.Add(new CardInstance(3, "plain", SeatId.Host, CardZone.DrawHand, 3));
            Assert.IsTrue(commands.ExecuteTimed(1, ++commandId, session.TurnTimer.For(SeatId.Host).Id, 101,
                (s, seat) => s.Rules.TrySummonUnit(s.State, seat, 3)).Accepted);
            Ready(SeatId.Guest, 102);
            session.TurnTimer.Expire(session, SeatId.Guest, 122.3); // first pass returns priority to Host
            ulong id = session.TurnTimer.For(SeatId.Host).Id;
            Assert.IsTrue(commands.ExecuteTimed(1, ++commandId, id, 124,
                (s, seat) => s.Rules.TryDeclareAttack(s.State, seat, new[] { 2UL, 0UL, 0UL })).Accepted);
            Ready(SeatId.Guest, 125);
            Assert.IsTrue(session.TurnTimer.Expire(session, SeatId.Guest, 145.3).Accepted);
            Assert.AreEqual(MatchOutcome.HostWon, session.State.Outcome);
            Assert.AreEqual(20, session.State.Guest.NexusHealth, "second timeout must forfeit before fallback combat");
        }
    }
}
