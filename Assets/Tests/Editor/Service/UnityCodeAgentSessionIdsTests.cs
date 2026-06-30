using System;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Infrastructure;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class UnityCodeAgentSessionIdsTests
    {
        [Test]
        public void Create_UsesExistingReadableProjectRootFormat()
        {
            var sessionId = UnityCodeAgentSessionIds.Create(
                new UnityCodeAgentPaths("C:/work/My Project"),
                new DateTimeOffset(2026, 6, 30, 12, 13, 14, 567, TimeSpan.Zero));

            Assert.That(sessionId, Is.EqualTo("UnityCodeAgentSession-20260630121314567-C__work_My_Project"));
        }

        [Test]
        public void TryParse_ExtractsTimestampAndProjectIdentity()
        {
            var parsed = UnityCodeAgentSessionIds.TryParse(
                "UnityCodeAgentSession-20260630121314567-C__work_My_Project",
                out var parts);

            Assert.That(parsed, Is.True);
            Assert.That(parts.TimestampUtc, Is.EqualTo(new DateTimeOffset(2026, 6, 30, 12, 13, 14, 567, TimeSpan.Zero)));
            Assert.That(parts.SanitizedProjectRoot, Is.EqualTo("C__work_My_Project"));
        }

        [TestCase("")]
        [TestCase("session-1")]
        [TestCase("UnityCodeAgentSession-20260630121314567")]
        [TestCase("UnityCodeAgentSession-not-a-timestamp-C__work")]
        [TestCase("UnityCodeAgentSession-20260630121314567-")]
        public void TryParse_RejectsMalformedOrNonUnityIds(string sessionId)
        {
            Assert.That(UnityCodeAgentSessionIds.TryParse(sessionId, out _), Is.False);
        }
    }
}
