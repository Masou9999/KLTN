using System;
using System.Collections.Generic;

namespace KLTN.Game.ApplicationLayer
{
    /// <summary>
    /// The matches of one server process, keyed by match ID, plus the route from each
    /// transport connection to its match (SDS HLD 10.2, Figure 18). Capacity is configuration
    /// (MATCH_CAPACITY); a capacity of 1 is the original one-match-per-process server.
    /// </summary>
    public sealed class MatchSessionRegistry
    {
        #region State

        private readonly Dictionary<string, MatchSession> sessionsById =
            new Dictionary<string, MatchSession>(StringComparer.Ordinal);

        // Insertion order, so routing and capacity reports are deterministic.
        private readonly List<MatchSession> sessions = new List<MatchSession>();
        private readonly Dictionary<ulong, MatchSession> sessionByConnection = new Dictionary<ulong, MatchSession>();
        private readonly HashSet<string> knownMatchIds = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Construction

        public MatchSessionRegistry(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");
            }

            Capacity = capacity;
        }

        #endregion

        #region Properties

        public int Capacity { get; }

        public int Count => sessions.Count;

        public bool IsFull => sessions.Count >= Capacity;

        public bool IsEmpty => sessions.Count == 0;

        public IReadOnlyList<MatchSession> Sessions => sessions.ToArray();

        /// <summary>Every match this process ever hosted, so a finished match is never handed out again.</summary>
        public IReadOnlyCollection<string> KnownMatchIds => knownMatchIds;

        public bool IsKnown(string matchId)
        {
            return matchId != null && knownMatchIds.Contains(matchId);
        }

        /// <summary>Matches still occupying a slot (not closed).</summary>
        public List<string> ActiveMatchIds()
        {
            var result = new List<string>();

            foreach (MatchSession session in sessions)
            {
                if (!session.IsClosed)
                {
                    result.Add(session.MatchId);
                }
            }

            return result;
        }

        #endregion

        #region Sessions

        /// <summary>Adds a match; refused when the server is full or the ID was already used.</summary>
        public bool TryAdd(MatchSession session)
        {
            if (session == null || IsFull || knownMatchIds.Contains(session.MatchId))
            {
                return false;
            }

            sessionsById.Add(session.MatchId, session);
            sessions.Add(session);
            knownMatchIds.Add(session.MatchId);
            return true;
        }

        public bool TryGet(string matchId, out MatchSession session)
        {
            session = null;
            return matchId != null && sessionsById.TryGetValue(matchId, out session);
        }

        /// <summary>Frees the slot; connections routed to the match lose their route.</summary>
        public bool Remove(string matchId)
        {
            if (!TryGet(matchId, out MatchSession session))
            {
                return false;
            }

            sessionsById.Remove(matchId);
            sessions.Remove(session);

            foreach (ulong connectionId in new List<ulong>(sessionByConnection.Keys))
            {
                if (sessionByConnection[connectionId] == session)
                {
                    sessionByConnection.Remove(connectionId);
                }
            }

            return true;
        }

        #endregion

        #region Connection Routing

        public void Bind(ulong connectionId, MatchSession session)
        {
            if (session == null || !sessionsById.ContainsKey(session.MatchId))
            {
                throw new ArgumentException("The session is not registered.", nameof(session));
            }

            sessionByConnection[connectionId] = session;
        }

        public bool TryGetByConnection(ulong connectionId, out MatchSession session)
        {
            return sessionByConnection.TryGetValue(connectionId, out session);
        }

        public void Unbind(ulong connectionId)
        {
            sessionByConnection.Remove(connectionId);
        }

        /// <summary>
        /// Match a newly connecting player should join: the match the client named, else the
        /// match the player was allocated to, else the first open match that accepts anyone.
        /// Sealed or closed matches never take a new player. Null when none fits.
        /// </summary>
        public MatchSession FindForNewPlayer(string playerId, string requestedMatchId)
        {
            MatchSession allocated = null;
            MatchSession open = null;

            foreach (MatchSession session in sessions)
            {
                if (session.IsSealed || session.IsClosed || !session.Expects(playerId))
                {
                    continue;
                }

                if (session.IsNamed(requestedMatchId))
                {
                    return session;
                }

                if (session.HasExpectedPlayers)
                {
                    allocated ??= session;
                }
                else if (open == null && session.Members.Count < MatchSession.PlayersPerMatch)
                {
                    open = session;
                }
            }

            return allocated ?? open;
        }

        /// <summary>The match holding a seat this player may take back now (FR-MM-08).</summary>
        public MatchSession FindHeldSeat(string playerId, long nowMs)
        {
            foreach (MatchSession session in sessions)
            {
                if (session.IsSealed && !session.IsClosed && !session.IsFinished && session.Seats.CanReconnect(playerId, nowMs))
                {
                    return session;
                }
            }

            return null;
        }

        /// <summary>The player is already a member or seated in an open match of this server.</summary>
        public bool IsPlayerActive(string playerId)
        {
            foreach (MatchSession session in sessions)
            {
                if (session.IsClosed)
                {
                    continue;
                }

                if (session.HasMember(playerId)
                    || (session.Seats.TryFindSeat(playerId, out var seat)
                        && session.Seats.StateOf(seat) == SeatConnectionState.Connected))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
