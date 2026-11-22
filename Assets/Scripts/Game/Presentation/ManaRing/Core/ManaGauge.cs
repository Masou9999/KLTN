using System;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Display-side mana counter for one arc of the mana ring. Values are clamped, and
    /// <see cref="Changed"/> fires only when something visible changes. Reserved mana is
    /// the part of <see cref="CurrentMana"/> tentatively spent by staged cards.
    /// </summary>
    public sealed class ManaGauge
    {
        #region Runtime State

        private int maxMana;
        private int currentMana;
        private int reservedMana;

        #endregion

        #region Construction

        public ManaGauge(int maxMana)
        {
            this.maxMana = Math.Max(0, maxMana);
        }

        #endregion

        #region Events / Properties

        public event Action<ManaGauge> Changed;

        public int MaxMana => maxMana;
        public int CurrentMana => currentMana;

        /// <summary>Reserved mana, never more than <see cref="CurrentMana"/>.</summary>
        public int ReservedMana => Math.Min(reservedMana, currentMana);

        /// <summary>Mana still free after the reservation.</summary>
        public int AvailableMana => currentMana - ReservedMana;

        #endregion

        #region Public Commands

        public void SetMaxMana(int value)
        {
            int clamped = Math.Max(0, value);

            if (clamped == maxMana)
            {
                return;
            }

            maxMana = clamped;
            currentMana = Math.Min(currentMana, maxMana);
            Notify();
        }

        public void SetMana(int value)
        {
            int clamped = Clamp(value, 0, maxMana);

            if (clamped == currentMana)
            {
                return;
            }

            currentMana = clamped;
            Notify();
        }

        public void AddMana(int amount)
        {
            SetMana(currentMana + Math.Max(0, amount));
        }

        /// <summary>
        /// Spends mana immediately. Returns false and leaves the gauge untouched when
        /// <paramref name="amount"/> exceeds the current mana.
        /// </summary>
        public bool ConsumeMana(int amount)
        {
            if (amount < 0 || amount > currentMana)
            {
                return false;
            }

            SetMana(currentMana - amount);
            return true;
        }

        public void SetReserved(int value)
        {
            int clamped = Clamp(value, 0, maxMana);

            if (clamped == reservedMana)
            {
                return;
            }

            reservedMana = clamped;
            Notify();
        }

        #endregion

        #region Queries

        /// <summary>
        /// State of the slot at fill index <paramref name="index"/> (0 fills first):
        /// free mana first, then the reserved tail, then empty slots.
        /// </summary>
        public ManaSlotState StateOf(int index)
        {
            if (index < AvailableMana)
            {
                return ManaSlotState.Filled;
            }

            return index < currentMana ? ManaSlotState.Reserved : ManaSlotState.Empty;
        }

        #endregion

        #region Helpers

        private void Notify()
        {
            Changed?.Invoke(this);
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }

        #endregion
    }
}
