using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class MatchTimeoutTests
    {
        private MatchState state;
        private MatchRulesEngine engine;

        [SetUp]
        public void SetUp()
        {
            state = new MatchState { HostMulliganDone = true, GuestMulliganDone = true };
            state.BeginFirstRound();
            engine = new MatchRulesEngine(new Dictionary<string, CardDefinition>());
        }

        [Test]
        public void TimeoutValidation_DoesNotMutateOnInvalidActorPhaseOrFinishedMatch()
        {
            Assert.AreEqual(CommandRejectionReason.MatchNotReady, engine.TryTimeout(null, SeatId.Host).RejectionReason);
            Assert.AreEqual(CommandRejectionReason.InvalidSeat, engine.TryTimeout(state, (SeatId)99).RejectionReason);
            Assert.AreEqual(CommandRejectionReason.NotYourTurn, engine.TryTimeout(state, SeatId.Guest).RejectionReason);
            state.Phase = MatchPhase.RoundStart;
            Assert.AreEqual(CommandRejectionReason.InvalidPhase, engine.TryTimeout(state, SeatId.Host).RejectionReason);
            engine.TrySurrender(state, SeatId.Host);
            Assert.AreEqual(CommandRejectionReason.MatchFinished, engine.TryTimeout(state, SeatId.Host).RejectionReason);
            Assert.AreEqual(0, state.Host.ConsecutiveTimeouts);
        }

        [Test]
        public void LateMulligan_CannotMutateStartedMatch()
        {
            Assert.AreEqual(CommandRejectionReason.InvalidPhase,
                engine.TryMulligan(state, SeatId.Host, Array.Empty<ulong>()).RejectionReason);
            Assert.AreEqual(1, state.RoundNumber);
        }

        [Test]
        public void MulliganTimeout_AfterOpponentConfirmed_CompletesWithoutStartingRound()
        {
            var mulligan = new MatchState();
            Assert.IsTrue(engine.TryMulligan(mulligan, SeatId.Host, Array.Empty<ulong>()).Accepted);

            Assert.IsTrue(engine.TryTimeout(mulligan, SeatId.Guest).Accepted);

            Assert.IsTrue(mulligan.IsMulliganPhaseComplete);
            Assert.AreEqual(0, mulligan.RoundNumber);
            Assert.AreEqual(MatchPhase.Mulligan, mulligan.Phase);
            Assert.AreEqual(0, mulligan.Guest.ConsecutiveTimeouts);
        }

        [Test]
        public void MulliganTimeout_BothSeats_LeavesRoundStartToAdapter()
        {
            var mulligan = new MatchState();

            Assert.IsTrue(engine.TryTimeout(mulligan, SeatId.Host).Accepted);
            Assert.IsTrue(engine.TryTimeout(mulligan, SeatId.Guest).Accepted);

            Assert.IsTrue(mulligan.IsMulliganPhaseComplete);
            Assert.AreEqual(0, mulligan.RoundNumber);

            mulligan.BeginFirstRound();
            Assert.AreEqual(1, mulligan.RoundNumber);
        }
    }
}
