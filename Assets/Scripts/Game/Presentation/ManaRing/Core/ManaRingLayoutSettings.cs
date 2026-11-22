using System;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Tunable arc parameters for the mana ring. Defaults were measured from the
    /// reference mock-up on the action-frame art; radii are fractions of the frame width.
    /// </summary>
    [Serializable]
    public sealed class ManaRingLayoutSettings
    {
        #region Configuration

        public const int DefaultSlotsPerArc = 10;

        /// <summary>Slots per arc; the ring always holds two arcs of this size.</summary>
        public int slotsPerArc = DefaultSlotsPerArc;

        /// <summary>Direction of the gap between the arcs (180 = 9 o'clock).</summary>
        public float gapCenterAngle = 180f;

        /// <summary>Angular width of the gap between the upper and lower arcs.</summary>
        public float gapAngle = 10f;

        /// <summary>Angular length of each arc; slot width is derived from it.</summary>
        public float arcSpanAngle = 71f;

        /// <summary>Inner slot radius; hidden under the frame border.</summary>
        public float innerRadius = 0.330f;

        /// <summary>Outer slot radius; how far slots protrude past the frame.</summary>
        public float outerRadius = 0.459f;

        /// <summary>
        /// Uniform scale of each slot around its own centre. 1 keeps neighbours touching.
        /// </summary>
        public float slotSizeScale = 1f;

        /// <summary>Rotates the whole ring (both arcs) by this many degrees.</summary>
        public float rotationOffset;

        /// <summary>Fill order inside each arc.</summary>
        public ManaFillDirection fillDirection = ManaFillDirection.FromOrnament;

        #endregion

        #region Queries

        public int SafeSlotsPerArc => Math.Max(1, slotsPerArc);

        #endregion
    }
}
