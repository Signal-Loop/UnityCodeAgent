using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Settings;
using UnityEngine.TestTools;

// Test file goal: verify AgentService reconnects to a restarted service and uses the restarted manifest port.
// Scope: restart recovery across the Unity service facade using fake bootstrap, manifest, HTTP API, and SSE stream dependencies.
// Boundaries: excludes the real service process, manifest files on disk, and live loopback networking.

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class AgentServiceRestartRecoveryTests
    {
        [Test]
        [Description("Goal: verify CreateSessionAsync retries after a restart and uses the restarted manifest port. Scope: AgentService restart recovery only. Boundaries: excludes the real bootstrap process, disk-backed manifest loading, and real HTTP transport.")]
        public async Task CreateSessionAsync_RestartsAndUsesNewManifestPort()
        {
            var harness = new RestartHarness();
            var service = harness.CreateService();

            var response = await service.CreateSessionAsync(harness.Context, CancellationToken.None);

            Assert.That(response.SessionId, Does.Match(@"^UnityCodeAgentSession-[0-9]{17}-C__work$"));
            Assert.That(harness.Bootstrap.StartCallCount, Is.EqualTo(1));
            Assert.That(harness.ApiPortsUsed, Is.EqualTo(new[] { 5000, 5001 }));
        }

        [Test]
        [Description("Goal: verify CreateSessionAsync embeds a readable sanitized project root in the SDK session id. Scope: AgentService session id generation only. Boundaries: excludes the real bootstrap process and transport retries.")]
        public async Task CreateSessionAsync_UsesReadableSafeProjectRootInSessionId()
        {
            var projectRoot = "C:/work/My Project";
            var harness = new RestartHarness(projectRoot: projectRoot);
            var service = harness.CreateService();

            var response = await service.CreateSessionAsync(harness.Context, CancellationToken.None);

            Assert.That(new UnityCodeAgentPaths(projectRoot).SanitizedProjectRoot, Is.EqualTo("C__work_My_Project"));
            Assert.That(response.SessionId, Does.Match(@"^UnityCodeAgentSession-[0-9]{17}-C__work_My_Project$"));
            Assert.That(response.SessionId, Does.Not.Contain(projectRoot));
        }

        [Test]
        [Description("Goal: verify GetModelsAsync retries after a restart and uses the restarted manifest port. Scope: AgentService restart recovery only. Boundaries: excludes the real bootstrap process, manifest files, and live model discovery.")]
        public async Task GetModelsAsync_RestartsAndUsesNewManifestPort()
        {
            var harness = new RestartHarness();
            var service = harness.CreateService();

            var response = await service.GetModelsAsync(harness.Context, new ListAgentModelsRequestDto(), CancellationToken.None);

            Assert.That(response.Count, Is.EqualTo(1));
            Assert.That(response[0].Id, Is.EqualTo("gpt-5-mini"));
            Assert.That(harness.Bootstrap.StartCallCount, Is.EqualTo(1));
            Assert.That(harness.ApiPortsUsed, Is.EqualTo(new[] { 5000, 5001 }));
        }

        [Test]
        [Description("Goal: verify SendPromptAsync retries after a restart and uses the restarted manifest port. Scope: AgentService restart recovery only. Boundaries: excludes real prompt execution, queueing, and service-side event emission.")]
        public async Task SendPromptAsync_RestartsAndUsesNewManifestPort()
        {
            var harness = new RestartHarness();
            var service = harness.CreateService();

            await service.SendPromptAsync(harness.Context, new SendAgentPromptRequestDto("session-1", "hello"), CancellationToken.None);

            Assert.That(harness.Bootstrap.StartCallCount, Is.EqualTo(1));
            Assert.That(harness.ApiPortsUsed, Is.EqualTo(new[] { 5000, 5001 }));
            Assert.That(harness.ApiOperations, Is.EqualTo(new[] { "send:5000:session-1", "open:5001:session-1", "send:5001:session-1" }));
        }

        [Test]
        [Description("Goal: verify SendPromptAsync opens an unavailable session before retrying when the service is alive but has no attached session. Scope: AgentService session recovery only. Boundaries: excludes process restart and real HTTP transport.")]
        public async Task SendPromptAsync_ReopensUnavailableSessionBeforeRetrying()
        {
            var harness = new RestartHarness(staleInitialTransport: false);
            var service = harness.CreateService();

            await service.SendPromptAsync(harness.Context, new SendAgentPromptRequestDto("session-1", "hello"), CancellationToken.None);

            Assert.That(harness.Bootstrap.StartCallCount, Is.EqualTo(0));
            Assert.That(harness.ApiPortsUsed, Is.EqualTo(new[] { 5000 }));
            Assert.That(harness.ApiOperations, Is.EqualTo(new[] { "send:5000:session-1", "open:5000:session-1", "send:5000:session-1" }));
        }

        [UnityTest]
        [Description("Goal: verify session recovery can build Unity-backed open-session requests when recovery resumes from a background thread. Scope: AgentService editor-thread request construction. Boundaries: excludes real HTTP transport and process startup.")]
        public IEnumerator SendPromptAsync_ReopensUnavailableSessionFromBackgroundThread()
        {
            var harness = new RestartHarness(staleInitialTransport: false);
            var service = harness.CreateService();
            var task = Task.Run(() => service.SendPromptAsync(harness.Context, new SendAgentPromptRequestDto("session-1", "hello"), CancellationToken.None));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);

            while (!task.IsCompleted && DateTimeOffset.UtcNow < deadline)
            {
                yield return null;
            }

            Assert.That(task.IsCompleted, Is.True, "Background recovery did not complete before timeout.");
            if (task.IsFaulted)
            {
                throw task.Exception.InnerException ?? task.Exception;
            }

            Assert.That(harness.ApiPortsUsed, Is.EqualTo(new[] { 5000 }));
            Assert.That(harness.ApiOperations, Is.EqualTo(new[] { "send:5000:session-1", "open:5000:session-1", "send:5000:session-1" }));
        }

        [Test]
        [Description("Goal: verify AbortPromptAsync retries after a restart and uses the restarted manifest port. Scope: AgentService restart recovery only. Boundaries: excludes real runtime cancellation behavior and external process lifecycle.")]
        public async Task AbortPromptAsync_RestartsAndUsesNewManifestPort()
        {
            var harness = new RestartHarness();
            var service = harness.CreateService();

            await service.AbortPromptAsync(harness.Context, new AbortAgentPromptRequestDto("session-1"), CancellationToken.None);

            Assert.That(harness.Bootstrap.StartCallCount, Is.EqualTo(1));
            Assert.That(harness.ApiPortsUsed, Is.EqualTo(new[] { 5000, 5001 }));
            Assert.That(harness.ApiOperations, Is.EqualTo(new[] { "abort:5000:session-1", "open:5001:session-1", "abort:5001:session-1" }));
        }

        [Test]
        [Description("Goal: verify explicit RestartAsync stops the current manifest before starting through the existing bootstrap path. Scope: AgentService manual restart only. Boundaries: excludes real process termination and live service startup.")]
        public async Task RestartAsync_StopsBeforeStartingThroughBootstrap()
        {
            var harness = new RestartHarness();
            var operations = new List<string>();
            var bootstrap = new RecordingServiceBootstrap(new EndpointManifest { Port = 5001, ServiceProcessId = 101 }, operations);
            var service = new AgentService(
                bootstrap,
                bootstrap,
                (_, __) => throw new NotImplementedException(),
                (_, __) => throw new NotImplementedException(),
                _ =>
                {
                    operations.Add("load-manifest");
                    return new EndpointManifest { Port = 5000, ServiceProcessId = int.MaxValue };
                },
                harness.ProgressMessages.Add);

            var manifest = await service.RestartAsync(harness.Context, CancellationToken.None);

            Assert.That(manifest.Port, Is.EqualTo(5001));
            Assert.That(bootstrap.StartCallCount, Is.EqualTo(1));
            Assert.That(operations, Is.EqualTo(new[] { "load-manifest", "start" }));
            Assert.That(harness.ProgressMessages, Does.Contain("Restarting agent service..."));
            Assert.That(harness.ProgressMessages, Does.Contain("Starting agent service..."));
        }

        [Test]
        [Description("Goal: verify manual restart is skipped instead of queued when automatic start is already in progress. Scope: AgentService lifecycle gating only. Boundaries: excludes ServiceBootstrap process launching and live health checks.")]
        public async Task RestartAsync_DoesNotQueueBehindConcurrentStartAsync()
        {
            var harness = new RestartHarness();
            var operations = new List<string>();
            var bootstrap = new BlockingServiceBootstrap(operations);
            var progressMessages = new List<string>();
            var service = new AgentService(
                bootstrap,
                bootstrap,
                (_, __) => throw new NotImplementedException(),
                (_, __) => throw new NotImplementedException(),
                _ =>
                {
                    operations.Add("load-manifest");
                    return new EndpointManifest { Port = 5000, ServiceProcessId = -1 };
                },
                progressMessages.Add);

            var startTask = service.StartAsync(harness.Context);
            Assert.That(await CompletesWithinAsync(bootstrap.FirstCallEntered.Task, TimeSpan.FromSeconds(2)), Is.True, "Initial start did not enter bootstrap.");

            var skippedRestartManifest = await service.RestartAsync(harness.Context, CancellationToken.None);

            Assert.That(skippedRestartManifest, Is.Null);
            Assert.That(bootstrap.StartCallCount, Is.EqualTo(1), "Restart reached bootstrap while StartAsync still held the lifecycle gate.");
            Assert.That(bootstrap.MaxConcurrentCalls, Is.EqualTo(1));
            Assert.That(progressMessages, Does.Contain("Agent service restart is already in progress."));

            bootstrap.ReleaseFirstCall();
            await startTask;

            Assert.That(bootstrap.StartCallCount, Is.EqualTo(1));
            Assert.That(bootstrap.MaxConcurrentCalls, Is.EqualTo(1));
            Assert.That(operations, Is.EqualTo(new[] { "start-enter:1", "start-exit:1" }));
        }

        [Test]
        [Description("Goal: verify repeated manual restart requests are skipped instead of queued while a restart is already in progress. Scope: AgentService restart debounce only. Boundaries: excludes real process termination and live service startup.")]
        public async Task RestartAsync_DoesNotQueueBehindRestartInProgress()
        {
            var harness = new RestartHarness();
            var operations = new List<string>();
            var bootstrap = new BlockingServiceBootstrap(operations);
            var progressMessages = new List<string>();
            var service = new AgentService(
                bootstrap,
                bootstrap,
                (_, __) => throw new NotImplementedException(),
                (_, __) => throw new NotImplementedException(),
                _ =>
                {
                    operations.Add("load-manifest");
                    return new EndpointManifest { Port = 5000, ServiceProcessId = -1 };
                },
                progressMessages.Add);

            var restartTask = service.RestartAsync(harness.Context, CancellationToken.None);
            Assert.That(await CompletesWithinAsync(bootstrap.FirstCallEntered.Task, TimeSpan.FromSeconds(2)), Is.True, "Initial restart did not enter bootstrap.");

            var skippedRestartManifest = await service.RestartAsync(harness.Context, CancellationToken.None);

            Assert.That(skippedRestartManifest, Is.Null);
            Assert.That(bootstrap.StartCallCount, Is.EqualTo(1), "Second restart reached bootstrap while the first restart still held the lifecycle gate.");
            Assert.That(bootstrap.MaxConcurrentCalls, Is.EqualTo(1));
            Assert.That(progressMessages, Does.Contain("Agent service restart is already in progress."));

            bootstrap.ReleaseFirstCall();
            await restartTask;

            Assert.That(bootstrap.StartCallCount, Is.EqualTo(1));
            Assert.That(bootstrap.MaxConcurrentCalls, Is.EqualTo(1));
            Assert.That(operations, Is.EqualTo(new[] { "load-manifest", "start-enter:1", "start-exit:1" }));
        }

        [Test]
        [Description("Goal: verify StreamEventsAsync reconnects after restart and resumes on the restarted manifest port using Last-Event-ID replay. Scope: AgentService stream recovery only. Boundaries: excludes the real SSE transport, broker, and long-running editor integration.")]
        public async Task StreamEventsAsync_RestartsAndUsesNewManifestPortWithReplayCursor()
        {
            var harness = new RestartHarness();
            var service = harness.CreateService();
            var receivedEvents = new List<AgentServiceEventEnvelope>();
            long? lastAcceptedSequenceNumber = null;
            using var cancellation = new CancellationTokenSource();

            await service.StreamEventsAsync(harness.Context, envelope =>
            {
                receivedEvents.Add(envelope);
                if (envelope.SequenceNumber > 0)
                {
                    lastAcceptedSequenceNumber = Math.Max(lastAcceptedSequenceNumber ?? 0, envelope.SequenceNumber);
                }
                if (envelope.SequenceNumber == 42)
                {
                    cancellation.Cancel();
                }
            }, cancellation.Token, () => lastAcceptedSequenceNumber);

            Assert.That(receivedEvents.Select(e => e.SequenceNumber), Is.EqualTo(new long[] { 41, 0, 42 }));
            Assert.That(receivedEvents[1].Type, Is.EqualTo(AgentEventType.SessionStatusChanged));
            Assert.That(receivedEvents[1].Content, Is.EqualTo("ready"));
            Assert.That(receivedEvents[0].SequenceNumber, Is.EqualTo(41));
            Assert.That(receivedEvents[2].SequenceNumber, Is.EqualTo(42));
            Assert.That(harness.ProgressMessages, Does.Contain("Agent service connection lost. Restarting service connection..."));
            Assert.That(harness.ProgressMessages, Does.Contain("Agent service connection restored."));
            Assert.That(harness.Bootstrap.StartCallCount, Is.EqualTo(1));
            Assert.That(harness.StreamPortsUsed, Is.EqualTo(new[] { 5000, 5001 }));
            Assert.That(harness.StreamReplayIds, Is.EqualTo(new long?[] { null, 41 }));
        }

        [Test]
        [Description("Goal: verify StreamEventsAsync treats Unity/Mono WebException transport failures as reconnectable after the service is stopped. Scope: AgentService stream recovery only. Boundaries: excludes real SSE transport and process management.")]
        public async Task StreamEventsAsync_RestartsAfterWebException()
        {
            var harness = new RestartHarness(initialStreamExceptionFactory: () => new WebException("forcibly closed"));
            var service = harness.CreateService();
            var receivedEvents = new List<AgentServiceEventEnvelope>();
            long? lastAcceptedSequenceNumber = null;
            using var cancellation = new CancellationTokenSource();

            await service.StreamEventsAsync(harness.Context, envelope =>
            {
                receivedEvents.Add(envelope);
                if (envelope.SequenceNumber > 0)
                {
                    lastAcceptedSequenceNumber = Math.Max(lastAcceptedSequenceNumber ?? 0, envelope.SequenceNumber);
                }
                if (envelope.SequenceNumber == 42)
                {
                    cancellation.Cancel();
                }
            }, cancellation.Token, () => lastAcceptedSequenceNumber);

            Assert.That(receivedEvents.Select(e => e.SequenceNumber), Is.EqualTo(new long[] { 41, 0, 42 }));
            Assert.That(receivedEvents[1].Type, Is.EqualTo(AgentEventType.SessionStatusChanged));
            Assert.That(receivedEvents[1].Content, Is.EqualTo("ready"));
            Assert.That(harness.ProgressMessages, Does.Contain("Agent service connection lost. Restarting service connection..."));
            Assert.That(harness.ProgressMessages, Does.Contain("Agent service connection restored."));
            Assert.That(harness.Bootstrap.StartCallCount, Is.EqualTo(1));
            Assert.That(harness.StreamPortsUsed, Is.EqualTo(new[] { 5000, 5001 }));
            Assert.That(harness.StreamReplayIds, Is.EqualTo(new long?[] { null, 41 }));
        }

        private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
            => await Task.WhenAny(task, Task.Delay(timeout)) == task;

        private sealed class RestartHarness
        {
            private readonly EndpointManifest _initialManifest = new EndpointManifest { Port = 5000, ServiceProcessId = 100, StartedAtUtc = DateTimeOffset.UtcNow };
            private readonly EndpointManifest _restartedManifest = new EndpointManifest { Port = 5001, ServiceProcessId = 101, StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1) };
            private readonly bool _staleInitialTransport;
            private readonly Func<Exception> _initialStreamExceptionFactory;
            private readonly HashSet<string> _attachedSessions = new HashSet<string>();
            private readonly UnityCodeAgentSettings _settings;
            private readonly string _projectRoot;

            public RestartHarness(string projectRoot = "C:/work", bool staleInitialTransport = true, Func<Exception> initialStreamExceptionFactory = null)
            {
                _projectRoot = projectRoot;
                _staleInitialTransport = staleInitialTransport;
                _initialStreamExceptionFactory = initialStreamExceptionFactory ?? (() => new HttpRequestException("stale port"));
                Bootstrap = new FakeServiceBootstrap(_restartedManifest);
                _settings = UnityEngine.ScriptableObject.CreateInstance<UnityCodeAgentSettings>();
                _settings.Model = new ModelInfoDto("gpt-5-mini", "GPT-5 Mini");
                Context = CreateContext();
            }

            public FakeServiceBootstrap Bootstrap { get; }

            public UnityContext Context { get; }

            public List<int> ApiPortsUsed { get; } = new List<int>();

            public List<string> ApiOperations { get; } = new List<string>();

            public List<int> StreamPortsUsed { get; } = new List<int>();

            public List<long?> StreamReplayIds { get; } = new List<long?>();

            public List<string> ProgressMessages { get; } = new List<string>();

            public AgentService CreateService()
                => new AgentService(
                    Bootstrap,
                    Bootstrap,
                    (_, manifest) => CreateApiClient(manifest),
                    (_, manifest) => CreateEventStreamClient(manifest),
                    _ => _initialManifest,
                    ProgressMessages.Add);

            private UnityContext CreateContext()
                => new UnityContext(
                    new UnityCodeAgentPaths(_projectRoot),
                    ProviderConfigDto.Create(_settings.Model, null, null, null, null),
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

            private IAgentServiceApiClient CreateApiClient(EndpointManifest manifest)
            {
                ApiPortsUsed.Add(manifest.Port);
                return new FakeAgentServiceApiClient(manifest, _initialManifest, _restartedManifest, _staleInitialTransport, _attachedSessions, ApiOperations);
            }

            private IAgentServiceEventStreamClient CreateEventStreamClient(EndpointManifest manifest)
            {
                StreamPortsUsed.Add(manifest.Port);
                return new FakeAgentServiceEventStreamClient(manifest, _initialManifest, _restartedManifest, StreamReplayIds, _initialStreamExceptionFactory);
            }
        }

        private sealed class FakeServiceBootstrap : IServiceBootstrap
        {
            private readonly EndpointManifest _restartedManifest;

            public FakeServiceBootstrap(EndpointManifest restartedManifest)
            {
                _restartedManifest = restartedManifest;
            }

            public int StartCallCount { get; private set; }

            public Task<EndpointManifest> ConnectOrStartAsync(UnityContext context)
            {
                StartCallCount++;
                return Task.FromResult(_restartedManifest);
            }
        }

        private sealed class RecordingServiceBootstrap : IServiceBootstrap
        {
            private readonly EndpointManifest _manifest;
            private readonly List<string> _operations;

            public RecordingServiceBootstrap(EndpointManifest manifest, List<string> operations)
            {
                _manifest = manifest;
                _operations = operations;
            }

            public int StartCallCount { get; private set; }

            public Task<EndpointManifest> ConnectOrStartAsync(UnityContext context)
            {
                StartCallCount++;
                _operations.Add("start");
                return Task.FromResult(_manifest);
            }
        }

        private sealed class BlockingServiceBootstrap : IServiceBootstrap
        {
            private readonly List<string> _operations;
            private readonly TaskCompletionSource<bool> _releaseFirstCall = new TaskCompletionSource<bool>();
            private readonly TaskCompletionSource<bool> _releaseSecondCall = new TaskCompletionSource<bool>();
            private int _currentCalls;

            public BlockingServiceBootstrap(List<string> operations)
            {
                _operations = operations;
            }

            public TaskCompletionSource<bool> FirstCallEntered { get; } = new TaskCompletionSource<bool>();

            public TaskCompletionSource<bool> SecondCallEntered { get; } = new TaskCompletionSource<bool>();

            public int StartCallCount { get; private set; }

            public int MaxConcurrentCalls { get; private set; }

            public async Task<EndpointManifest> ConnectOrStartAsync(UnityContext context)
            {
                StartCallCount++;
                var callNumber = StartCallCount;
                _currentCalls++;
                MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, _currentCalls);
                _operations.Add($"start-enter:{callNumber}");

                if (callNumber == 1)
                {
                    FirstCallEntered.SetResult(true);
                    await _releaseFirstCall.Task;
                }
                else
                {
                    SecondCallEntered.SetResult(true);
                    await _releaseSecondCall.Task;
                }

                _operations.Add($"start-exit:{callNumber}");
                _currentCalls--;
                return new EndpointManifest { Port = 5000 + callNumber, ServiceProcessId = 100 + callNumber };
            }

            public void ReleaseFirstCall()
                => _releaseFirstCall.SetResult(true);

            public void ReleaseSecondCall()
                => _releaseSecondCall.SetResult(true);
        }

        private sealed class FakeAgentServiceApiClient : IAgentServiceApiClient
        {
            private readonly EndpointManifest _manifest;
            private readonly EndpointManifest _initialManifest;
            private readonly EndpointManifest _restartedManifest;
            private readonly bool _staleInitialTransport;
            private readonly HashSet<string> _attachedSessions;
            private readonly List<string> _apiOperations;

            public FakeAgentServiceApiClient(
                EndpointManifest manifest,
                EndpointManifest initialManifest,
                EndpointManifest restartedManifest,
                bool staleInitialTransport,
                HashSet<string> attachedSessions,
                List<string> apiOperations)
            {
                _manifest = manifest;
                _initialManifest = initialManifest;
                _restartedManifest = restartedManifest;
                _staleInitialTransport = staleInitialTransport;
                _attachedSessions = attachedSessions;
                _apiOperations = apiOperations;
            }

            public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<SessionSummaryDto>>(Array.Empty<SessionSummaryDto>());

            public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
                => _manifest.Port == _initialManifest.Port
                    ? throw new HttpRequestException("stale port")
                    : Task.FromResult<IReadOnlyList<ModelInfoDto>>(new[] { new ModelInfoDto("gpt-5-mini", "GPT-5 Mini") });

            public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"open:{_manifest.Port}:{request.SessionId}");
                _attachedSessions.Add(CreateAttachedSessionKey(_manifest.Port, request.SessionId));
                return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));
            }

            public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
                => _manifest.Port == _initialManifest.Port
                    ? throw new HttpRequestException("stale port")
                    : Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));

            public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"send:{_manifest.Port}:{request.SessionId}");

                if (_manifest.Port == _initialManifest.Port && _staleInitialTransport)
                {
                    throw new HttpRequestException("stale port");
                }

                ThrowIfSessionNotAttached(request.SessionId);
                return Task.CompletedTask;
            }

            public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"abort:{_manifest.Port}:{request.SessionId}");

                if (_manifest.Port == _initialManifest.Port && _staleInitialTransport)
                {
                    throw new HttpRequestException("stale port");
                }

                ThrowIfSessionNotAttached(request.SessionId);
                return Task.CompletedTask;
            }

            private void ThrowIfSessionNotAttached(string sessionId)
            {
                if (!_attachedSessions.Contains(CreateAttachedSessionKey(_manifest.Port, sessionId)))
                {
                    throw new AgentServiceApiException(System.Net.HttpStatusCode.NotFound, $"Session '{sessionId}' is not attached.", AgentServiceErrorCodes.SessionUnavailable);
                }
            }

            private static string CreateAttachedSessionKey(int port, string sessionId)
                => $"{port}:{sessionId}";

            public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }

        private sealed class FakeAgentServiceEventStreamClient : IAgentServiceEventStreamClient
        {
            private readonly EndpointManifest _manifest;
            private readonly EndpointManifest _initialManifest;
            private readonly EndpointManifest _restartedManifest;
            private readonly List<long?> _replayIds;
            private readonly Func<Exception> _initialStreamExceptionFactory;

            public FakeAgentServiceEventStreamClient(
                EndpointManifest manifest,
                EndpointManifest initialManifest,
                EndpointManifest restartedManifest,
                List<long?> replayIds,
                Func<Exception> initialStreamExceptionFactory)
            {
                _manifest = manifest;
                _initialManifest = initialManifest;
                _restartedManifest = restartedManifest;
                _replayIds = replayIds;
                _initialStreamExceptionFactory = initialStreamExceptionFactory;
            }

            public Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken)
            {
                _replayIds.Add(lastEventId);

                if (_manifest.Port == _initialManifest.Port)
                {
                    onEvent(new AgentServiceEventEnvelope(41, "session-1", DateTimeOffset.UtcNow, "partial", null, AgentEventType.AssistantDelta, string.Empty, false));
                    throw _initialStreamExceptionFactory();
                }

                if (_manifest.Port == _restartedManifest.Port)
                {
                    onEvent(new AgentServiceEventEnvelope(42, "session-1", DateTimeOffset.UtcNow, "done", null, AgentEventType.AssistantMessage, string.Empty, false));
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return Task.CompletedTask;
            }
        }
    }
}
