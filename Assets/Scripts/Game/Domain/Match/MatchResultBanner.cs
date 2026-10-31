namespace KLTN.Game.Domain
{
    public enum MatchResultBanner : byte
    {
        None = 0,
        Victory,
        Defeat,
        Draw,
    }

    /// <summary>Result title for one viewer; a draw has its own banner (FR-SURR-03).</summary>
    public static class MatchResultBannerPolicy
    {
        public static MatchResultBanner For(MatchOutcome outcome, SeatId viewer)
        {
            switch (outcome)
            {
                case MatchOutcome.Draw:
                    return MatchResultBanner.Draw;
                case MatchOutcome.HostWon:
                    return viewer == SeatId.Host ? MatchResultBanner.Victory : MatchResultBanner.Defeat;
                case MatchOutcome.GuestWon:
                    return viewer == SeatId.Guest ? MatchResultBanner.Victory : MatchResultBanner.Defeat;
                default:
                    return MatchResultBanner.None;
            }
        }

        /// <summary>Outcome name in the ReportResult payload ("HostWon"/"GuestWon"/"Draw").</summary>
        public static string WireName(MatchOutcome outcome)
        {
            switch (outcome)
            {
                case MatchOutcome.HostWon:
                    return "HostWon";
                case MatchOutcome.GuestWon:
                    return "GuestWon";
                case MatchOutcome.Draw:
                    return "Draw";
                default:
                    return null;
            }
        }
    }
}
