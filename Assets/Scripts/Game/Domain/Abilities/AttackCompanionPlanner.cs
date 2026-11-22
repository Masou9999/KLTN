using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Predicts which Last Breath companion lands in which empty attack slot. Mirrors
    /// <see cref="UnitPassiveRuleResolver.JoinAttackFromGraveyard"/>: slots fill left to
    /// right, companions are taken in their listed order, and a companion that needs an
    /// Ephemeral attacker is skipped (but not consumed) when none was declared.
    /// </summary>
    public static class AttackCompanionPlanner
    {
        /// <param name="companionRequiresEphemeral">
        /// One entry per pending companion, in <c>GetPendingAttackCompanions</c> order.
        /// </param>
        /// <param name="occupiedSlots">Slots the player filled with declared attackers.</param>
        /// <param name="hasEphemeralAttacker">Whether any declared attacker is Ephemeral.</param>
        /// <returns>For each slot, the companion index placed there, or -1.</returns>
        public static int[] PlanSlots(
            IReadOnlyList<bool> companionRequiresEphemeral,
            IReadOnlyList<bool> occupiedSlots,
            bool hasEphemeralAttacker
        )
        {
            if (companionRequiresEphemeral == null)
            {
                throw new ArgumentNullException(nameof(companionRequiresEphemeral));
            }

            if (occupiedSlots == null)
            {
                throw new ArgumentNullException(nameof(occupiedSlots));
            }

            var result = new int[occupiedSlots.Count];
            bool anyDeclared = false;

            for (int slot = 0; slot < result.Length; slot++)
            {
                result[slot] = -1;
                anyDeclared |= occupiedSlots[slot];
            }

            // An attack needs at least one declared unit; companions never attack alone.
            if (!anyDeclared)
            {
                return result;
            }

            int candidate = 0;

            for (int slot = 0; slot < result.Length; slot++)
            {
                if (occupiedSlots[slot])
                {
                    continue;
                }

                while (candidate < companionRequiresEphemeral.Count)
                {
                    int index = candidate++;

                    if (companionRequiresEphemeral[index] && !hasEphemeralAttacker)
                    {
                        continue;
                    }

                    result[slot] = index;
                    break;
                }
            }

            return result;
        }
    }
}
