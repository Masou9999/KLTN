using System;
using System.Collections.Generic;

namespace KLTN.Game.Presentation
{
    public enum BoardDropHintMode
    {
        None = 0,
        Attack = 1,
        Block = 2,
    }

    /// <summary>
    /// Decides which board slots show the drop hint while a Reserve card is dragged.
    /// Purely visual: the host still validates every staged action.
    /// </summary>
    public static class BoardDropHintRules
    {
        #region Public API

        /// <param name="slotOccupied">True when the slot already holds a card.</param>
        /// <param name="slotDefendable">
        /// Block mode only: true when an opponent attacker sits opposite the slot and
        /// the dragged card may block it.
        /// </param>
        public static bool[] ComputeEligibleSlots(
            BoardDropHintMode mode,
            int slotCount,
            IReadOnlyList<bool> slotOccupied,
            IReadOnlyList<bool> slotDefendable
        )
        {
            var eligible = new bool[Math.Max(0, slotCount)];

            if (mode == BoardDropHintMode.None)
            {
                return eligible;
            }

            for (int slot = 0; slot < eligible.Length; slot++)
            {
                if (IsSet(slotOccupied, slot))
                {
                    continue;
                }

                eligible[slot] = mode == BoardDropHintMode.Attack || IsSet(slotDefendable, slot);
            }

            return eligible;
        }

        #endregion

        #region Helpers

        private static bool IsSet(IReadOnlyList<bool> values, int index)
        {
            return values != null && index < values.Count && values[index];
        }

        #endregion
    }
}
