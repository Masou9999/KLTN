using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public sealed class DeathRecord
    {
        #region Properties

        public ulong Sequence { get; }
        public ulong CardInstanceId { get; }

        public string DefinitionId { get; }
        public SeatId Owner { get; }

        public int RoundNumber { get; }
        public int DamageAtDeath { get; }
        public int MaximumHealthAtDeath { get; }

        #endregion

        #region Construction

        public DeathRecord(
            ulong sequence,
            ulong cardInstanceId,
            string definitionId,
            SeatId owner,
            int roundNumber,
            int damageAtDeath,
            int maximumHealthAtDeath
        )
        {
            Sequence = sequence;
            CardInstanceId = cardInstanceId;
            DefinitionId = definitionId ?? string.Empty;
            Owner = owner;
            RoundNumber = roundNumber;
            DamageAtDeath = Math.Max(0, damageAtDeath);
            MaximumHealthAtDeath = Math.Max(0, maximumHealthAtDeath);
        }

        #endregion
    }

    public sealed class RoundHistory
    {
        #region State

        private readonly List<DeathRecord> currentRoundDeaths = new List<DeathRecord>();

        private readonly List<DeathRecord> previousRoundDeaths = new List<DeathRecord>();

        private readonly List<DeathRecord> allDeaths = new List<DeathRecord>();

        private readonly HashSet<ulong> consumedAttackDeaths = new HashSet<ulong>();

        private readonly HashSet<AbilityUseKey> usedAbilities =
            new HashSet<AbilityUseKey>();

        private readonly HashSet<AbilityUseKey> usedAbilitiesThisMatch =
            new HashSet<AbilityUseKey>();

        private ulong nextDeathSequence = 1;

        public int TrackedRoundNumber { get; private set; }

        public IReadOnlyList<DeathRecord> CurrentRoundDeaths => currentRoundDeaths;
        public IReadOnlyList<DeathRecord> PreviousRoundDeaths => previousRoundDeaths;
        public IReadOnlyList<DeathRecord> AllDeaths => allDeaths;

        public bool HasJoinedAttack(DeathRecord death)
        {
            return consumedAttackDeaths.Contains(death.Sequence);
        }

        /// <summary>
        /// Consumes one death only after its unit successfully joins an attack.
        /// Ordinary revival and round transitions do not consume this entitlement.
        /// </summary>
        internal void ConsumeAttackDeath(DeathRecord death)
        {
            consumedAttackDeaths.Add(death.Sequence);
        }

        #endregion

        #region Round Lifecycle

        public void BeginFirstRound(int roundNumber)
        {
            if (roundNumber <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(roundNumber));
            }

            if (TrackedRoundNumber == roundNumber)
            {
                return;
            }

            if (TrackedRoundNumber != 0)
            {
                throw new InvalidOperationException(
                    "Round history was already initialized."
                );
            }

            TrackedRoundNumber = roundNumber;
        }

        public void AdvanceToRound(int nextRoundNumber)
        {
            if (TrackedRoundNumber <= 0)
            {
                throw new InvalidOperationException(
                    "Round history has not been initialized."
                );
            }

            if (nextRoundNumber != TrackedRoundNumber + 1)
            {
                throw new InvalidOperationException(
                    "Round history must advance exactly one round."
                );
            }

            previousRoundDeaths.Clear();
            previousRoundDeaths.AddRange(currentRoundDeaths);

            currentRoundDeaths.Clear();
            usedAbilities.Clear();

            TrackedRoundNumber = nextRoundNumber;
        }

        #endregion

        #region Death Tracking

        public DeathRecord RecordDeath(
            CardInstance card,
            int damageAtDeath,
            int maximumHealthAtDeath
        )
        {
            if (card == null)
            {
                throw new ArgumentNullException(nameof(card));
            }

            if (TrackedRoundNumber <= 0)
            {
                throw new InvalidOperationException(
                    "Cannot record a death before round one."
                );
            }

            if (!card.IsDead)
            {
                throw new InvalidOperationException("Only dead cards can be recorded.");
            }

            var record = new DeathRecord(
                nextDeathSequence++,
                card.InstanceId,
                card.DefinitionId,
                card.Owner,
                TrackedRoundNumber,
                damageAtDeath,
                maximumHealthAtDeath
            );

            currentRoundDeaths.Add(record);
            allDeaths.Add(record);

            return record;
        }

        public bool HasDeathThisRound(SeatId owner)
        {
            return currentRoundDeaths.Exists(death => death.Owner == owner);
        }

        public bool HasDeathPreviousRound(SeatId owner)
        {
            return previousRoundDeaths.Exists(death => death.Owner == owner);
        }

        public int CountDeathsThisGame(SeatId owner)
        {
            int count = 0;

            foreach (DeathRecord death in allDeaths)
            {
                if (death.Owner == owner)
                {
                    count++;
                }
            }

            return count;
        }

        public int CountDeathsThisGame(ulong cardInstanceId)
        {
            int count = 0;

            foreach (DeathRecord death in allDeaths)
            {
                if (death.CardInstanceId == cardInstanceId)
                {
                    count++;
                }
            }

            return count;
        }

        public int GetLatestDeathRound(ulong cardInstanceId)
        {
            int latestRound = 0;

            foreach (DeathRecord death in allDeaths)
            {
                if (
                    death.CardInstanceId == cardInstanceId
                    && death.RoundNumber > latestRound
                )
                {
                    latestRound = death.RoundNumber;
                }
            }

            return latestRound;
        }

        #endregion

        #region Ability Usage Tracking

        public bool WasAbilityUsedThisRound(ulong cardInstanceId, string abilityId)
        {
            return usedAbilities.Contains(new AbilityUseKey(cardInstanceId, abilityId));
        }

        public bool TryMarkAbilityUsed(ulong cardInstanceId, string abilityId)
        {
            if (string.IsNullOrWhiteSpace(abilityId))
            {
                throw new ArgumentException("Ability ID is required.", nameof(abilityId));
            }

            return usedAbilities.Add(new AbilityUseKey(cardInstanceId, abilityId));
        }

        public bool TryMarkAbilityUsedThisMatch(ulong cardInstanceId, string abilityId)
        {
            if (string.IsNullOrWhiteSpace(abilityId))
            {
                throw new ArgumentException("Ability ID is required.", nameof(abilityId));
            }

            return usedAbilitiesThisMatch.Add(
                new AbilityUseKey(cardInstanceId, abilityId)
            );
        }

        #endregion

        #region Value Types

        private struct AbilityUseKey : IEquatable<AbilityUseKey>
        {
            private readonly ulong cardInstanceId;
            private readonly string abilityId;

            public AbilityUseKey(ulong cardInstanceId, string abilityId)
            {
                this.cardInstanceId = cardInstanceId;

                this.abilityId = abilityId ?? string.Empty;
            }

            public bool Equals(AbilityUseKey other)
            {
                return cardInstanceId == other.cardInstanceId
                    && string.Equals(
                        abilityId,
                        other.abilityId,
                        StringComparison.Ordinal
                    );
            }

            public override bool Equals(object obj)
            {
                return obj is AbilityUseKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = cardInstanceId.GetHashCode();

                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(abilityId);

                    return hash;
                }
            }
        }

        #endregion
    }
}
