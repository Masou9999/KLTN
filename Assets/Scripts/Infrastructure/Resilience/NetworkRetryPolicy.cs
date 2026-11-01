using System;

namespace KLTN.Infrastructure.Resilience
{
    /// <summary>
    /// Bounded retry with exponential backoff for REST calls (FR-NET-08).
    /// Pure decision logic so it can be unit tested without UnityWebRequest.
    /// </summary>
    public sealed class NetworkRetryPolicy
    {
        /// <summary>1 attempt + 3 retries, waiting ~1 s, 2 s, 4 s (+ up to 25 % jitter).</summary>
        public static readonly NetworkRetryPolicy Default = new(4, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8), 0.25);

        /// <summary>Single attempt, for calls that must never be repeated.</summary>
        public static readonly NetworkRetryPolicy None = new(1, TimeSpan.Zero, TimeSpan.Zero, 0);

        public int MaxAttempts { get; }
        public TimeSpan BaseDelay { get; }
        public TimeSpan MaxDelay { get; }
        public double JitterFraction { get; }

        public NetworkRetryPolicy(int maxAttempts, TimeSpan baseDelay, TimeSpan maxDelay, double jitterFraction)
        {
            if (maxAttempts < 1) { throw new ArgumentOutOfRangeException(nameof(maxAttempts)); }

            if (baseDelay < TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(baseDelay)); }

            if (maxDelay < baseDelay) { throw new ArgumentOutOfRangeException(nameof(maxDelay)); }

            if (jitterFraction < 0 || jitterFraction > 1) { throw new ArgumentOutOfRangeException(nameof(jitterFraction)); }

            MaxAttempts = maxAttempts;
            BaseDelay = baseDelay;
            MaxDelay = maxDelay;
            JitterFraction = jitterFraction;
        }

        #region Decisions

        /// <summary>
        /// Whether another attempt should be made after <paramref name="attemptsMade"/> failed attempts.
        /// Idempotent requests retry on any transient failure. Other requests (e.g. a purchase commit)
        /// retry only when the server certainly did not apply them, so a lost response never charges twice.
        /// </summary>
        public bool ShouldRetry(int attemptsMade, bool idempotent, bool connectionError, long statusCode, string errorStatus)
        {
            if (attemptsMade >= MaxAttempts) { return false; }

            return idempotent
                ? IsTransientFailure(connectionError, statusCode, errorStatus)
                : IsRejectedBeforeProcessing(statusCode, errorStatus);
        }

        /// <summary>Network error, timeout, throttling or a temporary server error.</summary>
        public static bool IsTransientFailure(bool connectionError, long statusCode, string errorStatus)
        {
            if (connectionError) { return true; }

            switch (statusCode)
            {
                case 408:
                case 429:
                case 500:
                case 502:
                case 503:
                case 504:
                    return true;
            }

            return errorStatus == "ABORTED" || errorStatus == "UNAVAILABLE" || errorStatus == "DEADLINE_EXCEEDED";
        }

        /// <summary>
        /// Failures where the server rejected the request without applying it:
        /// quota throttling (429 / RESOURCE_EXHAUSTED) or an aborted transaction (ABORTED).
        /// </summary>
        public static bool IsRejectedBeforeProcessing(long statusCode, string errorStatus)
        {
            return statusCode == 429 || errorStatus == "RESOURCE_EXHAUSTED" || errorStatus == "ABORTED";
        }

        #endregion

        #region Backoff

        /// <summary>
        /// Wait before retry number <paramref name="retryIndex"/> (0 = first retry):
        /// BaseDelay * 2^retryIndex, capped at MaxDelay, plus jitter in [0, JitterFraction) of that delay.
        /// </summary>
        /// <param name="random01">Uniform random value in [0, 1).</param>
        public TimeSpan GetDelay(int retryIndex, double random01)
        {
            if (retryIndex < 0) { throw new ArgumentOutOfRangeException(nameof(retryIndex)); }

            double baseMs = BaseDelay.TotalMilliseconds;
            double maxMs = MaxDelay.TotalMilliseconds;

            // Clamp the exponent so large indices cannot overflow.
            double delayMs = Math.Min(maxMs, baseMs * Math.Pow(2, Math.Min(retryIndex, 30)));
            double clampedRandom = Math.Max(0, Math.Min(random01, 1));
            delayMs += delayMs * JitterFraction * clampedRandom;
            return TimeSpan.FromMilliseconds(delayMs);
        }

        #endregion
    }
}
