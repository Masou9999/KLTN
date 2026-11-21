using KLTN.Game.Domain.Ranking;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class RankProgressPathTests
    {
        [Test]
        public void GainInsideTier_IsOneSegmentWithoutBackdrop()
        {
            var path = RankProgressPath.Build(70, 100);

            Assert.AreEqual(1, path.Count);
            Assert.AreEqual(RankTier.Silver, path[0].Tier);
            Assert.AreEqual(70, path[0].FromPoints);
            Assert.AreEqual(100, path[0].ToPoints);
            Assert.AreEqual(150, path[0].Cap);
            Assert.AreEqual(RankTransition.None, path[0].TransitionAfter);
            Assert.AreEqual(0f, path[0].BackdropFill);
            Assert.Less(path[0].FromFill, path[0].ToFill);
        }

        [Test]
        public void GainAcrossThreshold_PromotesThenContinuesInNewTier()
        {
            var path = RankProgressPath.Build(140, 170);

            Assert.AreEqual(2, path.Count);
            Assert.AreEqual(RankTransition.Promotion, path[0].TransitionAfter);
            Assert.AreEqual(RankTier.Silver, path[0].Tier);
            Assert.AreEqual(150, path[0].ToPoints);
            Assert.AreEqual(1f, path[0].ToFill);
            Assert.AreEqual(RankTier.Gold, path[0].NextTier);
            Assert.AreEqual(RankTier.Gold, path[1].Tier);
            Assert.AreEqual(150, path[1].FromPoints);
            Assert.AreEqual(170, path[1].ToPoints);
            Assert.AreEqual(0f, path[1].FromFill);
        }

        [Test]
        public void GainExactlyToThreshold_EndsInNewTierAtZero()
        {
            var path = RankProgressPath.Build(130, 150);

            Assert.AreEqual(2, path.Count);
            Assert.AreEqual(RankTransition.Promotion, path[0].TransitionAfter);
            Assert.AreEqual(RankTier.Gold, path[1].Tier);
            Assert.AreEqual(150, path[1].ToPoints);
            Assert.AreEqual(0f, path[1].ToFill);
        }

        [Test]
        public void GainIntoTopTier_EndsInMaxTierSegment()
        {
            var path = RankProgressPath.Build(790, 820);

            Assert.AreEqual(2, path.Count);
            Assert.AreEqual(RankTransition.Promotion, path[0].TransitionAfter);
            Assert.IsTrue(path[1].IsMaxTier);
            Assert.AreEqual(800, path[1].FromPoints);
            Assert.AreEqual(820, path[1].ToPoints);
        }

        [Test]
        public void GainAtTopTier_OnlyCountsPoints()
        {
            var path = RankProgressPath.Build(1200, 1230);

            Assert.AreEqual(1, path.Count);
            Assert.IsTrue(path[0].IsMaxTier);
            Assert.AreEqual(1f, path[0].FromFill);
            Assert.AreEqual(1f, path[0].ToFill);
        }

        [Test]
        public void LossInsideTier_KeepsBackdropAtStartingFill()
        {
            var path = RankProgressPath.Build(120, 100);

            Assert.AreEqual(1, path.Count);
            Assert.IsTrue(path[0].IsLoss);
            Assert.AreEqual(path[0].FromFill, path[0].BackdropFill);
            Assert.Greater(path[0].FromFill, path[0].ToFill);
        }

        [Test]
        public void LossBelowFloor_DemotesThenStartsFullInLowerTier()
        {
            var path = RankProgressPath.Build(160, 140);

            Assert.AreEqual(2, path.Count);
            Assert.AreEqual(RankTier.Gold, path[0].Tier);
            Assert.AreEqual(150, path[0].ToPoints);
            Assert.AreEqual(RankTransition.Demotion, path[0].TransitionAfter);
            Assert.AreEqual(RankTier.Silver, path[0].NextTier);
            Assert.AreEqual(RankTier.Silver, path[1].Tier);
            Assert.AreEqual(150, path[1].FromPoints);
            Assert.AreEqual(1f, path[1].FromFill);
            Assert.AreEqual(1f, path[1].BackdropFill);
            Assert.AreEqual(140, path[1].ToPoints);
        }

        [Test]
        public void LossFromTopTier_DemotesToMaster()
        {
            var path = RankProgressPath.Build(810, 790);

            Assert.AreEqual(2, path.Count);
            Assert.IsTrue(path[0].IsMaxTier);
            Assert.AreEqual(RankTransition.Demotion, path[0].TransitionAfter);
            Assert.AreEqual(RankTier.Master, path[1].Tier);
        }

        [Test]
        public void LossAtBronze_ClampsAtZero()
        {
            var path = RankProgressPath.Build(10, -20);

            Assert.AreEqual(1, path.Count);
            Assert.AreEqual(RankTier.Bronze, path[0].Tier);
            Assert.AreEqual(0, path[0].ToPoints);
        }

        [Test]
        public void NoChange_IsSingleStillSegment()
        {
            var path = RankProgressPath.Build(300, 300);

            Assert.AreEqual(1, path.Count);
            Assert.AreEqual(path[0].FromPoints, path[0].ToPoints);
            Assert.IsFalse(path[0].IsLoss);
        }
    }
}
