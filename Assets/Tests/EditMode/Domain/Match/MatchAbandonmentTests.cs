using System.Collections.Generic;
using KLTN.Game.Domain;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class MatchAbandonmentTests
    {
        private MatchRulesEngine engine;

        [SetUp]
        public void SetUp()
        {
            engine = new MatchRulesEngine(new Dictionary<string, CardDefinition>());
        }

        [Test]
        public void ForfeitAbandonedSeat_FollowsSurrenderRule()
        {
            MatchState state = CreateState();

            CommandResult result = engine.TryForfeitAbandonedSeat(state, SeatId.Guest);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(MatchOutcome.HostWon, state.Outcome);
            Assert.AreEqual(SeatId.Guest, state.SurrenderedSeat);
            Assert.AreEqual(MatchPhase.Finished, state.Phase);
        }

        [Test]
        public void DrawAbandonedMatch_EndsAsDrawWithoutSurrender()
        {
            MatchState state = CreateState();

            CommandResult result = engine.TryDrawAbandonedMatch(state);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(MatchOutcome.Draw, state.Outcome);
            Assert.IsNull(state.SurrenderedSeat);
            Assert.AreEqual(MatchPhase.Finished, state.Phase);
        }

        [Test]
        public void FinishedMatch_CannotBeEndedAgain()
        {
            MatchState state = CreateState();
            engine.TrySurrender(state, SeatId.Host);

            Assert.AreEqual(
                CommandRejectionReason.MatchFinished,
                engine.TryForfeitAbandonedSeat(state, SeatId.Guest).RejectionReason
            );
            Assert.AreEqual(
                CommandRejectionReason.MatchFinished,
                engine.TryDrawAbandonedMatch(state).RejectionReason
            );
            Assert.AreEqual(MatchOutcome.GuestWon, state.Outcome);
        }

        [Test]
        public void Rejoin_HeldSeatIsAccepted()
        {
            Assert.AreEqual(
                AdmissionRejection.None,
                MatchAdmissionPolicy.PreCheckRejoin("player-a", "token", true, new string[0])
            );
        }

        [Test]
        public void Rejoin_WithoutHeldSeatIsRefused()
        {
            Assert.AreEqual(
                AdmissionRejection.CannotRejoin,
                MatchAdmissionPolicy.PreCheckRejoin("player-a", "token", false, null)
            );
        }

        [Test]
        public void Rejoin_WhileAlreadyVerifyingIsRefused()
        {
            Assert.AreEqual(
                AdmissionRejection.DuplicatePlayer,
                MatchAdmissionPolicy.PreCheckRejoin("player-a", "token", true, new[] { "player-a" })
            );
        }

        [Test]
        public void Rejoin_MissingTokenIsRefused()
        {
            Assert.AreEqual(
                AdmissionRejection.MissingPayload,
                MatchAdmissionPolicy.PreCheckRejoin("player-a", "", true, null)
            );
        }

        private static MatchState CreateState()
        {
            var state = new MatchState(SeatId.Host);
            state.HostMulliganDone = true;
            state.GuestMulliganDone = true;
            state.BeginFirstRound();
            return state;
        }
    }
}
