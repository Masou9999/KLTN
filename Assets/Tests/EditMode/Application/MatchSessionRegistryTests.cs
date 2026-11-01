using System.Collections.Generic;
using KLTN.Game.Domain;
using NUnit.Framework;

namespace KLTN.Game.ApplicationLayer.Tests
{
    /// <summary>Several matches in one server process (FR-MM-10, SDS HLD 10.2).</summary>
    public sealed class MatchSessionRegistryTests
    {
        private const ulong HostA = 1;
        private const ulong GuestA = 2;
        private const ulong HostB = 3;
        private const ulong GuestB = 4;

        private MatchSessionRegistry registry;
        private MatchCommandService commands;
        private MatchSession matchA;
        private MatchSession matchB;

        [SetUp]
        public void SetUp()
        {
            registry = new MatchSessionRegistry(2);
            commands = new MatchCommandService(registry);
            matchA = StartedSession("match-a", HostA, GuestA, "player-1", "player-2");
            matchB = StartedSession("match-b", HostB, GuestB, "player-3", "player-4");
        }

        #region Isolation

        [Test]
        public void Command_RunsOnlyInTheSendersMatch()
        {
            var touched = new List<string>();

            MatchCommandOutcome outcome = commands.Execute(GuestB, 1, (session, seat) =>
            {
                touched.Add(session.MatchId + ":" + seat);
                return CommandResult.Success();
            });

            Assert.IsTrue(outcome.Accepted);
            Assert.AreSame(matchB, outcome.Session);
            CollectionAssert.AreEqual(new[] { "match-b:Guest" }, touched);
        }

        [Test]
        public void Surrender_InMatchA_DoesNotTouchMatchB()
        {
            MatchCommandOutcome outcome = commands.Execute(
                HostA,
                1,
                (session, seat) => session.Rules.TrySurrender(session.State, seat)
            );

            Assert.IsTrue(outcome.Accepted);
            Assert.IsTrue(matchA.IsFinished);
            Assert.AreEqual(MatchOutcome.GuestWon, matchA.State.Outcome);
            Assert.IsFalse(matchB.IsFinished);
            Assert.AreEqual(MatchPhase.Finished, matchA.State.Phase);
            Assert.AreNotEqual(MatchPhase.Finished, matchB.State.Phase);
        }

        [Test]
        public void CommandIds_AreTrackedPerMatchConnection()
        {
            Assert.IsTrue(commands.Execute(HostA, 5, Accept).Accepted);

            Assert.AreEqual(
                CommandRejectionReason.DuplicateCommand,
                commands.Execute(HostA, 5, Accept).Result.RejectionReason
            );
            Assert.IsTrue(commands.Execute(HostB, 1, Accept).Accepted, "match B has its own command IDs");
        }

        [Test]
        public void PauseInMatchA_DoesNotPauseMatchB()
        {
            matchA.RemoveConnection(GuestA, out SeatId seat);
            matchA.Seats.MarkDisconnected(seat, 1000);

            Assert.AreEqual(
                CommandRejectionReason.MatchPaused,
                commands.Execute(HostA, 1, Accept).Result.RejectionReason
            );
            Assert.IsTrue(commands.Execute(HostB, 1, Accept).Accepted);
            Assert.IsTrue(matchA.IsHoldingSeats);
            Assert.IsFalse(matchB.IsHoldingSeats);
        }

        [Test]
        public void EndingMatchA_KeepsMatchBRunning()
        {
            commands.Execute(HostA, 1, (session, seat) => session.Rules.TrySurrender(session.State, seat));
            matchA.Close();

            Assert.IsTrue(registry.Remove("match-a"));

            Assert.AreEqual(
                CommandRejectionReason.InvalidSeat,
                commands.Execute(GuestA, 2, Accept).Result.RejectionReason,
                "a removed match no longer routes its connections"
            );
            Assert.IsTrue(commands.Execute(GuestB, 1, Accept).Accepted);
            CollectionAssert.AreEqual(new[] { "match-b" }, registry.ActiveMatchIds());
        }

        [Test]
        public void Revisions_AreCountedPerMatch()
        {
            matchA.NextRevision();
            matchA.NextRevision();

            Assert.AreEqual(2UL, matchA.Revision);
            Assert.AreEqual(1UL, matchB.NextRevision());
        }

        #endregion

        #region Command Checks

        [Test]
        public void UnknownConnection_IsRejectedAsInvalidSeat()
        {
            MatchCommandOutcome outcome = commands.Execute(99, 1, Accept);

            Assert.IsNull(outcome.Session);
            Assert.AreEqual(CommandRejectionReason.InvalidSeat, outcome.Result.RejectionReason);
        }

        [Test]
        public void MatchNotStarted_IsRejected()
        {
            var smallRegistry = new MatchSessionRegistry(1);
            var session = new MatchSession("waiting");
            smallRegistry.TryAdd(session);
            smallRegistry.Bind(7, session);
            session.TryTakeFreeSeat(7, out _);

            MatchCommandOutcome outcome = new MatchCommandService(smallRegistry).Execute(7, 1, Accept);

            Assert.AreEqual(CommandRejectionReason.MatchNotReady, outcome.Result.RejectionReason);
        }

        #endregion

        #region Capacity

        [Test]
        public void Registry_RefusesMatchesBeyondCapacity()
        {
            Assert.IsTrue(registry.IsFull);
            Assert.IsFalse(registry.TryAdd(new MatchSession("match-c")));
        }

        [Test]
        public void CapacityOne_HoldsASingleMatch()
        {
            var single = new MatchSessionRegistry(1);

            Assert.IsTrue(single.TryAdd(new MatchSession("only")));
            Assert.IsFalse(single.TryAdd(new MatchSession("second")));
        }

        [Test]
        public void RemovedMatchId_IsNeverAddedAgain()
        {
            registry.Remove("match-a");

            Assert.IsFalse(registry.TryAdd(new MatchSession("match-a")));
            CollectionAssert.Contains(registry.KnownMatchIds, "match-a");
            Assert.IsTrue(registry.TryAdd(new MatchSession("match-c")));
        }

        [Test]
        public void ZeroCapacity_IsInvalid()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MatchSessionRegistry(0));
        }

        #endregion

        #region Routing

        [Test]
        public void NewPlayer_GoesToTheMatchTheyWereAllocatedTo()
        {
            var routing = new MatchSessionRegistry(3);
            var first = new MatchSession("m1", "mm-1", new[] { "p1", "p2" });
            var second = new MatchSession("m2", "mm-2", new[] { "p3", "p4" });
            routing.TryAdd(first);
            routing.TryAdd(second);

            Assert.AreSame(second, routing.FindForNewPlayer("p4", null));
            Assert.AreSame(first, routing.FindForNewPlayer("p1", "MM-1"), "matchmaker ID, case and dashes ignored");
            Assert.AreSame(second, routing.FindForNewPlayer("p3", "m2"));
            Assert.AreSame(first, routing.FindForNewPlayer("p1", "m2"), "membership wins over a wrong match name");
            Assert.IsNull(routing.FindForNewPlayer("stranger", null));
        }

        [Test]
        public void OpenMatches_TakeAnyPlayerInArrivalOrder()
        {
            var routing = new MatchSessionRegistry(2);
            var first = new MatchSession("open-1");
            var second = new MatchSession("open-2");
            routing.TryAdd(first);
            routing.TryAdd(second);

            Assert.AreSame(first, routing.FindForNewPlayer("a", null));
            first.TryAddMember("a");
            first.TryAddMember("b");

            Assert.AreSame(second, routing.FindForNewPlayer("c", null), "a full open match is skipped");
        }

        [Test]
        public void SealedMatch_TakesNoNewPlayer()
        {
            var routing = new MatchSessionRegistry(1);
            var session = new MatchSession("m1", null, new[] { "p1", "p2" });
            routing.TryAdd(session);
            session.Seal();

            Assert.IsNull(routing.FindForNewPlayer("p1", null));
        }

        [Test]
        public void Members_AreLimitedToTwoAllocatedPlayers()
        {
            var session = new MatchSession("m1", null, new[] { "p1", "p2" });

            Assert.IsFalse(session.TryAddMember("p3"));
            Assert.IsTrue(session.TryAddMember("p1"));
            Assert.IsFalse(session.TryAddMember("p1"));
            Assert.IsTrue(session.TryAddMember("p2"));

            session.RemoveMember("p2");
            Assert.IsTrue(session.TryAddMember("p2"), "a player whose verification failed frees the place");
        }

        [Test]
        public void HeldSeat_IsFoundOnlyInItsOwnMatch()
        {
            matchB.RemoveConnection(HostB, out SeatId seat);
            matchB.Seats.MarkDisconnected(seat, 1000);

            Assert.AreSame(matchB, registry.FindHeldSeat("player-3", 2000));
            Assert.IsNull(registry.FindHeldSeat("player-1", 2000), "player-1 is still connected in match A");
            Assert.IsNull(registry.FindHeldSeat("player-3", 1000 + SeatConnectionTracker.DefaultGracePeriodMilliseconds));
        }

        [Test]
        public void ActivePlayer_IsDetectedAcrossMatches()
        {
            Assert.IsTrue(registry.IsPlayerActive("player-4"));
            Assert.IsFalse(registry.IsPlayerActive("player-9"));
        }

        #endregion

        #region Helpers

        private static CommandResult Accept(MatchSession session, SeatId seat)
        {
            return CommandResult.Success();
        }

        private MatchSession StartedSession(string matchId, ulong host, ulong guest, string hostPlayer, string guestPlayer)
        {
            var session = new MatchSession(matchId, null, new[] { hostPlayer, guestPlayer });
            Assert.IsTrue(registry.TryAdd(session));

            registry.Bind(host, session);
            registry.Bind(guest, session);
            session.TryTakeFreeSeat(host, out SeatId hostSeat);
            session.TryTakeFreeSeat(guest, out SeatId guestSeat);
            session.Seats.Bind(hostSeat, hostPlayer);
            session.Seats.Bind(guestSeat, guestPlayer);
            session.Seal();

            var state = new MatchState(SeatId.Host);
            state.HostMulliganDone = true;
            state.GuestMulliganDone = true;
            state.BeginFirstRound();
            session.Start(state, new MatchRulesEngine(new Dictionary<string, CardDefinition>()), true, 0);
            return session;
        }

        #endregion
    }
}
