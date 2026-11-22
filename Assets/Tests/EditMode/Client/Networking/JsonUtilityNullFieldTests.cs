using System;
using NUnit.Framework;
using UnityEngine;

namespace KLTN.Client.Tests.Networking
{
    /// <summary>
    /// Documents the transport rule MatchTurnClockRegistry.Normalize relies on: JsonUtility
    /// never yields null for a nested [Serializable] class, so "no turn window" must be
    /// encoded by an ID of 0 rather than by null.
    /// </summary>
    public sealed class JsonUtilityNullFieldTests
    {
        [Serializable]
        private sealed class Window
        {
            public ulong decisionId;
        }

        [Serializable]
        private sealed class Clock
        {
            public Window self;
            public Window opponent;
        }

        [Test]
        public void NullNestedClass_RoundTripsAsDefaultInstance()
        {
            var clock = new Clock { self = null, opponent = new Window { decisionId = 7 } };

            Clock received = JsonUtility.FromJson<Clock>(JsonUtility.ToJson(clock));

            Assert.IsNotNull(received.self);
            Assert.AreEqual(0UL, received.self.decisionId);
            Assert.AreEqual(7UL, received.opponent.decisionId);
        }
    }
}
