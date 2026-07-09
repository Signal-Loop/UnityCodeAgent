using GitHub.Copilot;
using NUnit.Framework;
using System.Net;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Api;
using UnityCodeCopilot.Service.Copilot;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Options;
using UnityCodeCopilot.Service.Settings;
using UnityCodeCopilot.Service.Telemetry;

namespace UnityCodeCopilot.Service.Tests;

public sealed class CopilotSessionManagerTests
{
    [Test]
    public async Task RuntimeEvents_UnsupportedImageInputSuppressesFollowingGenericProvider404()
    {
        var host = new FakeRuntimeHost();
        var broker = new EventStreamBroker();
        await using var manager = CreateManager(host, broker);
        await manager.CreateAsync(CreateRequest("session-1"), CancellationToken.None);

        host.Sessions["session-1"].Raise(CreateUnsupportedImageFailure());
        host.Sessions["session-1"].Raise(new SessionErrorEvent
        {
            Data = new SessionErrorData
            {
                ErrorType = "query",
                Message = "Resource not found on provider.",
                StatusCode = 404,
            },
            Timestamp = DateTimeOffset.UnixEpoch,
        });

        using var subscription = broker.Subscribe(0);
        var errors = subscription.RetainedEvents.Where(item => item.Type == AgentEventType.Error).ToArray();
        Assert.That(errors, Has.Length.EqualTo(1));
        Assert.That(errors[0].Content, Does.Contain("not available for image input"));
        Assert.That(errors[0].Content, Does.Not.Contain("BaseUrl"));
    }

    [Test]
    public async Task SteerScreenshotAsync_ForwardsValidPngToRequestedSessionOnly()
    {
        var host = new FakeRuntimeHost();
        await using var manager = CreateManager(host);
        await manager.CreateAsync(CreateRequest("session-1"), CancellationToken.None);
        await manager.CreateAsync(CreateRequest("session-2"), CancellationToken.None);
        var png = new AgentToolBinaryResultDto("iVBORw0KGgo=", "image/png", "image");

        await manager.SteerScreenshotAsync("session-2", png, CancellationToken.None);

        Assert.That(host.Sessions["session-1"].SteeredScreenshots, Is.Empty);
        Assert.That(host.Sessions["session-2"].SteeredScreenshots, Is.EqualTo(new[] { (png.Data, png.MimeType) }));
    }

    [TestCase("not-base64", "image/png")]
    [TestCase("iVBORw0KGgo=", "image/jpeg")]
    public async Task SteerScreenshotAsync_RejectsInvalidImageBeforeRuntimeSend(string data, string mimeType)
    {
        var host = new FakeRuntimeHost();
        await using var manager = CreateManager(host);
        await manager.CreateAsync(CreateRequest("session-1"), CancellationToken.None);

        Assert.ThrowsAsync<ArgumentException>(async () =>
            await manager.SteerScreenshotAsync(
                "session-1",
                new AgentToolBinaryResultDto(data, mimeType, "image"),
                CancellationToken.None));
        Assert.That(host.Sessions["session-1"].SteeredScreenshots, Is.Empty);
    }

    [Test]
    [Description("Goal: prove creating a second runtime session does not detach or dispose the first attached session. Scope: CopilotSessionManager attachment bookkeeping only. Boundaries: excludes real Copilot SDK transport and persisted event conversion.")]
    public async Task CreateAsync_KeepsPreviouslyAttachedSessionAlive()
    {
        var host = new FakeRuntimeHost();
        await using var manager = CreateManager(host);

        await manager.CreateAsync(CreateRequest("session-1"), CancellationToken.None);
        await manager.CreateAsync(CreateRequest("session-2"), CancellationToken.None);

        Assert.That(host.Sessions["session-1"].IsDisposed, Is.False);
        Assert.That(host.Sessions["session-1"].Subscription.IsDisposed, Is.False);
        Assert.That(host.Sessions["session-2"].IsDisposed, Is.False);

        await manager.SendAsync(new SendAgentPromptRequestDto("session-1", "prompt for first session"), CancellationToken.None);

        Assert.That(host.Sessions["session-1"].SentPrompts, Is.EqualTo(new[] { "prompt for first session" }));
        Assert.That(host.Sessions["session-2"].SentPrompts, Is.Empty);
    }

    [Test]
    [Description("Goal: prove send and abort route to the requested attached session id while other attached sessions remain alive. Scope: CopilotSessionManager command targeting only. Boundaries: excludes SDK queueing and cancellation internals.")]
    public async Task SendAsyncAndAbortAsync_TargetRequestedSessionOnly()
    {
        var host = new FakeRuntimeHost();
        await using var manager = CreateManager(host);

        await manager.CreateAsync(CreateRequest("session-1"), CancellationToken.None);
        await manager.CreateAsync(CreateRequest("session-2"), CancellationToken.None);

        await manager.SendAsync(new SendAgentPromptRequestDto("session-2", "prompt for second session"), CancellationToken.None);
        await manager.AbortAsync(new AbortAgentPromptRequestDto("session-1"), CancellationToken.None);

        Assert.That(host.Sessions["session-1"].AbortCount, Is.EqualTo(1));
        Assert.That(host.Sessions["session-1"].SentPrompts, Is.Empty);
        Assert.That(host.Sessions["session-2"].AbortCount, Is.Zero);
        Assert.That(host.Sessions["session-2"].SentPrompts, Is.EqualTo(new[] { "prompt for second session" }));
    }

    [Test]
    [Description("Goal: prove reopening one attached session with a different provider signature detaches only that session id. Scope: CopilotSessionManager provider reconfiguration only. Boundaries: excludes model validation and SDK resume behavior.")]
    public async Task OpenAsync_WithChangedProvider_ReplacesOnlyMatchingSession()
    {
        var host = new FakeRuntimeHost();
        await using var manager = CreateManager(host);

        var firstSession1 = await manager.OpenAsync(OpenRequest("session-1", "gpt-4o"), CancellationToken.None);
        await manager.OpenAsync(OpenRequest("session-2", "gpt-4o"), CancellationToken.None);
        await manager.OpenAsync(OpenRequest("session-1", "claude-sonnet-4"), CancellationToken.None);

        Assert.That(firstSession1.SessionId, Is.EqualTo("session-1"));
        Assert.That(host.CreatedSessions["open:session-1:1"].IsDisposed, Is.True);
        Assert.That(host.CreatedSessions["open:session-1:1"].Subscription.IsDisposed, Is.True);
        Assert.That(host.CreatedSessions["open:session-2:1"].IsDisposed, Is.False);
        Assert.That(host.CreatedSessions["open:session-1:2"].IsDisposed, Is.False);

        await manager.SendAsync(new SendAgentPromptRequestDto("session-2", "still attached"), CancellationToken.None);

        Assert.That(host.CreatedSessions["open:session-2:1"].SentPrompts, Is.EqualTo(new[] { "still attached" }));
    }

    [Test]
    [Description("Goal: prove reopening an attached session with changed disabled skills detaches and resumes the runtime session with the updated skill configuration. Scope: CopilotSessionManager request signature only. Boundaries: excludes SDK skill loading internals.")]
    public async Task OpenAsync_WithChangedDisabledSkills_ReplacesAttachedSession()
    {
        var host = new FakeRuntimeHost();
        await using var manager = CreateManager(host);

        await manager.OpenAsync(OpenRequest("session-1", "gpt-4o", disabledSkills: Array.Empty<string>()), CancellationToken.None);
        await manager.OpenAsync(OpenRequest("session-1", "gpt-4o", disabledSkills: new[] { "expensive-skill" }), CancellationToken.None);

        Assert.That(host.CreatedSessions["open:session-1:1"].IsDisposed, Is.True);
        Assert.That(host.CreatedSessions["open:session-1:1"].Subscription.IsDisposed, Is.True);
        Assert.That(host.CreatedSessions["open:session-1:2"].IsDisposed, Is.False);
        Assert.That(host.OpenRequests, Has.Count.EqualTo(2));
        Assert.That(host.OpenRequests[0].DisabledSkills, Is.Empty);
        Assert.That(host.OpenRequests[1].DisabledSkills, Is.EqualTo(new[] { "expensive-skill" }));
    }

    [Test]
    [Description("Goal: prove prompt-send auth failures on BYOK sessions keep BYOK provider guidance and redact the configured API key. Scope: CopilotSessionManager exception translation only. Boundaries: excludes live provider calls and endpoint serialization.")]
    public async Task SendAsync_ByokAuthFailure_ThrowsByokGuidanceWithoutApiKey()
    {
        var host = new FakeRuntimeHost();
        await using var manager = CreateManager(host);
        var provider = new ProviderConfigDto("gpt-4o", BaseUrl: "https://provider.example.test/v1", ApiKey: "secret-test-key");

        await manager.CreateAsync(new CreateAgentSessionRequestDto("session-1", provider, true, new InfiniteSessionsDto(), AppContext.BaseDirectory), CancellationToken.None);
        host.Sessions["session-1"].SendException = new IOException("provider returned 401 Unauthorized for key secret-test-key");

        var exception = Assert.ThrowsAsync<AgentServiceAuthenticationException>(async () =>
            await manager.SendAsync(new SendAgentPromptRequestDto("session-1", "hello"), CancellationToken.None));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("BYOK provider request failed."));
        Assert.That(exception.Message, Does.Contain("BaseUrl"));
        Assert.That(exception.Message, Does.Contain("selected model"));
        Assert.That(exception.Message, Does.Not.Contain("401 Unauthorized"));
        Assert.That(exception.Message, Does.Not.Contain("secret-test-key"));
        Assert.That(exception.Message, Does.Not.Contain("Details:"));
        Assert.That(exception.InnerException?.Message, Does.Contain("401 Unauthorized"));
    }

    [Test]
    [Description("Goal: prove prompt-send auth failures on default Copilot sessions keep GitHub Copilot-specific guidance. Scope: CopilotSessionManager exception translation only. Boundaries: excludes SDK auth preflight and endpoint serialization.")]
    public async Task SendAsync_CopilotAuthFailure_ThrowsCopilotGuidance()
    {
        var host = new FakeRuntimeHost();
        await using var manager = CreateManager(host);

        await manager.CreateAsync(CreateRequest("session-1"), CancellationToken.None);
        host.Sessions["session-1"].SendException = new IOException("Communication error with Copilot CLI: 401 Unauthorized");

        var exception = Assert.ThrowsAsync<AgentServiceAuthenticationException>(async () =>
            await manager.SendAsync(new SendAgentPromptRequestDto("session-1", "hello"), CancellationToken.None));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("GitHub Copilot authentication failed."));
        Assert.That(exception.Message, Does.Not.Contain("401 Unauthorized"));
        Assert.That(exception.Message, Does.Not.Contain("Details:"));
        Assert.That(exception.InnerException?.Message, Does.Contain("401 Unauthorized"));
    }

    [Test]
    [Description("Goal: prove prompt-send auth failures prefer HttpRequestException status codes over parsing response text. Scope: shared auth failure classification through CopilotSessionManager. Boundaries: excludes live provider calls and endpoint serialization.")]
    public async Task SendAsync_ByokHttpUnauthorizedStatus_ThrowsByokGuidance()
    {
        var host = new FakeRuntimeHost();
        await using var manager = CreateManager(host);
        var provider = new ProviderConfigDto("gpt-4o", BaseUrl: "https://provider.example.test/v1", ApiKey: "secret-test-key");

        await manager.CreateAsync(new CreateAgentSessionRequestDto("session-1", provider, true, new InfiniteSessionsDto(), AppContext.BaseDirectory), CancellationToken.None);
        host.Sessions["session-1"].SendException = new HttpRequestException("provider request failed", null, HttpStatusCode.Unauthorized);

        var exception = Assert.ThrowsAsync<AgentServiceAuthenticationException>(async () =>
            await manager.SendAsync(new SendAgentPromptRequestDto("session-1", "hello"), CancellationToken.None));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("BYOK provider request failed."));
        Assert.That(exception.Message, Does.Contain("BaseUrl"));
        Assert.That(exception.Message, Does.Contain("selected model"));
        Assert.That(exception.InnerException, Is.TypeOf<HttpRequestException>());
    }

    private static CopilotSessionManager CreateManager(FakeRuntimeHost host, EventStreamBroker? broker = null)
        => new(
            broker ?? new EventStreamBroker(),
            host,
            new UnityCodeCopilotServiceLogger(
                new ProjectPaths(AppContext.BaseDirectory),
                new ServiceOptions
                {
                    LogToFile = false,
                    MinLogLevel = UnityCodeCopilotServiceLogger.LogLevel.Off,
                }),
            new CopilotTelemetry());

    private static ModelCallFailureEvent CreateUnsupportedImageFailure()
        => new()
        {
            Data = new ModelCallFailureData
            {
                ErrorMessage = """{"message":"No endpoints found that support image input","code":404}""",
                StatusCode = 404,
                Source = ModelCallFailureSource.TopLevel,
                RequestFingerprint = new ModelCallFailureRequestFingerprint
                {
                    ImagePartCount = 1,
                    ImagePartsMissingMediaType = 0,
                    MessageCount = 5,
                    NamelessToolCallCount = 0,
                    ToolCallCount = 1,
                    ToolResultMessageCount = 1,
                },
            },
            Timestamp = DateTimeOffset.UnixEpoch,
        };

    private static CreateAgentSessionRequestDto CreateRequest(string sessionId, string model = "gpt-4o")
        => new(sessionId, new ProviderConfigDto(model), true, new InfiniteSessionsDto(), AppContext.BaseDirectory);

    private static OpenAgentSessionRequestDto OpenRequest(string sessionId, string model, IReadOnlyList<string>? disabledSkills = null)
        => new(
            sessionId,
            new ProviderConfigDto(model),
            true,
            new InfiniteSessionsDto(),
            AppContext.BaseDirectory,
            Array.Empty<string>(),
            disabledSkills ?? Array.Empty<string>());

    private sealed class FakeRuntimeHost : IAgentRuntimeHost
    {
        private readonly Dictionary<string, int> _createCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _openCounts = new(StringComparer.Ordinal);

        public Dictionary<string, FakeRuntimeSession> Sessions { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, FakeRuntimeSession> CreatedSessions { get; } = new(StringComparer.Ordinal);

        public List<OpenAgentSessionRequestDto> OpenRequests { get; } = new();

        public Task<AgentRuntimeAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AgentRuntimeAuthStatus(true, "Authenticated", "test", "test-user"));

        public Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SessionMetadata>>(Array.Empty<SessionMetadata>());

        public Task<IAgentRuntimeSession> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
        {
            var session = CreateSession("create", request.SessionId, _createCounts);
            return Task.FromResult<IAgentRuntimeSession>(session);
        }

        public Task<IAgentRuntimeSession> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
        {
            OpenRequests.Add(request);
            var session = CreateSession("open", request.SessionId, _openCounts);
            return Task.FromResult<IAgentRuntimeSession>(session);
        }

        private FakeRuntimeSession CreateSession(string operation, string sessionId, Dictionary<string, int> counts)
        {
            counts.TryGetValue(sessionId, out var count);
            count++;
            counts[sessionId] = count;

            var session = new FakeRuntimeSession(sessionId);
            Sessions[sessionId] = session;
            CreatedSessions[$"{operation}:{sessionId}:{count}"] = session;
            return session;
        }
    }

    private sealed class FakeRuntimeSession : IAgentRuntimeSession
    {
        public FakeRuntimeSession(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }

        public List<string> SentPrompts { get; } = new();

        public List<(string Data, string MimeType)> SteeredScreenshots { get; } = new();

        public Exception? SendException { get; set; }

        public int AbortCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public FakeSubscription Subscription { get; } = new();

        public Task<IReadOnlyList<SessionEvent>> GetEventsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SessionEvent>>(Array.Empty<SessionEvent>());

        public IDisposable OnSessionEvent(Action<SessionEvent> handler)
        {
            EventHandler = handler;
            return Subscription;
        }

        public Action<SessionEvent>? EventHandler { get; private set; }

        public void Raise(SessionEvent sessionEvent)
            => EventHandler?.Invoke(sessionEvent);

        public Task SendPromptAsync(string prompt, CancellationToken cancellationToken)
        {
            if (SendException != null)
            {
                throw SendException;
            }

            SentPrompts.Add(prompt);
            return Task.CompletedTask;
        }

        public Task SteerScreenshotAsync(string base64Data, string mimeType, CancellationToken cancellationToken)
        {
            SteeredScreenshots.Add((base64Data, mimeType));
            return Task.CompletedTask;
        }

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            AbortCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSubscription : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
            => IsDisposed = true;
    }
}
