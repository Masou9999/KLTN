using System;
using KLTN.Infrastructure.Resilience;
using NUnit.Framework;

namespace KLTN.Client.Tests
{
    public sealed class NetworkRetryPolicyTests
    {
        private static readonly NetworkRetryPolicy Policy = NetworkRetryPolicy.Default;

        [Test]
        public void Default_AllowsThreeRetries()
        {
            Assert.AreEqual(4, Policy.MaxAttempts);
            Assert.IsTrue(Policy.ShouldRetry(3, true, true, 0, null));
            Assert.IsFalse(Policy.ShouldRetry(4, true, true, 0, null));
        }

        [TestCase(408)]
        [TestCase(429)]
        [TestCase(500)]
        [TestCase(502)]
        [TestCase(503)]
        [TestCase(504)]
        public void Idempotent_RetriesTransientStatus(long status)
        {
            Assert.IsTrue(Policy.ShouldRetry(1, true, false, status, null));
        }

        [Test]
        public void Idempotent_RetriesConnectionErrorAndTimeout()
        {
            Assert.IsTrue(Policy.ShouldRetry(1, true, true, 0, "NETWORK_ERROR"));
        }

        [TestCase(400, "INVALID_ARGUMENT")]
        [TestCase(401, "UNAUTHENTICATED")]
        [TestCase(403, "PERMISSION_DENIED")]
        [TestCase(404, "NOT_FOUND")]
        [TestCase(409, "ALREADY_EXISTS")]
        [TestCase(400, "FAILED_PRECONDITION")]
        [TestCase(501, "UNIMPLEMENTED")]
        public void Idempotent_DoesNotRetryPermanentErrors(long status, string errorStatus)
        {
            Assert.IsFalse(Policy.ShouldRetry(1, true, false, status, errorStatus));
        }

        [Test]
        public void Idempotent_RetriesAbortedTransaction()
        {
            Assert.IsTrue(Policy.ShouldRetry(1, true, false, 409, "ABORTED"));
        }

        [TestCase(500, null)]
        [TestCase(503, "UNAVAILABLE")]
        [TestCase(504, "DEADLINE_EXCEEDED")]
        public void NonIdempotent_DoesNotRetryWhenServerMayHaveApplied(long status, string errorStatus)
        {
            Assert.IsFalse(Policy.ShouldRetry(1, false, false, status, errorStatus));
        }

        [Test]
        public void NonIdempotent_DoesNotRetryLostResponse()
        {
            // A timed-out purchase commit may already have charged the player.
            Assert.IsFalse(Policy.ShouldRetry(1, false, true, 0, "NETWORK_ERROR"));
        }

        [TestCase(429, "RESOURCE_EXHAUSTED")]
        [TestCase(409, "ABORTED")]
        public void NonIdempotent_RetriesWhenRejectedUnapplied(long status, string errorStatus)
        {
            Assert.IsTrue(Policy.ShouldRetry(1, false, false, status, errorStatus));
        }

        [Test]
        public void None_NeverRetries()
        {
            Assert.IsFalse(NetworkRetryPolicy.None.ShouldRetry(1, true, true, 503, "UNAVAILABLE"));
        }

        [TestCase(0, 1000)]
        [TestCase(1, 2000)]
        [TestCase(2, 4000)]
        [TestCase(3, 8000)]
        [TestCase(10, 8000)]
        public void GetDelay_DoublesUpToMaxWithoutJitter(int retryIndex, double expectedMs)
        {
            Assert.AreEqual(expectedMs, Policy.GetDelay(retryIndex, 0).TotalMilliseconds, 0.001);
        }

        [Test]
        public void GetDelay_AddsBoundedJitter()
        {
            double max = Policy.GetDelay(1, 0.999999).TotalMilliseconds;

            Assert.Greater(max, 2000);
            Assert.LessOrEqual(max, 2500);
        }

        [Test]
        public void GetDelay_ClampsRandomOutsideRange()
        {
            Assert.AreEqual(1000, Policy.GetDelay(0, -5).TotalMilliseconds, 0.001);
            Assert.AreEqual(1250, Policy.GetDelay(0, 5).TotalMilliseconds, 0.001);
        }

        [Test]
        public void GetDelay_RejectsNegativeIndex()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Policy.GetDelay(-1, 0));
        }

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            TimeSpan second = TimeSpan.FromSeconds(1);

            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkRetryPolicy(0, second, second, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new NetworkRetryPolicy(2, second, TimeSpan.Zero, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkRetryPolicy(2, second, second, 2));
        }
    }
}
