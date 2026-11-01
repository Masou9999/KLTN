using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KLTN.Infrastructure.CloudCode
{
    /// <summary>A match the allocator placed on this running server (FR-MM-10).</summary>
    public sealed class MatchAssignment
    {
        public string MatchId { get; set; }

        /// <summary>Matchmaker match ID the clients know; used to route their connection.</summary>
        public string MatchmakerMatchId { get; set; }

        /// <summary>Fresh MATCH_TOKEN for this match; only this server receives it.</summary>
        public string MatchToken { get; set; }

        public IReadOnlyList<string> PlayerIds { get; set; } = Array.Empty<string>();
    }

    /// <summary>What a multi-match server tells the backend on each heartbeat.</summary>
    public sealed class ServerHeartbeat
    {
        public string ServerId { get; set; }
        public string ServerToken { get; set; }
        public int Capacity { get; set; }

        /// <summary>Every match this process ever hosted, so none is handed out twice.</summary>
        public IReadOnlyCollection<string> KnownMatchIds { get; set; } = Array.Empty<string>();

        /// <summary>Matches still occupying a slot.</summary>
        public IReadOnlyCollection<string> ActiveMatchIds { get; set; } = Array.Empty<string>();

        /// <summary>The server is about to quit; the allocator must not place matches on it.</summary>
        public bool Draining { get; set; }
    }

    public sealed class AssignmentPoll
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        public IReadOnlyList<MatchAssignment> Matches { get; private set; } = Array.Empty<MatchAssignment>();

        public static AssignmentPoll Ok(IReadOnlyList<MatchAssignment> matches)
        {
            return new AssignmentPoll { Success = true, Matches = matches ?? Array.Empty<MatchAssignment>() };
        }

        public static AssignmentPoll Fail(string message)
        {
            return new AssignmentPoll { Success = false, Message = message };
        }
    }

    /// <summary>
    /// Backend port of a server hosting several matches (SDS HLD 10.2): the heartbeat reports
    /// capacity and running matches, and the answer carries the matches newly placed here.
    /// Implemented by <see cref="CloudCodeMatchResultReporter"/> (MatchServerModule.GetAssignedMatches).
    /// </summary>
    public interface IMatchAssignmentSource
    {
        Task<AssignmentPoll> GetAssignedMatchesAsync(ServerHeartbeat heartbeat, CancellationToken cancellationToken);
    }
}
