using System;
using UnityEngine;

namespace KLTN.Infrastructure.CloudCode
{
    /// <summary>Chooses the reporter for a dedicated server from its environment.</summary>
    public static class MatchResultReporterFactory
    {
        #region Environment Variables

        public const string MatchTokenVariable = "MATCH_TOKEN";
        public const string ProjectIdVariable = "UGS_PROJECT_ID";
        public const string EnvironmentIdVariable = "UGS_ENVIRONMENT_ID";
        public const string KeyIdVariable = "UGS_SERVICE_ACCOUNT_KEY_ID";
        public const string SecretVariable = "UGS_SERVICE_ACCOUNT_SECRET";

        /// <summary>"1" forces <see cref="StubMatchResultReporter"/> (local testing only).</summary>
        public const string StubVariable = "MATCH_REPORTER_STUB";

        #endregion

        public static IMatchResultReporter Create(bool forceStub)
        {
            if (forceStub || Environment.GetEnvironmentVariable(StubVariable) == "1")
            {
                Debug.LogWarning("[MatchServer] Using StubMatchResultReporter: results are NOT saved.");
                return new StubMatchResultReporter();
            }

            string projectId = Read(ProjectIdVariable);
            string environmentId = Read(EnvironmentIdVariable);
            string keyId = Read(KeyIdVariable);
            string secret = Read(SecretVariable);
            string matchToken = Read(MatchTokenVariable);

            if (projectId == null || environmentId == null || keyId == null || secret == null || matchToken == null)
            {
                Debug.LogWarning(
                    "[MatchServer] UGS service account or MATCH_TOKEN missing: match is unranked "
                        + "(NullMatchResultReporter, cards not verified)."
                );
                return new NullMatchResultReporter();
            }

            Debug.Log($"[MatchServer] Reporting results to Cloud Code project {projectId}.");
            return new CloudCodeMatchResultReporter(projectId, environmentId, keyId, secret, matchToken);
        }

        private static string Read(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
