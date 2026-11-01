using System;
using System.Collections.Generic;
using KLTN.Game.Domain;

namespace KLTN.Game.ApplicationLayer
{
    /// <summary>
    /// One match hosted by a server process (SDS HLD 10.2): its domain state and rules engine,
    /// the seats bound to player keys (<see cref="SeatConnectionTracker"/>) and the transport
    /// connections currently sitting in those seats. Several sessions can live in one process;
    /// nothing here is shared between them.
    /// </summary>
    public sealed class MatchSession
    {
        #region Constants

        public const int PlayersPerMatch = 2;

        #endregion

        #region State

        private readonly Dictionary<ulong, SeatId> seatByConnection = new Dictionary<ulong, SeatId>();
        private readonly Dictionary<ulong, ulong> lastCommandIdByConnection = new Dictionary<ulong, ulong>();
        private readonly Dictionary<SeatId, string[]> loadoutBySeat = new Dictionary<SeatId, string[]>();
        private readonly HashSet<string> expectedPlayerIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> members = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Construction

        /// <param name="matchId">Backend match ID (MATCH_ID) or a local ID on listen-host.</param>
        /// <param name="matchmakerMatchId">ID the client knows from the matchmaker; may be null.</param>
        /// <param name="expectedPlayerIds">Players allocated to this match; empty accepts anyone.</param>
        public MatchSession(
            string matchId,
            string matchmakerMatchId = null,
            IEnumerable<string> expectedPlayerIds = null,
            long createdAtMs = 0,
            long gracePeriodMilliseconds = SeatConnectionTracker.DefaultGracePeriodMilliseconds
        )
        {
            if (string.IsNullOrWhiteSpace(matchId))
            {
                throw new ArgumentException("Match ID is required.", nameof(matchId));
            }

            MatchId = matchId;
            MatchmakerMatchId = matchmakerMatchId;
            CreatedAtMs = createdAtMs;
            Seats = new SeatConnectionTracker(gracePeriodMilliseconds);

            if (expectedPlayerIds != null)
            {
                foreach (string playerId in expectedPlayerIds)
                {
                    if (!string.IsNullOrWhiteSpace(playerId))
                    {
                        this.expectedPlayerIds.Add(playerId);
                    }
                }
            }
        }

        #endregion

        #region Properties

        public string MatchId { get; }

        public string MatchmakerMatchId { get; }

        public long CreatedAtMs { get; }

        public SeatConnectionTracker Seats { get; }

        /// <summary>Null until both players are seated and their decks are built.</summary>
        public MatchState State { get; private set; }

        public MatchRulesEngine Rules { get; private set; }

        public bool IsStarted => State != null && Rules != null;

        public bool IsFinished => State != null && State.IsFinished;

        /// <summary>The allocated pair has connected; afterwards only a held seat may be taken back.</summary>
        public bool IsSealed { get; private set; }

        /// <summary>The match is over for the server (ended, abandoned or cancelled).</summary>
        public bool IsClosed { get; private set; }

        public bool IsRanked { get; private set; }

        public long StartedUtcMs { get; private set; }

        /// <summary>Snapshot revision of this match; every published update increments it.</summary>
        public ulong Revision { get; private set; }

        /// <summary>The result was handed to the backend (or skipped); it must not happen twice.</summary>
        public bool IsSettlementStarted { get; private set; }

        public bool IsPaused => Seats.IsPaused;

        /// <summary>A seat is waiting for its player and the match can still continue.</summary>
        public bool IsHoldingSeats => !IsClosed && !IsFinished && Seats.IsHoldingSeats;

        public bool HasExpectedPlayers => expectedPlayerIds.Count > 0;

        public IReadOnlyCollection<string> ExpectedPlayerIds => expectedPlayerIds;

        public int ConnectionCount => seatByConnection.Count;

        public IReadOnlyDictionary<ulong, SeatId> Connections => seatByConnection;

        /// <summary>Players admitted or being verified before the pair was sealed.</summary>
        public IReadOnlyCollection<string> Members => members;

        #endregion

        #region Routing

        /// <summary>Whether this player belongs to the match (any player when no list is known).</summary>
        public bool Expects(string playerId)
        {
            return !string.IsNullOrEmpty(playerId)
                && (expectedPlayerIds.Count == 0 || expectedPlayerIds.Contains(playerId));
        }

        /// <summary>The client named this match, by backend ID or matchmaker ID (dashes ignored).</summary>
        public bool IsNamed(string requestedMatchId)
        {
            string requested = MatchAdmissionPolicy.NormalizeMatchId(requestedMatchId);

            return requested.Length > 0
                && (requested == MatchAdmissionPolicy.NormalizeMatchId(MatchId)
                    || requested == MatchAdmissionPolicy.NormalizeMatchId(MatchmakerMatchId));
        }

        /// <summary>Keeps a place for a player while the backend verifies them.</summary>
        public bool TryAddMember(string playerId)
        {
            if (IsSealed || IsClosed || !Expects(playerId) || members.Count >= PlayersPerMatch)
            {
                return false;
            }

            return members.Add(playerId);
        }

        public void RemoveMember(string playerId)
        {
            if (!IsSealed && playerId != null)
            {
                members.Remove(playerId);
            }
        }

        public bool HasMember(string playerId)
        {
            return playerId != null && members.Contains(playerId);
        }

        public void Seal()
        {
            IsSealed = true;
        }

        public void Close()
        {
            IsClosed = true;
            Seats.Resolve();
        }

        #endregion

        #region Connections and Seats

        public bool TryGetSeat(ulong connectionId, out SeatId seat)
        {
            return seatByConnection.TryGetValue(connectionId, out seat);
        }

        /// <summary>A connection currently sits in this seat.</summary>
        public bool HasConnectedSeat(SeatId seat)
        {
            foreach (SeatId assigned in seatByConnection.Values)
            {
                if (assigned == seat)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A bound seat stays taken while its player is away.</summary>
        public bool IsSeatTaken(SeatId seat)
        {
            return HasConnectedSeat(seat) || Seats.IsBound(seat);
        }

        /// <summary>Seats a new connection in the first free seat (Host, then Guest).</summary>
        public bool TryTakeFreeSeat(ulong connectionId, out SeatId seat)
        {
            foreach (SeatId candidate in new[] { SeatId.Host, SeatId.Guest })
            {
                if (!IsSeatTaken(candidate) && TryTakeSeat(connectionId, candidate))
                {
                    seat = candidate;
                    return true;
                }
            }

            seat = default;
            return false;
        }

        public bool TryTakeSeat(ulong connectionId, SeatId seat)
        {
            if (seatByConnection.ContainsKey(connectionId) || HasConnectedSeat(seat))
            {
                return false;
            }

            seatByConnection.Add(connectionId, seat);
            return true;
        }

        /// <summary>A returning player sits in their held seat on a new connection.</summary>
        public void Reseat(ulong connectionId, SeatId seat)
        {
            seatByConnection[connectionId] = seat;
            lastCommandIdByConnection.Remove(connectionId);
        }

        public bool RemoveConnection(ulong connectionId, out SeatId seat)
        {
            if (!seatByConnection.TryGetValue(connectionId, out seat))
            {
                return false;
            }

            seatByConnection.Remove(connectionId);
            lastCommandIdByConnection.Remove(connectionId);
            return true;
        }

        public List<ulong> ConnectionsOf(SeatId seat)
        {
            var result = new List<ulong>();

            foreach (KeyValuePair<ulong, SeatId> pair in seatByConnection)
            {
                if (pair.Value == seat)
                {
                    result.Add(pair.Key);
                }
            }

            return result;
        }

        #endregion

        #region Commands

        /// <summary>
        /// Command IDs must grow per connection. A rejected domain command still consumes its
        /// ID so a client cannot replay the same request.
        /// </summary>
        public bool TryAcceptCommandId(ulong connectionId, ulong commandId)
        {
            if (lastCommandIdByConnection.TryGetValue(connectionId, out ulong last) && commandId <= last)
            {
                return false;
            }

            lastCommandIdByConnection[connectionId] = commandId;
            return true;
        }

        public ulong NextRevision()
        {
            return ++Revision;
        }

        #endregion

        #region Loadouts and Start

        public void SetLoadout(SeatId seat, string[] ownedCardIds)
        {
            loadoutBySeat[seat] = ownedCardIds ?? Array.Empty<string>();
        }

        public bool TryGetLoadout(SeatId seat, out string[] ownedCardIds)
        {
            return loadoutBySeat.TryGetValue(seat, out ownedCardIds);
        }

        public bool HasBothLoadouts => loadoutBySeat.ContainsKey(SeatId.Host) && loadoutBySeat.ContainsKey(SeatId.Guest);

        public void Start(MatchState state, MatchRulesEngine rules, bool ranked, long startedUtcMs)
        {
            if (IsStarted)
            {
                throw new InvalidOperationException($"Match {MatchId} already started.");
            }

            State = state ?? throw new ArgumentNullException(nameof(state));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            IsRanked = ranked;
            StartedUtcMs = startedUtcMs;
        }

        /// <summary>Returns true once: the caller then reports (or deliberately skips) the result.</summary>
        public bool TryBeginSettlement()
        {
            if (!IsFinished || IsSettlementStarted)
            {
                return false;
            }

            IsSettlementStarted = true;
            return true;
        }

        /// <summary>
        /// Listen-host only: a player left before the match was created, so the seats are freed
        /// for a new guest. Connections still seated stay in their seats.
        /// </summary>
        public void ResetUnstarted()
        {
            if (IsStarted)
            {
                throw new InvalidOperationException("A started match cannot be reset.");
            }

            Seats.Reset();
            loadoutBySeat.Clear();
            members.Clear();
            IsSealed = false;
        }

        #endregion
    }
}
