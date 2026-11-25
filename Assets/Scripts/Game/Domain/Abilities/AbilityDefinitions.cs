using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    [Flags]
    public enum UnitKeyword : ushort
    {
        None = 0,
        Fearsome = 1 << 0,
        CannotBlock = 1 << 1,
        Ephemeral = 1 << 2,
        Lifesteal = 1 << 3,
    }

    public enum AbilityTrigger : byte
    {
        Play,
        Summon,
        Attack,
        /// <summary>Adjacent units support on both attack and block declarations.</summary>
        Support,
        RoundStart,
        Death,
        AllyDeath,
        AllySummon,
    }

    public enum AbilityCondition : byte
    {
        None,
        AllyDiedThisRound,
        AllyDiedPreviousRound,
    }

    public enum MissingTargetPolicy : byte
    {
        RejectPlay = 0,
        SkipAbility = 1,
    }

    public enum AbilityTargetSlot : byte
    {
        Primary,
        Secondary,
    }

    public enum TargetRelation : byte
    {
        Ally,
        Enemy,
        Any,
    }

    [Flags]
    public enum AbilityTargetZone : byte
    {
        None = 0,
        DrawHand = 1 << 0,
        Reserve = 1 << 1,
        Board = 1 << 2,
        Graveyard = 1 << 3,
        Deck = 1 << 4,
        Stack = 1 << 5,

        ActiveRoster = Reserve | Board,
    }

    public enum EffectKind : byte
    {
        Damage,
        Heal,
        Kill,
        Draw,
        Buff,
        Sacrifice,
        Revive,
        Copy,
        ReduceCost,
        CounterSpell,
        ReturnToDrawHand,
        ReturnToReserve,
        KillAllUnits,
        HalfNexus,
        Summon,
    }

    public enum EffectTarget : byte
    {
        None,

        Source,
        SourceOwner,
        TriggerSubject,

        PrimarySelection,
        SecondarySelection,

        AlliedNexus,
        EnemyNexus,

        AllUnits,
        WeakestAllies,
        WeakestEnemies,
        StrongestDeadAlly,
        RandomDeadAlly,
    }

    public enum EffectDuration : byte
    {
        Permanent,
        ThisRound,

        /// <summary>
        /// Ends when the unit leaves the board: after combat, at round end, on death or
        /// when an effect returns it. Appended so serialized card assets keep their values.
        /// </summary>
        WhileOnBoard,
    }

    /// <summary>
    /// Describes one group of targets that must be selected and validated
    /// before an ability starts resolving.
    /// </summary>
    public sealed class AbilityTargetRequirement
    {
        #region Properties

        public AbilityTargetSlot Slot { get; }
        public TargetRelation Relation { get; }
        public AbilityTargetZone Zones { get; }
        public int Count { get; }
        public bool ExcludeSource { get; }

        #endregion

        #region Construction

        public AbilityTargetRequirement(
            AbilityTargetSlot slot,
            TargetRelation relation,
            AbilityTargetZone zones,
            int count = 1,
            bool excludeSource = true
        )
        {
            if (zones == AbilityTargetZone.None)
            {
                throw new ArgumentException(
                    "At least one target zone is required.",
                    nameof(zones)
                );
            }

            Slot = slot;
            Relation = relation;
            Zones = zones;
            Count = Math.Max(1, count);
            ExcludeSource = excludeSource;
        }

        #endregion
    }

    /// <summary>
    /// Immutable description of one effect.
    /// Amount is normally damage, healing, draw count or Power buff.
    /// SecondaryAmount is normally Health buff.
    /// Count is the number of automatically selected targets.
    /// </summary>
    public sealed class EffectDefinition
    {
        #region Properties

        public EffectKind Kind { get; }
        public EffectTarget Target { get; }

        public int Amount { get; }
        public int SecondaryAmount { get; }
        public int Count { get; }

        public EffectDuration Duration { get; }

        public string CardDefinitionId { get; }

        #endregion

        #region Construction

        public EffectDefinition(
            EffectKind kind,
            EffectTarget target,
            int amount = 0,
            int secondaryAmount = 0,
            int count = 1,
            EffectDuration duration = EffectDuration.Permanent,
            string cardDefinitionId = null
        )
        {
            Kind = kind;
            Target = target;
            Amount = amount;
            SecondaryAmount = secondaryAmount;
            Count = Math.Max(1, count);
            Duration = duration;
            CardDefinitionId = cardDefinitionId ?? string.Empty;
        }

        #endregion
    }

    /// <summary>
    /// Immutable description of one triggered ability.
    /// </summary>
    public sealed class AbilityDefinition
    {
        #region Properties

        public string Id { get; }
        public AbilityTrigger Trigger { get; }
        public AbilityCondition Condition { get; }
        public bool OncePerRound { get; }
        public bool OncePerMatch { get; }
        public MissingTargetPolicy MissingTargetPolicy { get; }

        public IReadOnlyList<AbilityTargetRequirement> RequiredTargets { get; }
        public IReadOnlyList<EffectDefinition> Effects { get; }

        #endregion

        #region Construction

        public AbilityDefinition(
            string id,
            AbilityTrigger trigger,
            IReadOnlyList<EffectDefinition> effects,
            AbilityCondition condition = AbilityCondition.None,
            bool oncePerRound = false,
            IReadOnlyList<AbilityTargetRequirement> requiredTargets = null,
            bool oncePerMatch = false,
            MissingTargetPolicy missingTargetPolicy = MissingTargetPolicy.RejectPlay
        )
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Ability ID is required.", nameof(id));
            }

            if (effects == null || effects.Count == 0)
            {
                throw new ArgumentException(
                    "An ability requires at least one effect.",
                    nameof(effects)
                );
            }

            if (oncePerRound && oncePerMatch)
            {
                throw new ArgumentException(
                    "An ability cannot be both once-per-round " + "and once-per-match."
                );
            }

            Id = id;
            Trigger = trigger;
            Condition = condition;
            OncePerRound = oncePerRound;
            OncePerMatch = oncePerMatch;
            MissingTargetPolicy = missingTargetPolicy;

            RequiredTargets = CopyList(requiredTargets, nameof(requiredTargets));

            Effects = CopyList(effects, nameof(effects));
        }

        #endregion

        #region Collection Helpers

        private static IReadOnlyList<T> CopyList<T>(
            IReadOnlyList<T> source,
            string parameterName
        )
            where T : class
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<T>();
            }

            var copy = new T[source.Count];

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] == null)
                {
                    throw new ArgumentException(
                        "Collection cannot contain null values.",
                        parameterName
                    );
                }

                copy[i] = source[i];
            }

            return Array.AsReadOnly(copy);
        }

        #endregion
    }
}
