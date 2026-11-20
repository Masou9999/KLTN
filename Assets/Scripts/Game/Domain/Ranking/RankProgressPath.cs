using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain.Ranking
{
    /// <summary>What happens when a progress segment reaches its end.</summary>
    public enum RankTransition
    {
        None = 0,
        Promotion = 1,
        Demotion = 2,
    }

    /// <summary>
    /// One continuous run of the rank bar inside a single tier. Points move from
    /// <see cref="FromPoints"/> to <see cref="ToPoints"/>; the tier changes only after
    /// the segment ends, as described by <see cref="TransitionAfter"/>.
    /// </summary>
    public sealed class RankProgressSegment
    {
        public RankProgressSegment(
            RankTier tier, int fromPoints, int toPoints, RankTransition transitionAfter)
        {
            Tier = tier;
            FromPoints = fromPoints;
            ToPoints = toPoints;
            TransitionAfter = transitionAfter;
        }

        public RankTier Tier { get; }
        public int FromPoints { get; }
        public int ToPoints { get; }
        public RankTransition TransitionAfter { get; }

        /// <summary>True at the highest tier, which has no bar and no next threshold.</summary>
        public bool IsMaxTier => Tier == RankTier.Challenger;

        public bool IsLoss => ToPoints < FromPoints;

        /// <summary>Points needed for the next tier; equals the tier floor at the top tier.</summary>
        public int Cap => IsMaxTier
            ? RankPolicy.MinPoints(RankTier.Challenger)
            : RankPolicy.MinPoints((RankTier)((int)Tier + 1));

        public RankTier NextTier => TransitionAfter switch
        {
            RankTransition.Promotion => (RankTier)((int)Tier + 1),
            RankTransition.Demotion => (RankTier)((int)Tier - 1),
            _ => Tier,
        };

        public float FromFill => RankProgressPath.Fill(Tier, FromPoints);

        public float ToFill => RankProgressPath.Fill(Tier, ToPoints);

        /// <summary>Fill kept behind a losing bar to show how much was lost; 0 on a gain.</summary>
        public float BackdropFill => IsLoss ? FromFill : 0f;
    }

    /// <summary>
    /// Splits a season point change into per-tier bar segments for the result screen.
    /// Gains stop at each threshold for a promotion; losses stop at each tier floor for
    /// a demotion. The final segment always ends at the saved points.
    /// </summary>
    public static class RankProgressPath
    {
        #region Public API

        public static IReadOnlyList<RankProgressSegment> Build(int pointsBefore, int pointsAfter)
        {
            int before = Math.Max(0, pointsBefore);
            int after = Math.Max(0, pointsAfter);
            var segments = new List<RankProgressSegment>();
            RankTier tier = RankPolicy.TierFor(before);
            int current = before;

            if (after >= before)
            {
                while (tier != RankTier.Challenger)
                {
                    int cap = RankPolicy.MinPoints((RankTier)((int)tier + 1));
                    if (after < cap) break;
                    segments.Add(new RankProgressSegment(
                        tier, current, cap, RankTransition.Promotion));
                    current = cap;
                    tier = (RankTier)((int)tier + 1);
                }
            }
            else
            {
                while (tier != RankTier.Bronze)
                {
                    int floor = RankPolicy.MinPoints(tier);
                    if (after >= floor) break;
                    segments.Add(new RankProgressSegment(
                        tier, current, floor, RankTransition.Demotion));
                    current = floor;
                    tier = (RankTier)((int)tier - 1);
                }
            }

            segments.Add(new RankProgressSegment(tier, current, after, RankTransition.None));
            return segments;
        }

        /// <summary>Bar fill (0–1) for points inside a tier; always full at the top tier.</summary>
        public static float Fill(RankTier tier, int points)
        {
            if (tier == RankTier.Challenger) return 1f;
            int min = RankPolicy.MinPoints(tier);
            int cap = RankPolicy.MinPoints((RankTier)((int)tier + 1));
            float value = (points - min) / (float)(cap - min);
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        #endregion
    }
}
