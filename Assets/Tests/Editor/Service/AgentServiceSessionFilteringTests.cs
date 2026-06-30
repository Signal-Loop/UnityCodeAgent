using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Settings;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class AgentServiceSessionFilteringTests
    {
        [Test]
        public async Task GetSessionsAsync_ReturnsOnlySessionsMatchingCurrentProjectIdentity()
        {
            var harness = new SessionFilteringHarness("C:/work/current");
            harness.Sessions.AddRange(new[]
            {
                CreateSession("UnityCodeAgentSession-20260630120000000-C__work_current"),
                CreateSession("UnityCodeAgentSession-20260630120000001-C__work_other"),
                CreateSession("session-1"),
                CreateSession("UnityCodeAgentSession-bad-C__work_current"),
            });

            var sessions = await harness.CreateService().GetSessionsAsync(harness.Context, CancellationToken.None);

            Assert.That(sessions.Select(session => session.SessionId), Is.EqualTo(new[] { "UnityCodeAgentSession-20260630120000000-C__work_current" }));
        }

        [Test]
        public async Task GetCurrentSessionAsync_OpensFirstSessionMatchingCurrentProjectIdentity()
        {
            var harness = new SessionFilteringHarness("C:/work/current");
            harness.Sessions.AddRange(new[]
            {
                CreateSession("UnityCodeAgentSession-20260630120000000-C__work_other"),
                CreateSession("UnityCodeAgentSession-20260630120000001-C__work_current"),
            });

            var session = await harness.CreateService().GetCurrentSessionAsync(harness.Context, CancellationToken.None);

            Assert.That(session.SessionId, Is.EqualTo("UnityCodeAgentSession-20260630120000001-C__work_current"));
            Assert.That(harness.OpenedSessionIds, Is.EqualTo(new[] { "UnityCodeAgentSession-20260630120000001-C__work_current" }));
        }

        [Test]
        public async Task OpenSendAndAbort_KeepUsingExplicitSessionIds()
        {
            var harness = new SessionFilteringHarness("C:/work/current");
            var service = harness.CreateService();

            await service.OpenSessionAsync(harness.Context, "external-session", CancellationToken.None);
            await service.SendPromptAsync(harness.Context, new SendAgentPromptRequestDto("other-project-session", "hello"), CancellationToken.None);
            await service.AbortPromptAsync(harness.Context, new AbortAgentPromptRequestDto("malformed-session"), CancellationToken.None);

            Assert.That(harness.OpenedSessionIds, Is.EqualTo(new[] { "external-session" }));
            Assert.That(harness.SentSessionIds, Is.EqualTo(new[] { "other-project-session" }));
            Assert.That(harness.AbortedSessionIds, Is.EqualTo(new[] { "malformed-session" }));
        }

        private static SessionSummaryDto CreateSession(string sessionId)
            => new SessionSummaryDto(sessionId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, sessionId);

        private sealed class SessionFilteringHarness
        {
            private readonly EndpointManifest _manifest = new EndpointManifest { Port = 5000, ServiceProcessId = -1 };

            public SessionFilteringHarness(string projectRoot)
            {
                var settings = UnityEngine.ScriptableObject.CreateInstance<UnityCodeAgentSettings>();
                settings.Model = new ModelInfoDto("gpt-5-mini", "GPT-5 Mini");
                Context = new UnityContext(
                    new UnityCodeAgentPaths(projectRoot),
                    ProviderConfigDto.Create(settings.Model, null, null, null, null),
                    string.Empty,
                    false,
                    false,
                    false,
                    true,
                    5007,
                    90,
                    UnityCodeAgentLogger.LogLevel.Info,
                    false,
                    UnityCodeAgentTelemetryMode.None,
                    string.Empty,
                    string.Empty,
                    false,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    UnityCodeAgentSettings.DefaultToolAssemblyNames,
                    Array.Empty<string>(),
                    string.Empty);
            }

            public UnityContext Context { get; }

            public List<SessionSummaryDto> Sessions { get; } = new List<SessionSummaryDto>();

            public List<string> OpenedSessionIds { get; } = new List<string>();

            public List<string> SentSessionIds { get; } = new List<string>();

            public List<string> AbortedSessionIds { get; } = new List<string>();

            public AgentService CreateService()
            {
                var bootstrap = new NoOpBootstrap(_manifest);
                return new AgentService(
                    bootstrap,
                    bootstrap,
                    (_, __) => new FakeApiClient(this),
                    (_, __) => throw new NotImplementedException(),
                    _ => _manifest);
            }
        }

        private sealed class NoOpBootstrap : IServiceBootstrap
        {
            private readonly EndpointManifest _manifest;

            public NoOpBootstrap(EndpointManifest manifest)
            {
                _manifest = manifest;
            }

            public Task<EndpointManifest> ConnectOrStartAsync(UnityContext context)
                => Task.FromResult(_manifest);
        }

        private sealed class FakeApiClient : IAgentServiceApiClient
        {
            private readonly SessionFilteringHarness _harness;

            public FakeApiClient(SessionFilteringHarness harness)
            {
                _harness = harness;
            }

            public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<SessionSummaryDto>>(_harness.Sessions);

            public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
            {
                _harness.OpenedSessionIds.Add(request.SessionId);
                return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));
            }

            public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _harness.SentSessionIds.Add(request.SessionId);
                return Task.CompletedTask;
            }

            public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _harness.AbortedSessionIds.Add(request.SessionId);
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
                => throw new NotImplementedException();

            public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
                => throw new NotImplementedException();

            public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
                => throw new NotImplementedException();
        }
    }
}
