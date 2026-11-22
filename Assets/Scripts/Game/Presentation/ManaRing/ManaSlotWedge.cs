using System;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Describes the wedge drawn inside a mana-slot sprite, in normalized sprite space
    /// (0..1, origin bottom-left). Filled, reserved and empty sprites share one wedge,
    /// so swapping sprites never changes a slot's geometry.
    /// </summary>
    [Serializable]
    public struct ManaSlotWedge
    {
        #region Configuration

        [Tooltip("Point where the wedge's slanted sides meet (below the sprite).")]
        public Vector2 apex;

        [Tooltip("Half of the angle between the rim's slanted sides, in degrees.")]
        public float halfAngle;

        [Tooltip("Distance from the apex to the inner (bottom) rim.")]
        public float rimInnerRadius;

        [Tooltip("Distance from the apex to the outer (top) rim.")]
        public float rimOuterRadius;

        [Tooltip("Extra glow kept past each slanted side, as a fraction of halfAngle.")]
        public float sideGlow;

        [Tooltip("Glow kept inside the inner rim, in normalized sprite units.")]
        public float innerGlow;

        [Tooltip("Glow kept outside the outer rim, in normalized sprite units.")]
        public float outerGlow;

        #endregion

        #region Queries

        /// <summary>
        /// Rim measured on the 1254 px Fill/Empty art: apex (627, 1586) top-left,
        /// rim radii 632..1308 px, sides at +/-19.3 degrees.
        /// </summary>
        public static ManaSlotWedge Default => new ManaSlotWedge
        {
            apex = new Vector2(0.5f, -0.26475f),
            halfAngle = 19.3f,
            rimInnerRadius = 0.50399f,
            rimOuterRadius = 1.04306f,
            sideGlow = 0.15f,
            innerGlow = 0.02f,
            outerGlow = 0.035f,
        };

        public bool IsValid => halfAngle > 0.01f && rimOuterRadius > rimInnerRadius;

        #endregion
    }
}
