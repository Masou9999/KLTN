using KLTN.Game.Networking;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Text shown in the three reward slots. A value the backend did not provide
    /// (unranked, failed or still unknown) is shown as <see cref="Missing"/>.
    /// </summary>
    public readonly struct MatchResultRewards
    {
        public const string Missing = "--";

        public MatchResultRewards(string coins, string silver, string points, string status)
        {
            Coins = coins;
            Silver = silver;
            Points = points;
            Status = status;
        }

        public string Coins { get; }
        public string Silver { get; }
        public string Points { get; }
        public string Status { get; }

        public static MatchResultRewards Unknown(string status = null)
        {
            return new MatchResultRewards(Missing, Missing, Missing, status);
        }

        /// <summary>Formats a settlement; only a saved settlement carries reward values.</summary>
        public static MatchResultRewards From(MatchSettlementDto settlement)
        {
            if (settlement == null) return Unknown();
            switch ((MatchSettlementState)settlement.state)
            {
                case MatchSettlementState.PrivateMatch:
                    return Unknown(string.IsNullOrEmpty(settlement.message)
                        ? "Trận riêng: không tính thưởng/điểm."
                        : settlement.message);
                case MatchSettlementState.Failed:
                    return Unknown("Lưu kết quả thất bại" + (string.IsNullOrEmpty(settlement.message)
                        ? "."
                        : ": " + settlement.message));
                case MatchSettlementState.Saving:
                    return Unknown();
            }

            string points = settlement.ranked
                ? Signed(settlement.pointsAfter - settlement.pointsBefore)
                : Missing;
            string status = null;
            if (settlement.result == "surrender_early")
                status = "Đầu hàng trước vòng 3: không nhận thưởng.";
            else if (!settlement.ranked && !string.IsNullOrEmpty(settlement.notScoredReason))
                status = settlement.notScoredReason;
            return new MatchResultRewards(
                "+" + settlement.coins, "+" + settlement.silver, points, status);
        }

        private static string Signed(int value)
        {
            return value >= 0 ? "+" + value : value.ToString();
        }
    }
}
