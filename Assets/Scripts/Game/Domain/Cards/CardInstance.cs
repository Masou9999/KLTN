using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Mutable state belonging to one physical card in one match.
    /// </summary>
    public sealed class CardInstance
    {
        #region Properties

        public ulong InstanceId { get; }
        public string DefinitionId { get; }
        public SeatId Owner { get; }

        public CardZone Zone { get; private set; }
        public int CurrentHealth { get; private set; }
        public int BoardSlotIndex { get; private set; } = -1;

        public int PermanentDamageModifier { get; private set; }
        public int PermanentHealthModifier { get; private set; }

        public int RoundDamageModifier { get; private set; }
        public int RoundHealthModifier { get; private set; }

        /// <summary>Buffs that last only while this unit stays on the board.</summary>
        public int BoardDamageModifier { get; private set; }
        public int BoardHealthModifier { get; private set; }

        public int PermanentCostModifier { get; private set; }
        public int RoundCostModifier { get; private set; }

        /// <summary>
        /// True while this unit fights only because a graveyard passive pulled it into
        /// the current attack. It survives the unit's death so combat presentation can
        /// still identify it, and clears on revival or a return to an ordinary zone.
        /// </summary>
        public bool IsAttackCompanion { get; private set; }

        public bool IsDead => CurrentHealth <= 0;

        public bool IsInActiveRoster =>
            Zone == CardZone.Reserve || Zone == CardZone.Board;

        #endregion

        #region Construction

        public CardInstance(
            ulong instanceId,
            string definitionId,
            SeatId owner,
            CardZone zone,
            int startingHealth
        )
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Owner = owner;
            Zone = zone;
            CurrentHealth = Math.Max(0, startingHealth);
        }

        #endregion

        #region Runtime Stats

        public int GetDamage(CardDefinition definition)
        {
            if (definition == null)
            {
                return 0;
            }

            return Math.Max(
                0,
                definition.BaseDamage + PermanentDamageModifier + RoundDamageModifier
                    + BoardDamageModifier
            );
        }

        public int GetMaximumHealth(CardDefinition definition)
        {
            if (definition == null)
            {
                return 0;
            }

            return Math.Max(
                0,
                definition.BaseHealth + PermanentHealthModifier + RoundHealthModifier
                    + BoardHealthModifier
            );
        }

        public int GetCost(CardDefinition definition)
        {
            if (definition == null)
            {
                return 0;
            }

            return Math.Max(
                0,
                definition.Cost + PermanentCostModifier + RoundCostModifier
            );
        }

        public void ApplyCostReduction(
            CardDefinition definition,
            int amount,
            EffectDuration duration
        )
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            int reduction = Math.Max(0, amount);

            switch (duration)
            {
                case EffectDuration.Permanent:
                    PermanentCostModifier -= reduction;
                    break;

                case EffectDuration.ThisRound:
                    RoundCostModifier -= reduction;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(duration));
            }
        }

        public void ApplyBuff(
            CardDefinition definition,
            int damageAmount,
            int healthAmount,
            EffectDuration duration
        )
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            switch (duration)
            {
                case EffectDuration.Permanent:
                    PermanentDamageModifier += damageAmount;
                    PermanentHealthModifier += healthAmount;
                    break;

                case EffectDuration.ThisRound:
                    RoundDamageModifier += damageAmount;
                    RoundHealthModifier += healthAmount;
                    break;

                case EffectDuration.WhileOnBoard:
                    BoardDamageModifier += damageAmount;
                    BoardHealthModifier += healthAmount;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(duration));
            }

            CurrentHealth = Math.Max(0, CurrentHealth + healthAmount);

            CurrentHealth = Math.Min(CurrentHealth, GetMaximumHealth(definition));
        }

        public void ClearRoundModifiers(CardDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            RoundDamageModifier = 0;
            RoundHealthModifier = 0;
            RoundCostModifier = 0;
            BoardDamageModifier = 0;
            BoardHealthModifier = 0;

            CurrentHealth = Math.Min(CurrentHealth, GetMaximumHealth(definition));
        }

        /// <summary>Drops while-on-board buffs; call before the unit leaves the board.</summary>
        public void ClearBoardModifiers(CardDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            BoardDamageModifier = 0;
            BoardHealthModifier = 0;

            CurrentHealth = Math.Min(CurrentHealth, GetMaximumHealth(definition));
        }

        public void ClearRoundModifiersOnDeath()
        {
            if (!IsDead)
            {
                throw new InvalidOperationException(
                    "Only dead cards can clear round modifiers on death."
                );
            }

            RoundDamageModifier = 0;
            RoundHealthModifier = 0;
            RoundCostModifier = 0;
            BoardDamageModifier = 0;
            BoardHealthModifier = 0;
        }

        public void ResetToBase(CardDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            PermanentDamageModifier = 0;
            PermanentHealthModifier = 0;
            PermanentCostModifier = 0;

            RoundDamageModifier = 0;
            RoundHealthModifier = 0;
            RoundCostModifier = 0;
            BoardDamageModifier = 0;
            BoardHealthModifier = 0;

            CurrentHealth = definition.BaseHealth;

            BoardSlotIndex = -1;

            IsAttackCompanion = false;
        }

        #endregion

        #region State Mutations

        public void MoveTo(CardZone zone, int boardSlotIndex = -1)
        {
            Zone = zone;

            BoardSlotIndex = zone == CardZone.Board ? boardSlotIndex : -1;

            if (zone != CardZone.Board && zone != CardZone.Graveyard)
            {
                IsAttackCompanion = false;
            }
        }

        internal void MarkAttackCompanion()
        {
            IsAttackCompanion = true;
        }

        public void ApplyDamage(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            CurrentHealth = Math.Max(0, CurrentHealth - amount);
        }

        public void Heal(CardDefinition definition, int amount)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (amount <= 0 || IsDead)
            {
                return;
            }

            CurrentHealth = Math.Min(
                GetMaximumHealth(definition),
                CurrentHealth + amount
            );
        }

        public void Kill()
        {
            CurrentHealth = 0;
        }

        #endregion
    }
}
