using System;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Which half of the mana ring a slot group occupies. The upper arc shows the
    /// opponent's mana and the lower arc shows the local player's mana.
    /// </summary>
    public enum ManaArcSide
    {
        Upper = 0,
        Lower = 1,
    }

    /// <summary>
    /// Where the first mana of an arc appears. <see cref="FromOrnament"/> starts at the
    /// slot tucked under the frame ornament (right) and grows toward the 9 o'clock gap.
    /// </summary>
    public enum ManaFillDirection
    {
        FromOrnament = 0,
        FromGap = 1,
    }

    /// <summary>Visual state of a single mana slot.</summary>
    public enum ManaSlotState
    {
        Empty = 0,
        Reserved = 1,
        Filled = 2,
    }

    /// <summary>
    /// Annular-sector geometry of one slot. Angles are degrees, counter-clockwise from
    /// +X with +Y up; radii are fractions of the frame width.
    /// </summary>
    public readonly struct ManaSlotGeometry : IEquatable<ManaSlotGeometry>
    {
        public ManaSlotGeometry(
            float centerAngle,
            float halfAngle,
            float innerRadius,
            float outerRadius
        )
        {
            CenterAngle = centerAngle;
            HalfAngle = halfAngle;
            InnerRadius = innerRadius;
            OuterRadius = outerRadius;
        }

        public float CenterAngle { get; }
        public float HalfAngle { get; }
        public float InnerRadius { get; }
        public float OuterRadius { get; }
        public float StartAngle => CenterAngle - HalfAngle;
        public float EndAngle => CenterAngle + HalfAngle;

        public bool Equals(ManaSlotGeometry other)
        {
            return CenterAngle.Equals(other.CenterAngle)
                && HalfAngle.Equals(other.HalfAngle)
                && InnerRadius.Equals(other.InnerRadius)
                && OuterRadius.Equals(other.OuterRadius);
        }

        public override bool Equals(object obj)
        {
            return obj is ManaSlotGeometry other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CenterAngle.GetHashCode();
                hash = (hash * 397) ^ HalfAngle.GetHashCode();
                hash = (hash * 397) ^ InnerRadius.GetHashCode();
                return (hash * 397) ^ OuterRadius.GetHashCode();
            }
        }
    }
}
