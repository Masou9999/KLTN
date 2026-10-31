using NUnit.Framework;

namespace KLTN.Game.Domain.Tests
{
    public sealed class MatchAdmissionPolicyTests
    {
        private const string MatchId = "0123456789abcdef0123456789abcdef";

        [Test]
        public void FirstPlayer_IsAccepted()
        {
            AdmissionRejection result = MatchAdmissionPolicy.PreCheck(
                "player-a", "token", MatchId, MatchId, new string[0], 2, false
            );

            Assert.AreEqual(AdmissionRejection.None, result);
        }

        [Test]
        public void MissingIdentity_IsRejected()
        {
            Assert.AreEqual(
                AdmissionRejection.MissingPayload,
                MatchAdmissionPolicy.PreCheck("", "token", MatchId, MatchId, null, 2, false)
            );
            Assert.AreEqual(
                AdmissionRejection.MissingPayload,
                MatchAdmissionPolicy.PreCheck("player-a", null, MatchId, MatchId, null, 2, false)
            );
        }

        [Test]
        public void OtherMatch_IsRejected()
        {
            AdmissionRejection result = MatchAdmissionPolicy.PreCheck(
                "player-a", "token", "ffffffffffffffffffffffffffffffff", MatchId, null, 2, false
            );

            Assert.AreEqual(AdmissionRejection.WrongMatch, result);
        }

        [Test]
        public void MatchmakerIdWithDashes_MatchesServerId()
        {
            AdmissionRejection result = MatchAdmissionPolicy.PreCheck(
                "player-a", "token", "01234567-89AB-CDEF-0123-456789ABCDEF", MatchId, null, 2, false
            );

            Assert.AreEqual(AdmissionRejection.None, result);
        }

        [Test]
        public void ServerWithoutMatchId_SkipsMatchCheck()
        {
            AdmissionRejection result = MatchAdmissionPolicy.PreCheck(
                "player-a", "token", "anything", null, null, 2, false
            );

            Assert.AreEqual(AdmissionRejection.None, result);
        }

        [Test]
        public void ThirdPlayer_IsRejected()
        {
            AdmissionRejection result = MatchAdmissionPolicy.PreCheck(
                "player-c", "token", MatchId, MatchId, new[] { "player-a", "player-b" }, 2, false
            );

            Assert.AreEqual(AdmissionRejection.MatchFull, result);
        }

        [Test]
        public void SamePlayerTwice_IsRejected()
        {
            AdmissionRejection result = MatchAdmissionPolicy.PreCheck(
                "player-a", "token", MatchId, MatchId, new[] { "player-a" }, 2, false
            );

            Assert.AreEqual(AdmissionRejection.DuplicatePlayer, result);
        }

        [Test]
        public void LateJoin_AfterGameSceneLoaded_IsRejected()
        {
            AdmissionRejection result = MatchAdmissionPolicy.PreCheck(
                "player-a", "token", MatchId, MatchId, new string[0], 2, true
            );

            Assert.AreEqual(AdmissionRejection.MatchStarted, result);
        }

        [Test]
        public void EveryRejection_HasMessage()
        {
            foreach (AdmissionRejection rejection in System.Enum.GetValues(typeof(AdmissionRejection)))
            {
                if (rejection != AdmissionRejection.None)
                {
                    Assert.IsNotEmpty(MatchAdmissionPolicy.Describe(rejection), rejection.ToString());
                }
            }
        }
    }
}
