using System.Collections.Generic;
using KLTN.Game.Domain.Ranking;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class RankPolicyTests
    {
        [TestCase(0, RankTier.Bronze)]
        [TestCase(59, RankTier.Bronze)]
        [TestCase(60, RankTier.Silver)]
        [TestCase(149, RankTier.Silver)]
        [TestCase(150, RankTier.Gold)]
        [TestCase(270, RankTier.Platinum)]
        [TestCase(420, RankTier.Diamond)]
        [TestCase(599, RankTier.Diamond)]
        [TestCase(600, RankTier.Master)]
        [TestCase(799, RankTier.Master)]
        [TestCase(800, RankTier.Challenger)]
        [TestCase(5000, RankTier.Challenger)]
        public void TierFor_UsesInclusiveThresholds(int points, RankTier expected)
        {
            Assert.AreEqual(expected, RankPolicy.TierFor(points));
        }

        [Test]
        public void Thresholds_AreStrictlyIncreasing()
        {
            for (int tier = 1; tier < RankPolicy.TierCount; tier++)
            {
                Assert.Greater(RankPolicy.MinPoints((RankTier)tier), RankPolicy.MinPoints((RankTier)(tier - 1)));
            }
        }

        [Test]
        public void PointsToNextTier_CountsFromCurrentPoints()
        {
            Assert.AreEqual(60, RankPolicy.PointsToNextTier(0));
            Assert.AreEqual(1, RankPolicy.PointsToNextTier(149));
            Assert.AreEqual(0, RankPolicy.PointsToNextTier(900));
        }

        [Test]
        public void Matchmaking_AllowsOnlyAdjacentTiers()
        {
            Assert.IsTrue(RankMatchmakingPolicy.CanMeet(RankTier.Gold, RankTier.Gold));
            Assert.IsTrue(RankMatchmakingPolicy.CanMeet(RankTier.Gold, RankTier.Platinum));
            Assert.IsTrue(RankMatchmakingPolicy.CanMeet(RankTier.Gold, RankTier.Silver));
            Assert.IsFalse(RankMatchmakingPolicy.CanMeet(RankTier.Bronze, RankTier.Gold));
            Assert.IsFalse(RankMatchmakingPolicy.CanMeet(RankTier.Challenger, RankTier.Diamond));
        }

        [TestCase(0, "user000000")]
        [TestCase(42, "user000042")]
        [TestCase(999999, "user999999")]
        public void Username_FormatsSixDigits(int number, string expected)
        {
            Assert.AreEqual(expected, UsernamePolicy.Format(number));
            Assert.IsTrue(UsernamePolicy.IsValid(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("User123456")]
        [TestCase("1alex")]
        public void Username_RejectsWrongFormat(string name)
        {
            Assert.IsFalse(UsernamePolicy.IsValid(name));
        }

        [TestCase("John Doe 01", null)]
        [TestCase("Abcd123456789012", null)]
        [TestCase("Abcd1234567890123", "invalid_length")]
        [TestCase("abc", "invalid_length")]
        [TestCase("Admin", "reserved_name")]
        [TestCase("SUPPORT", "reserved_name")]
        [TestCase("1Alex", "invalid_name")]
        [TestCase("Alex_01", "special_characters")]
        [TestCase("Alex@01", "special_characters")]
        [TestCase("Alex\t01", "special_characters")]
        public void Username_ValidatesNewChoices(string name, string error)
        {
            Assert.AreEqual(error, UsernamePolicy.ValidateChoice(name));
        }

        [Test]
        public void Username_PreservesDisplayAndLegacyNames()
        {
            Assert.AreEqual("John Doe 01", UsernamePolicy.DisplayName(" John  Doe 01 "));
            Assert.AreEqual("john doe 01", UsernamePolicy.Normalize(" JOHN  Doe 01 "));
            Assert.IsTrue(UsernamePolicy.IsValid("legacy_name_12345678"));
            Assert.IsTrue(UsernamePolicy.IsValid("John Doe 01"));
        }

        [Test]
        public void Standing_WinRateCountsDrawsAsPlayed()
        {
            var standing = new SeasonStanding("uid", "user000001", 90, 3, 1, 1, 5);

            Assert.AreEqual(0.6, standing.WinRate, 1e-9);
            Assert.AreEqual(6000, standing.WinRateBasisPoints);
            Assert.AreEqual(RankTier.Silver, standing.Tier);
        }

        [Test]
        public void Standing_NewPlayerIsBronzeWithZeroWinRate()
        {
            var standing = new SeasonStanding("uid", "user000002", 0, 0, 0, 0, 0);

            Assert.AreEqual(RankTier.Bronze, standing.Tier);
            Assert.AreEqual(0d, standing.WinRate);
            Assert.AreEqual(0, standing.WinRateBasisPoints);
        }

        [Test]
        public void Standing_WinRateRoundsDownLikeRules()
        {
            Assert.AreEqual(3333, SeasonStanding.WinRateOf(1, 3));
            Assert.AreEqual(6666, SeasonStanding.WinRateOf(2, 3));
            Assert.AreEqual(10000, SeasonStanding.WinRateOf(7, 7));
        }

        [Test]
        public void LeaderboardOrder_PointsThenWinRateThenUsername()
        {
            var rows = new List<SeasonStanding>
            {
                new SeasonStanding("a", "user000003", 30, 1, 1, 0, 2),
                new SeasonStanding("b", "user000002", 30, 1, 1, 0, 2),
                new SeasonStanding("c", "user000009", 30, 2, 0, 0, 2),
                new SeasonStanding("d", "user000001", 0, 0, 0, 0, 0),
                new SeasonStanding("e", "user000005", 60, 2, 0, 0, 2),
            };

            rows.Sort(SeasonStanding.LeaderboardOrder);

            CollectionAssert.AreEqual(
                new[] { "user000005", "user000009", "user000002", "user000003", "user000001" },
                rows.ConvertAll(row => row.Username)
            );
        }
    }
}
