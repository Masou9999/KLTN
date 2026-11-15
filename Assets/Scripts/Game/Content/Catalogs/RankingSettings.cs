using UnityEngine;

namespace KLTN.Game.Content
{
    /// <summary>
    /// Inspector-tunable ranking settings (Resources/RankingSettings.asset).
    /// Season length and points lock reach Firestore through the UGS schedule payload:
    /// change them here, then recreate the season-tick schedule with the new payload
    /// (UGS schedules cannot be edited). The live values always come from config/season.
    /// </summary>
    [CreateAssetMenu(fileName = "RankingSettings", menuName = "KLTN/Ranking Settings")]
    public sealed class RankingSettings : ScriptableObject
    {
        public const string ResourcesPath = "RankingSettings";

        [Header("Season (Cloud Code)")]
        [Tooltip("Season length in hours. Takes effect from the next season.")]
        [Min(0.05f)]
        [SerializeField]
        private float seasonDurationHours = 24f;

        [Tooltip("Points lock before the season ends. Matches starting after the lock, or ending after the reset, are not scored.")]
        [Min(0)]
        [SerializeField]
        private int pointsLockMinutes = 30;

        [Tooltip("Cron for the UGS schedule (UGS minimum: hourly). Resets do not wait for it: seasons follow the clock.")]
        [SerializeField]
        private string scheduleCron = "0 * * * *";

        [Header("Host / Client Matches")]
        [InspectorName("Host Client Points And Rewards Enabled")]
        [Tooltip("Save rank points, coins and silver for Host/Client matches. Read once on the host when the match starts; changes apply to the next match. Find Random uses its server configuration.")]
        [SerializeField]
        private bool hostClientPointsEnabled = true;

        [Header("Leaderboard UI")]
        [Range(10, 100)]
        [SerializeField]
        private int pageSize = 50;

        private static RankingSettings cached;

        public float SeasonDurationHours => seasonDurationHours;
        public int PointsLockMinutes => pointsLockMinutes;
        public string ScheduleCron => scheduleCron;
        public bool HostClientPointsEnabled => hostClientPointsEnabled;
        public int PageSize => pageSize;

        /// <summary>Loads the asset; falls back to defaults so a missing asset never breaks a match.</summary>
        public static RankingSettings Load()
        {
            if (cached != null)
            {
                return cached;
            }

            cached = Resources.Load<RankingSettings>(ResourcesPath);

            if (cached == null)
            {
                Debug.LogWarning(
                    "[Ranking] Missing Resources/RankingSettings.asset; using defaults. "
                        + "Run Tools/KLTN/Build Leaderboard Scene + Main Menu Button."
                );
                cached = CreateInstance<RankingSettings>();
            }

            return cached;
        }
    }
}
