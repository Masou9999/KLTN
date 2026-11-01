using KLTN.Game.Domain;
using NUnit.Framework;

namespace KLTN.Game.ApplicationLayer.Tests
{
    public sealed class SeatConnectionTrackerTests
    {
        private const long Grace = 30000;
        private const string HostPlayer = "player-host";
        private const string GuestPlayer = "player-guest";

        private SeatConnectionTracker tracker;

        [SetUp]
        public void SetUp()
        {
            tracker = new SeatConnectionTracker(Grace);
            tracker.Bind(SeatId.Host, HostPlayer);
            tracker.Bind(SeatId.Guest, GuestPlayer);
        }

        [Test]
        public void BothConnected_IsNotPaused()
        {
            Assert.IsFalse(tracker.IsPaused);
            Assert.IsFalse(tracker.IsHoldingSeats);
            Assert.AreEqual(GraceExpiryKind.None, tracker.Evaluate(1000).Kind);
        }

        [Test]
        public void Disconnect_HoldsSeatAndPausesUntilDeadline()
        {
            Assert.IsTrue(tracker.MarkDisconnected(SeatId.Guest, 1000));

            Assert.IsTrue(tracker.IsPaused);
            Assert.IsTrue(tracker.IsHoldingSeats);
            Assert.AreEqual(SeatConnectionState.Disconnected, tracker.StateOf(SeatId.Guest));
            Assert.AreEqual(1000 + Grace, tracker.DeadlineOf(SeatId.Guest));
            Assert.AreEqual(GraceExpiryKind.None, tracker.Evaluate(1000 + Grace - 1).Kind);
        }

        [Test]
        public void ReconnectInTime_RestoresSameSeatAndResumes()
        {
            tracker.MarkDisconnected(SeatId.Guest, 1000);

            Assert.IsTrue(tracker.CanReconnect(GuestPlayer, 20000));
            ReconnectResult result = tracker.TryReconnect(GuestPlayer, 20000, out SeatId seat);

            Assert.AreEqual(ReconnectResult.Reconnected, result);
            Assert.AreEqual(SeatId.Guest, seat);
            Assert.IsFalse(tracker.IsPaused);
            Assert.AreEqual(0, tracker.DeadlineOf(SeatId.Guest));
            Assert.AreEqual(GraceExpiryKind.None, tracker.Evaluate(1000 + Grace + 1).Kind);
        }

        [Test]
        public void SecondDropAfterReconnect_GetsAFreshGracePeriod()
        {
            tracker.MarkDisconnected(SeatId.Host, 0);
            tracker.TryReconnect(HostPlayer, 25000, out _);
            tracker.MarkDisconnected(SeatId.Host, 26000);

            Assert.AreEqual(26000 + Grace, tracker.DeadlineOf(SeatId.Host));
            Assert.AreEqual(GraceExpiryKind.None, tracker.Evaluate(40000).Kind);
        }

        [Test]
        public void OneSeatExpires_ThatSeatForfeits()
        {
            tracker.MarkDisconnected(SeatId.Host, 1000);

            GraceExpiryDecision decision = tracker.Evaluate(1000 + Grace);

            Assert.AreEqual(GraceExpiryKind.Forfeit, decision.Kind);
            Assert.AreEqual(SeatId.Host, decision.ForfeitingSeat);
            Assert.IsTrue(tracker.IsResolved);
            Assert.IsFalse(tracker.IsPaused);
            Assert.IsFalse(tracker.IsHoldingSeats);
        }

        [Test]
        public void Decision_IsReturnedOnlyOnce()
        {
            tracker.MarkDisconnected(SeatId.Guest, 0);
            tracker.Evaluate(Grace);

            Assert.AreEqual(GraceExpiryKind.None, tracker.Evaluate(Grace * 2).Kind);
        }

        [Test]
        public void ReconnectAfterDeadline_IsRefused()
        {
            tracker.MarkDisconnected(SeatId.Guest, 0);

            Assert.IsFalse(tracker.CanReconnect(GuestPlayer, Grace));
            Assert.AreEqual(ReconnectResult.Expired, tracker.TryReconnect(GuestPlayer, Grace, out _));
            Assert.AreEqual(SeatConnectionState.Disconnected, tracker.StateOf(SeatId.Guest));
        }

        [Test]
        public void ReconnectAfterForfeit_IsRefused()
        {
            tracker.MarkDisconnected(SeatId.Guest, 0);
            tracker.Evaluate(Grace);

            Assert.AreEqual(ReconnectResult.Expired, tracker.TryReconnect(GuestPlayer, Grace + 1, out _));
        }

        [Test]
        public void BothSeatsAbsentPastDeadline_IsDraw()
        {
            tracker.MarkDisconnected(SeatId.Host, 0);
            tracker.MarkDisconnected(SeatId.Guest, 5000);

            // The host expired first but the guest may still come back.
            Assert.AreEqual(GraceExpiryKind.None, tracker.Evaluate(Grace).Kind);
            Assert.AreEqual(SeatConnectionState.Abandoned, tracker.StateOf(SeatId.Host));
            Assert.IsTrue(tracker.IsHoldingSeats);

            GraceExpiryDecision decision = tracker.Evaluate(5000 + Grace);

            Assert.AreEqual(GraceExpiryKind.Draw, decision.Kind);
            Assert.IsFalse(tracker.IsHoldingSeats);
        }

        [Test]
        public void BothAbsent_ReturningPlayerWinsAgainstExpiredSeat()
        {
            tracker.MarkDisconnected(SeatId.Host, 0);
            tracker.MarkDisconnected(SeatId.Guest, 5000);
            tracker.Evaluate(Grace);

            Assert.AreEqual(ReconnectResult.Reconnected, tracker.TryReconnect(GuestPlayer, Grace + 1000, out _));

            GraceExpiryDecision decision = tracker.Evaluate(Grace + 1000);

            Assert.AreEqual(GraceExpiryKind.Forfeit, decision.Kind);
            Assert.AreEqual(SeatId.Host, decision.ForfeitingSeat);
        }

        [Test]
        public void DifferentPlayerId_CannotTakeHeldSeat()
        {
            tracker.MarkDisconnected(SeatId.Guest, 0);

            Assert.IsFalse(tracker.CanReconnect("intruder", 1000));
            Assert.AreEqual(ReconnectResult.UnknownPlayer, tracker.TryReconnect("intruder", 1000, out _));
            Assert.AreEqual(SeatConnectionState.Disconnected, tracker.StateOf(SeatId.Guest));
        }

        [Test]
        public void ConnectedPlayer_CannotReconnectTwice()
        {
            Assert.IsFalse(tracker.CanReconnect(HostPlayer, 0));
            Assert.AreEqual(ReconnectResult.AlreadyConnected, tracker.TryReconnect(HostPlayer, 0, out _));
        }

        [Test]
        public void Resolved_IgnoresLaterDisconnects()
        {
            tracker.Resolve();

            Assert.IsFalse(tracker.MarkDisconnected(SeatId.Host, 0));
            Assert.IsFalse(tracker.IsPaused);
            Assert.IsFalse(tracker.IsHoldingSeats);
        }

        [Test]
        public void UnboundSeat_DoesNotPause()
        {
            var lonely = new SeatConnectionTracker(Grace);
            lonely.Bind(SeatId.Host, HostPlayer);
            lonely.MarkDisconnected(SeatId.Host, 0);

            Assert.IsFalse(lonely.IsPaused);
            Assert.IsTrue(lonely.IsHoldingSeats);
            Assert.IsFalse(lonely.IsBound(SeatId.Guest));
        }

        [Test]
        public void InvalidGracePeriod_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SeatConnectionTracker(0));
        }
    }
}
