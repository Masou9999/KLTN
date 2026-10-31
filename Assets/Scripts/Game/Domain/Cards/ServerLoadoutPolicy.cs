using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    /// <summary>
    /// Chooses the card IDs a seat may build its deck from (FR-MATCH-02, FR-MATCH-11).
    /// The backend's owned list is authoritative; a client report can only narrow it.
    /// </summary>
    public static class ServerLoadoutPolicy
    {
        #region Public API

        /// <param name="serverOwned">Owned IDs returned by the backend, or null when unverified (listen-host).</param>
        /// <param name="clientReported">IDs the client sent, or null/empty when it sent nothing.</param>
        public static IReadOnlyList<string> Resolve(
            IReadOnlyCollection<string> serverOwned,
            IReadOnlyCollection<string> clientReported
        )
        {
            if (serverOwned == null)
            {
                return Distinct(clientReported, null);
            }

            if (clientReported == null || clientReported.Count == 0)
            {
                return Distinct(serverOwned, null);
            }

            var reported = new HashSet<string>(StringComparer.Ordinal);

            foreach (string raw in clientReported)
            {
                string id = raw?.Trim();

                if (!string.IsNullOrEmpty(id))
                {
                    reported.Add(id);
                }
            }

            return Distinct(serverOwned, reported);
        }

        #endregion

        #region Helpers

        private static List<string> Distinct(IEnumerable<string> ids, HashSet<string> filter)
        {
            var result = new List<string>();

            if (ids == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string raw in ids)
            {
                string id = raw?.Trim();

                if (string.IsNullOrEmpty(id) || (filter != null && !filter.Contains(id)) || !seen.Add(id))
                {
                    continue;
                }

                result.Add(id);
            }

            return result;
        }

        #endregion
    }
}
