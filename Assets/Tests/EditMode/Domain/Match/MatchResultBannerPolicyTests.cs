using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class MatchResultBannerPolicyTests
    {
        [TestCase(MatchOutcome.HostWon, SeatId.Host, MatchResultBanner.Victory)]
        [TestCase(MatchOutcome.HostWon, SeatId.Guest, MatchResultBanner.Defeat)]
        [TestCase(MatchOutcome.GuestWon, SeatId.Guest, MatchResultBanner.Victory)]
        [TestCase(MatchOutcome.GuestWon, SeatId.Host, MatchResultBanner.Defeat)]
        [TestCase(MatchOutcome.Draw, SeatId.Host, MatchResultBanner.Draw)]
        [TestCase(MatchOutcome.Draw, SeatId.Guest, MatchResultBanner.Draw)]
        [TestCase(MatchOutcome.Running, SeatId.Host, MatchResultBanner.None)]
        public void Banner_MatchesOutcomeForViewer(MatchOutcome outcome, SeatId viewer, MatchResultBanner expected)
        {
            Assert.AreEqual(expected, MatchResultBannerPolicy.For(outcome, viewer));
        }

        [Test]
        public void WireNames_MatchReportResultContract()
        {
            Assert.AreEqual("HostWon", MatchResultBannerPolicy.WireName(MatchOutcome.HostWon));
            Assert.AreEqual("GuestWon", MatchResultBannerPolicy.WireName(MatchOutcome.GuestWon));
            Assert.AreEqual("Draw", MatchResultBannerPolicy.WireName(MatchOutcome.Draw));
            Assert.IsNull(MatchResultBannerPolicy.WireName(MatchOutcome.Running));
        }
    }
}
