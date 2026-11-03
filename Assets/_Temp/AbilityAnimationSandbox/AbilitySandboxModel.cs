#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using KLTN.Game.Domain;
using KLTN.Game.Networking;

namespace KLTN.Sandbox
{
    /// <summary>
    /// Owns an offline fixture. Only setup/editing and resource replenishment bypass
    /// rules; every played ability is resolved by the real match rules engine.
    /// </summary>
    public sealed class AbilitySandboxModel
    {
        #region Configuration

        public const int HandLimit = 10;
        public static readonly string[] ReserveIds =
        {
            "MaLon", "VongNhi", "ThanTrung", "ThienLinhCai", "OngKe", "MaTranh", "MaDoi"
        };
        public static readonly string[] HandIds =
        {
            "ThanTrung", "ThienLinhCai", "OngKe", "MaTranh", "MaDoi"
        };

        #endregion

        #region Dependencies And State

        private readonly IReadOnlyDictionary<string, CardDefinition> definitions;
        private readonly MatchSnapshotBuilder snapshots;
        private readonly AbilityResolutionDtoMapper abilities;
        private MatchRulesEngine rules;
        private ulong revision;

        public MatchState State { get; private set; }
        public CardDefinition Definition(string id) => definitions[id];

        #endregion

        #region Construction And Fixture Editing

        public AbilitySandboxModel(
            IReadOnlyDictionary<string, CardDefinition> definitions)
        {
            this.definitions = definitions;
            snapshots = new MatchSnapshotBuilder(definitions);
            abilities = new AbilityResolutionDtoMapper(definitions);
            Reset();
        }

        /// <summary>
        /// Recreates both reserves, empty hands and a deterministic draw deck.
        /// </summary>
        public void Reset()
        {
            var deck = new List<CardDefinition>();
            for (int i = 0; i < DeckRules.RequiredCardCount; i++)
            {
                deck.Add(definitions[HandIds[i % HandIds.Length]]);
            }
            var random = new SeededRandomSource(731);
            State = new MatchFactory(random).Create(deck, deck, 0);
            rules = new MatchRulesEngine(definitions, random);
            State.HostMulliganDone = State.GuestMulliganDone = true;
            State.BeginFirstRound();
            foreach (SeatId seat in new[] { SeatId.Host, SeatId.Guest })
            {
                Add(seat, "MaLon", CardZone.Reserve);
                Add(seat, "MaLon", CardZone.Reserve);
                Add(seat, "VongNhi", CardZone.Reserve);
            }
            PrepareNextAction();
        }

        public int Count(string id, bool reserve)
        {
            return Zone(reserve).Count(card => card.DefinitionId == id);
        }

        /// <summary>
        /// Applies a validated modal draft atomically. Removing a fixture card is not
        /// a death, and unchanged cards keep their identity, damage and trigger history.
        /// </summary>
        public bool ApplyCounts(int[] reserveCounts, int[] handCounts)
        {
            if (State.PendingSelection != null
                || !ValidCounts(reserveCounts, ReserveIds.Length)
                || !ValidCounts(handCounts, HandIds.Length)
                || reserveCounts.Sum() + State.Host.Board.Count
                    > MatchState.MaximumActiveRosterSize
                || handCounts.Sum() > HandLimit)
            {
                return false;
            }
            // Played test units can be removed from Reserve, but only enter via Play.
            for (int i = 2; i < ReserveIds.Length; i++)
            {
                if (reserveCounts[i] > Count(ReserveIds[i], true))
                {
                    return false;
                }
            }
            Reconcile(ReserveIds, reserveCounts, true);
            Reconcile(HandIds, handCounts, false);
            PrepareNextAction();
            return true;
        }

        private static bool ValidCounts(int[] counts, int length)
        {
            return counts != null && counts.Length == length
                && counts.All(count => count >= 0 && count <= HandLimit);
        }

        private List<CardInstance> Zone(bool reserve)
        {
            return reserve ? State.Host.Reserve : State.Host.DrawHand;
        }

        private void Reconcile(string[] ids, int[] counts, bool reserve)
        {
            // Remove first so replacing cards at capacity works regardless of row order.
            List<CardInstance> zone = Zone(reserve);
            for (int i = 0; i < ids.Length; i++)
            {
                int excess = Count(ids[i], reserve) - counts[i];
                for (int j = zone.Count - 1; j >= 0 && excess > 0; j--)
                {
                    if (zone[j].DefinitionId == ids[i])
                    {
                        zone.RemoveAt(j);
                        excess--;
                    }
                }
            }
            for (int i = 0; i < ids.Length; i++)
            {
                while (Count(ids[i], reserve) < counts[i])
                {
                    Add(SeatId.Host, ids[i],
                        reserve ? CardZone.Reserve : CardZone.DrawHand);
                }
            }
        }

        private void Add(SeatId seat, string id, CardZone zone)
        {
            var card = new CardInstance(State.AllocateCardInstanceId(), id,
                seat, zone, definitions[id].BaseHealth);
            bool added = zone == CardZone.Reserve
                ? State.Player(seat).TryAddCreatedCardToReserve(card)
                : State.Player(seat).TryAddCreatedCardToDrawHand(card);
            if (!added)
            {
                throw new InvalidOperationException("Invalid sandbox fixture: " + id);
            }
        }

        #endregion

        #region Ability Commands And Publication

        public CommandResult Summon(ulong id)
        {
            return Complete(rules.TrySummonUnit(State, SeatId.Host, id));
        }

        public CommandResult Select(ulong request, ulong[] primary, ulong[] secondary)
        {
            return Complete(rules.TrySubmitAbilitySelection(State, SeatId.Host,
                request, new AbilityTargetSelection(primary, secondary)));
        }

        public CommandResult Cancel(ulong request)
        {
            return Complete(rules.TryCancelAbilitySelection(State, SeatId.Host, request));
        }

        private CommandResult Complete(CommandResult result)
        {
            PrepareNextAction();
            // Draw effects use real rules, with excess cards returned to the local deck.
            while (State.Host.DrawHand.Count > HandLimit)
            {
                CardInstance card = State.Host.DrawHand[HandLimit];
                State.Host.DrawHand.RemoveAt(HandLimit);
                card.MoveTo(CardZone.Deck);
                State.Host.Deck.Insert(0, card);
            }
            return result;
        }

        private void PrepareNextAction()
        {
            State.Host.InitializeMana(10);
            State.Guest.InitializeMana(10);
            State.AttackTokenAvailable = false;
            if (State.PendingSelection == null && !State.IsFinished)
            {
                State.ActiveSeat = SeatId.Host;
                State.ConsecutivePasses = 0;
            }
            State.LastEvent = "Test kỹ năng • 10 mana • Một vòng duy nhất";
        }

        /// <summary>
        /// Publishes effects once, retaining a strictly increasing revision.
        /// </summary>
        public MatchUpdateDto BuildUpdate()
        {
            MatchSnapshotDto snapshot = snapshots.Build(State, SeatId.Host, true,
                ++revision);
            snapshot.matchId = "offline-ability-sandbox";
            snapshot.self.displayName = "Bạn • Test kỹ năng";
            snapshot.opponent.displayName = "Mục tiêu test";
            snapshot.selfConnection = snapshot.opponentConnection = 1;
            snapshot.viewerCanDeclareAttack = false;
            snapshot.viewerCanDeclareBlock = false;
            snapshot.viewerCanEndRound = false;
            AbilityResolutionDto resolution = abilities.Build(
                State.TakeAbilityResolution(), SeatId.Host);
            return new MatchUpdateDto
            {
                snapshot = snapshot,
                hasAbilityResolution = resolution != null,
                abilityResolution = resolution
            };
        }

        #endregion
    }
}
#endif
