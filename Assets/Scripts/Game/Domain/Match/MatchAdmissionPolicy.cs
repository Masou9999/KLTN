using System;
using System.Collections.Generic;

namespace KLTN.Game.Domain
{
    public enum AdmissionRejection : byte
    {
        None = 0,
        MissingPayload,
        WrongMatch,
        MatchFull,
        MatchStarted,
        DuplicatePlayer,
        CannotRejoin,
    }

    /// <summary>
    /// Checks a dedicated-server connection request before the backend verifies the player's
    /// token (FR-MM-09). Only the two players allocated to this match may join, and only
    /// before GameScene is loaded; afterwards only a player whose seat is being held may
    /// come back (<see cref="PreCheckRejoin"/>, FR-MM-08).
    /// </summary>
    public static class MatchAdmissionPolicy
    {
        #region Public API

        /// <param name="knownPlayerIds">Players already admitted or still being verified.</param>
        public static AdmissionRejection PreCheck(
            string playerId,
            string accessToken,
            string payloadMatchId,
            string serverMatchId,
            IReadOnlyCollection<string> knownPlayerIds,
            int capacity,
            bool matchStarted
        )
        {
            if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(accessToken))
            {
                return AdmissionRejection.MissingPayload;
            }

            string expected = NormalizeMatchId(serverMatchId);

            if (expected.Length > 0 && NormalizeMatchId(payloadMatchId) != expected)
            {
                return AdmissionRejection.WrongMatch;
            }

            if (matchStarted)
            {
                return AdmissionRejection.MatchStarted;
            }

            int known = knownPlayerIds?.Count ?? 0;

            if (knownPlayerIds != null)
            {
                foreach (string knownId in knownPlayerIds)
                {
                    if (string.Equals(knownId, playerId, StringComparison.Ordinal))
                    {
                        return AdmissionRejection.DuplicatePlayer;
                    }
                }
            }

            return known >= capacity ? AdmissionRejection.MatchFull : AdmissionRejection.None;
        }

        /// <summary>
        /// Connection request after the match started: only the player whose seat is still
        /// held may rejoin (FR-MM-08). The backend verifies the token again afterwards.
        /// </summary>
        /// <param name="seatHeldForPlayer">The server is holding a seat for this player.</param>
        /// <param name="verifyingPlayerIds">Players whose verification is still running.</param>
        public static AdmissionRejection PreCheckRejoin(
            string playerId,
            string accessToken,
            bool seatHeldForPlayer,
            IReadOnlyCollection<string> verifyingPlayerIds
        )
        {
            if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(accessToken))
            {
                return AdmissionRejection.MissingPayload;
            }

            if (verifyingPlayerIds != null)
            {
                foreach (string verifyingId in verifyingPlayerIds)
                {
                    if (string.Equals(verifyingId, playerId, StringComparison.Ordinal))
                    {
                        return AdmissionRejection.DuplicatePlayer;
                    }
                }
            }

            return seatHeldForPlayer ? AdmissionRejection.None : AdmissionRejection.CannotRejoin;
        }

        /// <summary>Matchmaker IDs may contain dashes; MATCH_ID uses the 32-character form.</summary>
        public static string NormalizeMatchId(string matchId)
        {
            if (string.IsNullOrWhiteSpace(matchId))
            {
                return string.Empty;
            }

            return matchId.Trim().Replace("-", string.Empty).ToLowerInvariant();
        }

        public static string Describe(AdmissionRejection rejection)
        {
            switch (rejection)
            {
                case AdmissionRejection.MissingPayload:
                    return "Thiếu thông tin đăng nhập khi vào trận.";
                case AdmissionRejection.WrongMatch:
                    return "Bạn không thuộc trận đấu này.";
                case AdmissionRejection.MatchFull:
                    return "Trận đấu đã đủ người.";
                case AdmissionRejection.MatchStarted:
                    return "Trận đấu đã bắt đầu.";
                case AdmissionRejection.DuplicatePlayer:
                    return "Tài khoản này đã ở trong trận.";
                case AdmissionRejection.CannotRejoin:
                    return "Không thể vào lại trận: đã hết thời gian giữ chỗ hoặc trận đã kết thúc.";
                default:
                    return string.Empty;
            }
        }

        #endregion
    }
}
