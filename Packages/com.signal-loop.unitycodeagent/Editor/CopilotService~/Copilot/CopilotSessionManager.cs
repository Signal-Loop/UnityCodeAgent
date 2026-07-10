using System.Text.Json;
using GitHub.Copilot;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Api;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Settings;
using UnityCodeCopilot.Service.Telemetry;

namespace UnityCodeCopilot.Service.Copilot;

public sealed class CopilotSessionManager : IAsyncDisposable, IAgentSessionService
{
    private const string ReadyStatus = "ready";
    private const string StreamingStatus = "streaming";
    private const string QueuedStatus = "queued";
    private const string AbortingStatus = "aborting";
    private const string DegradedStatus = "degraded";

    private readonly EventStreamBroker _broker;
    private readonly IAgentRuntimeHost _copilotHost;
    private readonly UnityCodeCopilotServiceLogger _log;
    private readonly CopilotTelemetry _telemetry;
    private readonly object _sync = new();
    private readonly Dictionary<string, AttachedSession> _attachedSessions = new(StringComparer.Ordinal);

    public CopilotSessionManager(EventStreamBroker broker, IAgentRuntimeHost runtimeClient, UnityCodeCopilotServiceLogger log, CopilotTelemetry telemetry)
    {
        _broker = broker;
        _copilotHost = runtimeClient;
        _log = log;
        _telemetry = telemetry;
    }

    public Task<AgentSessionResponseDto> CreateAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.ServiceSessionCreate, async operation =>
        {
            operation.SetTags(
                ("gen_ai.conversation.id", request.SessionId),
                ("gen_ai.request.model", request.Model));

            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetAttachedSession(request.SessionId, out var existing))
            {
                operation.SetTag("session.reused", true);

                _log.Info(nameof(CopilotSessionManager), "Session already attached; returning existing session.",
                    ("sessionId", existing.SessionId),
                    ("status", existing.CurrentStatus));

                return await CreateSessionResponseAsync(existing.SessionId, existing.CurrentStatus, existing.Session, cancellationToken);
            }

            _log.Info(nameof(CopilotSessionManager), "Creating session.",
                ("sessionId", request.SessionId),
                ("model", request.Model));

            var session = await _copilotHost.CreateSessionAsync(request, cancellationToken);
            await AttachSessionAsync(session, request.SessionId, request.Provider, AgentSessionRequestSignature.Create(request), ReadyStatus, cancellationToken);
            return await CreateSessionResponseAsync(session.SessionId, ReadyStatus, session, cancellationToken);
        });

    public Task<AgentSessionResponseDto> OpenAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.ServiceSessionOpen, async operation =>
        {
            operation.SetTags(
                ("gen_ai.conversation.id", request.SessionId),
                ("gen_ai.request.model", request.Model));

            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetAttachedSession(request.SessionId, out var existing))
            {
                var requestSignature = AgentSessionRequestSignature.Create(request);
                if (string.Equals(existing.RequestSignature, requestSignature, StringComparison.Ordinal))
                {
                    operation.SetTag("session.reused", true);

                    _log.Info(nameof(CopilotSessionManager), "Session already attached with matching configuration; returning existing session.",
                        ("sessionId", existing.SessionId),
                        ("status", existing.CurrentStatus),
                        ("model", request.Model));

                    return await CreateSessionResponseAsync(existing.SessionId, existing.CurrentStatus, existing.Session, cancellationToken);
                }

                operation.SetTag("session.reconfigured", true);

                _log.Info(nameof(CopilotSessionManager), "Session already attached with different configuration; resuming same session id with updated configuration.",
                    ("sessionId", existing.SessionId),
                    ("status", existing.CurrentStatus),
                    ("model", request.Model));

                await DetachAttachedSessionAsync(existing);
            }

            _log.Info(nameof(CopilotSessionManager), "Opening session.",
                ("sessionId", request.SessionId),
                ("model", request.Model));

            var session = await _copilotHost.OpenSessionAsync(request, cancellationToken);
            await AttachSessionAsync(session, request.SessionId, request.Provider, AgentSessionRequestSignature.Create(request), ReadyStatus, cancellationToken);
            return await CreateSessionResponseAsync(session.SessionId, ReadyStatus, session, cancellationToken);
        });

    private Task<AgentSessionResponseDto> CreateSessionResponseAsync(string sessionId, string status, IAgentRuntimeSession session, CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.ServiceSessionSnapshot, async operation =>
        {
            operation.SetTags(
                ("gen_ai.conversation.id", sessionId),
                ("session.status", status));

            cancellationToken.ThrowIfCancellationRequested();

            var sessionMessages = await session.GetEventsAsync(cancellationToken);
            var messages = new List<AgentServiceEventEnvelope>(sessionMessages.Count);
            var suppressNextProviderNotFound = false;

            for (var index = 0; index < sessionMessages.Count; index++)
            {
                var sessionMessage = sessionMessages[index];
                if (sessionMessage is ModelCallFailureEvent { Data: { } failureData }
                    && ServiceEventEnvelopeFactory.IsUnsupportedImageInputFailure(failureData))
                {
                    suppressNextProviderNotFound = true;
                }
                else if (suppressNextProviderNotFound
                    && sessionMessage is SessionErrorEvent { Data.StatusCode: 404 })
                {
                    suppressNextProviderNotFound = false;
                    continue;
                }

                var serviceEvent = ServiceEventEnvelopeFactory.Create(index, sessionId, sessionMessage);
                if (serviceEvent is not null)
                {
                    messages.Add(serviceEvent);
                }
            }

            operation.SetTag("session.message_count", messages.Count);

            _log.Info(nameof(CopilotSessionManager), "Loaded session response.",
                ("sessionId", sessionId),
                ("status", status),
                ("messageCount", messages.Count));

            return new AgentSessionResponseDto(sessionId, status, messages);
        });

    public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.ServiceSessionsList, async operation =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runtimeSessions = await _copilotHost.ListSessionsAsync(cancellationToken);
            var orderedRuntimeSessions = runtimeSessions
                .OrderByDescending(session => session.ModifiedTime)
                .ThenBy(session => session.SessionId, StringComparer.Ordinal)
                .ToArray();

            operation.SetTag("session.count", orderedRuntimeSessions.Length);

            _log.Debug(nameof(CopilotSessionManager), "Reconciling current session inventory.", ("runtimeCount", orderedRuntimeSessions.Length));

            var summaries = new List<SessionSummaryDto>(orderedRuntimeSessions.Length);

            for (var index = 0; index < orderedRuntimeSessions.Length; index++)
            {
                summaries.Add(ToSessionSummary(orderedRuntimeSessions[index]));

            }

            _log.Info(nameof(CopilotSessionManager), "Reconciled current session inventory.", ("sessionCount", summaries.Count));

            return (IReadOnlyList<SessionSummaryDto>)summaries;
        });

    public Task SendAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.ServiceSessionSend, async operation =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            operation.SetTags(
                ("gen_ai.conversation.id", request.SessionId),
                ("service.prompt.length", request.Prompt.Length));

            _telemetry.RecordPromptLength(request.Prompt.Length);

            var attached = GetRequiredSession(request.SessionId);

            _log.Info(nameof(CopilotSessionManager), "Sending prompt to session.",
                ("sessionId", request.SessionId),
                ("promptLength", request.Prompt.Length),
                ("currentStatus", attached.CurrentStatus));

            var nextStatus = string.Equals(attached.CurrentStatus, ReadyStatus, StringComparison.OrdinalIgnoreCase)
                ? StreamingStatus
                : QueuedStatus;
            UpdateSessionStatus(attached, nextStatus);

            try
            {
                await attached.Session.SendPromptAsync(request.Prompt, cancellationToken);
            }
            catch (Exception exception) when (CopilotAuthFailureClassifier.IsAuthenticationFailure(exception))
            {
                throw CopilotAuthFailureClassifier.CreateAuthenticationException(attached.Provider, exception);
            }
        });

    public async Task SteerScreenshotAsync(
        string sessionId,
        AgentToolBinaryResultDto screenshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(screenshot);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(screenshot.Data))
        {
            throw new ArgumentException("Screenshot data must not be empty.", nameof(screenshot));
        }

        if (!string.Equals(screenshot.MimeType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Screenshot MIME type must be image/png.", nameof(screenshot));
        }

        try
        {
            _ = Convert.FromBase64String(screenshot.Data);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Screenshot data must be valid base64.", nameof(screenshot), exception);
        }

        var attached = GetRequiredSession(sessionId);
        _log.Info(nameof(CopilotSessionManager), "Steering Game View screenshot into active session.",
            ("sessionId", sessionId),
            ("mimeType", screenshot.MimeType),
            ("encodedLength", screenshot.Data.Length));

        await attached.Session.SteerScreenshotAsync(screenshot.Data, screenshot.MimeType, cancellationToken);
    }

    public Task AbortAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.ServiceSessionAbort, async operation =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            operation.SetTags(("gen_ai.conversation.id", request.SessionId));

            var attached = GetRequiredSession(request.SessionId);

            _log.Info(nameof(CopilotSessionManager), "Aborting active prompt.", ("sessionId", request.SessionId), ("status", attached.CurrentStatus));

            UpdateSessionStatus(attached, AbortingStatus);
            try
            {
                await attached.Session.AbortAsync(cancellationToken);
            }
            catch (Exception exception) when (CopilotAuthFailureClassifier.IsAuthenticationFailure(exception))
            {
                throw CopilotAuthFailureClassifier.CreateAuthenticationException(attached.Provider, exception);
            }

            _broker.Publish(request.SessionId, AgentEventType.Service, "Prompt aborted.");
            UpdateSessionStatus(attached, ReadyStatus);
        });

    public async ValueTask DisposeAsync()
    {
        AttachedSession[] sessions;
        lock (_sync)
        {
            sessions = _attachedSessions.Values.ToArray();
            _attachedSessions.Clear();
        }

        foreach (var session in sessions)
        {
            session.Subscription.Dispose();
            await session.Session.DisposeAsync();
        }

        _log.Info(nameof(CopilotSessionManager), "Disposed attached sessions.", ("count", sessions.Length));
    }

    private async Task AttachSessionAsync(
        IAgentRuntimeSession session,
        string sessionName,
        ProviderConfigDto? provider,
        string requestSignature,
        string initialStatus,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        AttachedSession? previous = null;
        var attached = new AttachedSession(session.SessionId, initialStatus, requestSignature, provider, session, null!);
        attached.Subscription = session.OnSessionEvent(sessionEvent => HandleRuntimeEvent(attached, sessionEvent));

        lock (_sync)
        {
            _attachedSessions.TryGetValue(attached.SessionId, out previous);
            _attachedSessions[attached.SessionId] = attached;
        }

        if (previous != null)
        {
            previous.Subscription.Dispose();
            await previous.Session.DisposeAsync();
            _log.Info(nameof(CopilotSessionManager), "Replaced previous attached session handle.", ("sessionId", attached.SessionId));
        }

        _log.Info(nameof(CopilotSessionManager), "Attached runtime session.",
            ("sessionId", session.SessionId),
            ("sessionName", sessionName),
            ("model", provider?.Model));
        UpdateSessionStatus(attached, initialStatus);
    }

    private async Task DetachAttachedSessionAsync(AttachedSession session)
    {
        lock (_sync)
        {
            if (_attachedSessions.TryGetValue(session.SessionId, out var current) && ReferenceEquals(current, session))
            {
                _attachedSessions.Remove(session.SessionId);
            }
        }

        session.Subscription.Dispose();
        await session.Session.DisposeAsync();
        _log.Info(nameof(CopilotSessionManager), "Detached runtime session handle before reconfiguration.", ("sessionId", session.SessionId));
    }

    private void HandleRuntimeEvent(AttachedSession attached, SessionEvent sessionEvent)
    {
        if (string.IsNullOrWhiteSpace(sessionEvent.Type))
        {
            return;
        }

        _log.Debug(nameof(CopilotSessionManager), "Received runtime event.",
            ("sessionId", attached.SessionId),
            ("eventType", sessionEvent.Type),
            ("content", IsScreenshotSteeringEvent(sessionEvent)
                ? "[internal screenshot steering event redacted]"
                : sessionEvent.ToJson()));

        _telemetry.RecordRuntimeEvent(sessionEvent.Type);

        var suppressProviderNotFound = attached.SuppressNextProviderNotFound
            && sessionEvent is SessionErrorEvent { Data.StatusCode: 404 };

        if (sessionEvent is ModelCallFailureEvent { Data: { } failureData }
            && ServiceEventEnvelopeFactory.IsUnsupportedImageInputFailure(failureData))
        {
            attached.SuppressNextProviderNotFound = true;
        }
        else if (sessionEvent is SessionErrorEvent)
        {
            attached.SuppressNextProviderNotFound = false;
        }

        if (!suppressProviderNotFound)
        {
            _broker.Publish(attached.SessionId, sessionEvent);
        }

        if (sessionEvent is SessionErrorEvent)
        {
            UpdateSessionStatus(attached, DegradedStatus);

            var errorType = sessionEvent is SessionErrorEvent { Data: { } data }
                ? data.ErrorType
                : "unknown";

            _log.Error(nameof(CopilotSessionManager), "Session error received.", null,
                ("sessionId", attached.SessionId),
                ("errorType", errorType));
        }

        if (sessionEvent is SessionIdleEvent)
        {
            _log.Info(nameof(CopilotSessionManager), "Session became idle.", ("sessionId", attached.SessionId));
            UpdateSessionStatus(attached, ReadyStatus);
        }
    }

    private static bool IsScreenshotSteeringEvent(SessionEvent sessionEvent)
        => sessionEvent is UserMessageEvent { Data.Content: ScreenshotSteering.Prompt };

    private AttachedSession GetRequiredSession(string sessionId)
    {
        lock (_sync)
        {
            if (_attachedSessions.TryGetValue(sessionId, out var session))
            {
                return session;
            }
        }

        throw new AgentSessionUnavailableException(sessionId);
    }

    private bool TryGetAttachedSession(string sessionId, out AttachedSession session)
    {
        lock (_sync)
        {
            if (_attachedSessions.TryGetValue(sessionId, out var attachedSession))
            {
                session = attachedSession;
                return true;
            }

            session = null!;
            return false;
        }
    }

    private static SessionSummaryDto ToSessionSummary(SessionMetadata runtimeSession)
    {
        return new(
                runtimeSession.SessionId,
                runtimeSession.StartTime,
                runtimeSession.ModifiedTime,
                runtimeSession.Summary,
                runtimeSession.IsRemote,
                runtimeSession.Context?.WorkingDirectory,
                runtimeSession.Context?.GitRoot,
                runtimeSession.Context?.Repository,
                runtimeSession.Context?.Branch);
    }

    private void UpdateSessionStatus(AttachedSession attached, string status)
    {
        lock (_sync)
        {
            attached.CurrentStatus = status;
        }

        _log.Info(nameof(CopilotSessionManager), "Session status changed.", ("sessionId", attached.SessionId), ("status", status));
        _broker.Publish(attached.SessionId, AgentEventType.SessionStatusChanged, $"Status: {status}");
    }

    private sealed class AttachedSession
    {
        public AttachedSession(string sessionId, string currentStatus, string requestSignature, ProviderConfigDto? provider, IAgentRuntimeSession session, IDisposable subscription)
        {
            SessionId = sessionId;
            CurrentStatus = currentStatus;
            RequestSignature = requestSignature;
            Provider = provider;
            Session = session;
            Subscription = subscription;
        }

        public string SessionId { get; }

        public string CurrentStatus { get; set; }

        public string RequestSignature { get; }

        public ProviderConfigDto? Provider { get; }

        public IAgentRuntimeSession Session { get; }

        public IDisposable Subscription { get; set; }

        public bool SuppressNextProviderNotFound { get; set; }
    }
}
