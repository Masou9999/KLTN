using System.Collections.Generic;
using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class ServerLoadoutPolicyTests
    {
        [Test]
        public void ServerList_IsUsedWhenClientSendsNothing()
        {
            IReadOnlyList<string> result = ServerLoadoutPolicy.Resolve(new[] { "a", "b", "c" }, null);

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, result);
        }

        [Test]
        public void ClientReport_OnlyNarrowsServerList()
        {
            IReadOnlyList<string> result = ServerLoadoutPolicy.Resolve(
                new[] { "a", "b", "c" },
                new[] { "c", "a", "stolen" }
            );

            CollectionAssert.AreEqual(new[] { "a", "c" }, result);
        }

        [Test]
        public void ClientCannotAddUnownedCards()
        {
            IReadOnlyList<string> result = ServerLoadoutPolicy.Resolve(new[] { "a" }, new[] { "x", "y" });

            CollectionAssert.IsEmpty(result);
        }

        [Test]
        public void EmptyClientReport_FallsBackToServerList()
        {
            IReadOnlyList<string> result = ServerLoadoutPolicy.Resolve(new[] { "a", "b" }, new string[0]);

            CollectionAssert.AreEqual(new[] { "a", "b" }, result);
        }

        [Test]
        public void Unverified_TrustsClientReport()
        {
            IReadOnlyList<string> result = ServerLoadoutPolicy.Resolve(null, new[] { "a", " b ", "a", "" });

            CollectionAssert.AreEqual(new[] { "a", "b" }, result);
        }

        [Test]
        public void Duplicates_AreRemoved()
        {
            IReadOnlyList<string> result = ServerLoadoutPolicy.Resolve(new[] { "a", "a", "b" }, null);

            CollectionAssert.AreEqual(new[] { "a", "b" }, result);
        }
    }
}
