using System;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Computes the procedural arc layout of the mana ring. Each arc is split into equal
    /// sectors so adjacent slots share an edge, and the two arcs mirror each other
    /// around the gap direction.
    /// </summary>
    public static class ManaRingLayout
    {
        #region Public API

        /// <summary>
        /// Builds the slots of one arc ordered by fill index: element 0 receives the
        /// first mana. The upper arc lies just before the gap (counter-clockwise, above
        /// it) and the lower arc just after it.
        /// </summary>
        public static ManaSlotGeometry[] Build(
            ManaRingLayoutSettings settings,
            ManaArcSide side
        )
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            int count = settings.SafeSlotsPerArc;
            var slots = new ManaSlotGeometry[count];
            Fill(settings, side, slots);
            return slots;
        }

        /// <summary>Writes the arc into <paramref name="buffer"/>.</summary>
        public static void Fill(
            ManaRingLayoutSettings settings,
            ManaArcSide side,
            ManaSlotGeometry[] buffer
        )
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            int count = buffer.Length;

            if (count == 0)
            {
                return;
            }

            float span = Math.Max(0.01f, settings.arcSpanAngle);
            float halfGap = Math.Max(0f, settings.gapAngle) * 0.5f;
            float gapCenter = settings.gapCenterAngle + settings.rotationOffset;
            float step = span / count;
            float scale = Math.Max(0.01f, settings.slotSizeScale);
            float halfAngle = step * 0.5f * scale;

            float inner = Math.Min(settings.innerRadius, settings.outerRadius);
            float outer = Math.Max(settings.innerRadius, settings.outerRadius);
            float midRadius = (inner + outer) * 0.5f;
            float halfThickness = (outer - inner) * 0.5f * scale;

            // Ornament end: the upper arc ends at the top, the lower arc at the bottom.
            float ornamentEnd = side == ManaArcSide.Upper
                ? gapCenter - halfGap - span
                : gapCenter + halfGap + span;
            float gapEnd = side == ManaArcSide.Upper
                ? gapCenter - halfGap
                : gapCenter + halfGap;

            bool fromOrnament = settings.fillDirection == ManaFillDirection.FromOrnament;
            float start = fromOrnament ? ornamentEnd : gapEnd;
            float direction = Math.Sign((fromOrnament ? gapEnd : ornamentEnd) - start);

            for (int i = 0; i < count; i++)
            {
                float center = start + direction * step * (i + 0.5f);
                buffer[i] = new ManaSlotGeometry(
                    center,
                    halfAngle,
                    midRadius - halfThickness,
                    midRadius + halfThickness
                );
            }
        }

        #endregion
    }
}
