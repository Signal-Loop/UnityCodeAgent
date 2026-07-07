using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Settings;
using SignalLoop.UnityCodeAgent.UI;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class ChatEditorWindowClient : IDisposable
    {
        private readonly AgentService _service;
        private readonly EventStreamCursorStore _cursorStore;
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();
        private readonly ConcurrentQueue<AgentServiceEventEnvelope> _pendingServiceEvents = new ConcurrentQueue<AgentServiceEventEnvelope>();
        private readonly ConcurrentQueue<ChatClientUpdate> _pendingClientUpdates = new ConcurrentQueue<ChatClientUpdate>();
        private readonly HashSet<string> _changedSessionIds = new HashSet<string>();
        private readonly ActiveSessionState _activeSession = new ActiveSessionState();
        private CancellationTokenSource _eventStreamCancellation;
        private string _streamGenerationId = string.Empty;
        private string _pendingPromptEcho = string.Empty;
        private long? _lastReceivedGlobalEventId;
        private bool _isHydratingHistory;
        private bool _isEventStreamStarted;
        private bool _isShowingSessions;
        private bool _isRefreshingSessionRequest;
        private DateTimeOffset _nextSessionRequestRefreshUtc = DateTimeOffset.MinValue;

        public bool IsShowingSessions => _isShowingSessions;

        public ChatEditorWindowClient()
        {
            _service = new AgentService(ShowProgressMessage);
            _cursorStore = new EventStreamCursorStore();
        }

        public ChatEditorWindowClient(AgentService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _cursorStore = new EventStreamCursorStore();
        }

        public async Task<ChatClientCallResult> InitializeAsync(UnityContext context, CancellationToken cancellationToken)
        {
            ResetState();

            if (!ValidateProvider(context, out var validationFailure))
            {
                _activeSession.ClearRequestSignature();
                return validationFailure(
                    new ChatSetModelLabelUpdate(ProviderConfigDto.Empty.DisplayName),
                    new ChatSetBusyStateUpdate(false),
                    new ChatShowMessagesUpdate(Array.Empty<AgentServiceEventEnvelope>()));
            }

            _isHydratingHistory = true;

            try
            {
                _log.Debug(nameof(ChatEditorWindowClient), "Loading current session.");
                ShowProgressMessage("Opening chat window...");
                var modelSelection = _service.GetSelectedModel(context);
                var sessionRequestSignature = CreateSessionRequestSignature(context);
                var manifest = await _service.GetEndpointManifestAsync(context).ConfigureAwait(false);
                SelectStreamCursor(context, manifest);
                var session = await _service.GetCurrentSessionAsync(context, cancellationToken).ConfigureAwait(false);
                SetActiveSession(session.SessionId, sessionRequestSignature, session.Status);
                _changedSessionIds.Remove(_activeSession.SessionId);
                _log.Info(nameof(ChatEditorWindowClient), $"Loaded current session history sessionId={_activeSession.SessionId} messages={session.Messages.Count}");
                return Success(
                    new ChatSetModelLabelUpdate(modelSelection.DisplayName),
                    new ChatSetBusyStateUpdate(_activeSession.IsBusy),
                    new ChatShowMessagesUpdate(session.Messages));
            }
            catch (InvalidOperationException exception) when (exception.Message.IndexOf("did not contain any sessions", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ClearActiveSession();
                _log.Info(nameof(ChatEditorWindowClient), "No current chat session was available; showing empty transcript.");
                var modelSelection = GetSelectedModelOrDefault(context);
                _activeSession.SetRequestSignature(TryCreateSessionRequestSignature(context));
                return Success(
                    new ChatSetModelLabelUpdate(modelSelection.DisplayName),
                    new ChatSetBusyStateUpdate(false),
                    new ChatShowMessagesUpdate(Array.Empty<AgentServiceEventEnvelope>()));
            }
            catch (Exception exception)
            {
                ClearActiveSession();
                _log.Error(nameof(ChatEditorWindowClient), "Failed to load current chat session history.", exception);
                var modelSelection = GetSelectedModelOrDefault(context);
                _activeSession.SetRequestSignature(TryCreateSessionRequestSignature(context));
                return Failure(
                    new ChatSetModelLabelUpdate(modelSelection.DisplayName),
                    new ChatSetBusyStateUpdate(false),
                    new ChatShowMessagesUpdate(Array.Empty<AgentServiceEventEnvelope>()),
                    new ChatShowErrorUpdate(exception.Message, exception.ToString()));
            }
            finally
            {
                ClearQueuedProgressUpdates();
                _isHydratingHistory = false;
                EnsureEventStreamStarted(context);
            }
        }

        public async Task<ChatClientCallResult> SubmitPromptAsync(UnityContext context, string prompt, CancellationToken cancellationToken)
        {
            if (_isHydratingHistory)
            {
                _log.Debug(nameof(ChatEditorWindowClient), "Ignoring prompt submission while the window is hydrating.");
                return Failure();
            }

            if (_activeSession.IsBusy && !_isShowingSessions)
            {
                _log.Debug(nameof(ChatEditorWindowClient), "Ignoring prompt submission while the active session is busy.");
                return Failure();
            }

            var trimmedPrompt = (prompt ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmedPrompt))
            {
                _log.Debug(nameof(ChatEditorWindowClient), "Ignoring empty prompt submission.");
                return Failure();
            }

            if (!ValidateProvider(context, out var validationFailure))
            {
                var invalidUpdates = new List<ChatClientUpdate>();
                if (_isShowingSessions)
                {
                    ClearPendingServiceEvents();
                    ClearActiveSession();
                    _isShowingSessions = false;
                    invalidUpdates.Add(new ChatShowMessagesUpdate(null));
                }

                invalidUpdates.Add(new ChatSetModelLabelUpdate(ProviderConfigDto.Empty.DisplayName));
                invalidUpdates.Add(new ChatSetBusyStateUpdate(false));
                return validationFailure(invalidUpdates.ToArray());
            }

            var updates = new List<ChatClientUpdate>();

            try
            {
                if (_isShowingSessions)
                {
                    ClearPendingServiceEvents();
                    SetActiveSession(string.Empty, CreateSessionRequestSignature(context), "ready");
                    _isShowingSessions = false;
                    EnqueueUpdate(new ChatShowMessagesUpdate(null));
                }
                else if (!await EnsureSessionRequestAppliedAsync(context, cancellationToken).ConfigureAwait(false))
                {
                    return Failure(updates);
                }

                _pendingPromptEcho = trimmedPrompt;
                SetActiveSessionStatus("streaming");
                EnqueueUpdate(new ChatShowAgentEventUpdate(new AgentServiceEventEnvelope(
                    0,
                    _activeSession.SessionId,
                    DateTimeOffset.UtcNow,
                    trimmedPrompt,
                    null,
                    AgentEventType.UserMessage,
                    string.Empty,
                    false)));
                EnqueueUpdate(new ChatSetUserInput(string.Empty));
                EnqueueUpdate(new ChatSetBusyStateUpdate(true));

                if (string.IsNullOrWhiteSpace(_activeSession.SessionId))
                {
                    _log.Info(nameof(ChatEditorWindowClient), "Creating a new chat session for submitted prompt.");
                    var createdSession = await _service.CreateSessionAsync(context, cancellationToken).ConfigureAwait(false);
                    SetActiveSession(createdSession.SessionId, _activeSession.RequestSignature, "streaming");
                    _log.Info(nameof(ChatEditorWindowClient), $"Created chat session sessionId={_activeSession.SessionId}");
                }

                _changedSessionIds.Add(_activeSession.SessionId);

                EnsureEventStreamStarted(context);
                _log.Info(nameof(ChatEditorWindowClient), $"Submitting prompt sessionId={_activeSession.SessionId} promptLength={trimmedPrompt.Length}");

                await _service.SendPromptAsync(context, new SendAgentPromptRequestDto(_activeSession.SessionId, trimmedPrompt), cancellationToken).ConfigureAwait(false);
                return Success(updates);
            }
            catch (Exception exception)
            {
                SetActiveSessionStatus("ready");
                _log.Error(nameof(ChatEditorWindowClient), "Prompt submission failed.", exception);
                EnqueueUpdate(new ChatShowErrorUpdate(exception.Message, exception.ToString()));
                EnqueueUpdate(new ChatSetBusyStateUpdate(false));
                return Failure(updates);
            }
        }

        public async Task<ChatClientCallResult> AbortPromptAsync(UnityContext context, CancellationToken cancellationToken)
        {
            if (_isHydratingHistory)
            {
                _log.Debug(nameof(ChatEditorWindowClient), "Ignoring abort while the window is hydrating.");
                return Failure();
            }

            if (!_activeSession.IsBusy || _isShowingSessions)
            {
                _log.Debug(nameof(ChatEditorWindowClient), "Ignoring abort because no active response is busy.");
                return Failure();
            }

            if (string.IsNullOrWhiteSpace(_activeSession.SessionId))
            {
                _log.Warning(nameof(ChatEditorWindowClient), "Abort requested without an active session.");
                return Failure();
            }

            try
            {
                _log.Debug(nameof(ChatEditorWindowClient), $"Aborting active response sessionId={_activeSession.SessionId}");
                _changedSessionIds.Remove(_activeSession.SessionId);
                await _service.AbortPromptAsync(context, new AbortAgentPromptRequestDto(_activeSession.SessionId), cancellationToken).ConfigureAwait(false);
                return Success();
            }
            catch (Exception exception)
            {
                _log.Error(nameof(ChatEditorWindowClient), $"Abort failed sessionId={_activeSession.SessionId}", exception);
                return Failure(new ChatShowErrorUpdate(exception.Message, exception.ToString()));
            }
        }

        public async Task<ChatClientCallResult> ShowSessionsAsync(UnityContext context, CancellationToken cancellationToken)
        {
            try
            {
                var sessions = await _service.GetSessionsAsync(context, cancellationToken).ConfigureAwait(false);
                _isShowingSessions = true;
                return Success(new ChatShowSessionsUpdate(sessions, _changedSessionIds));
            }
            catch (Exception exception)
            {
                _log.Error(nameof(ChatEditorWindowClient), "Loading session list failed.", exception);
                return Failure(new ChatShowErrorUpdate(exception.Message, exception.ToString()));
            }
        }

        public async Task<ChatClientCallResult> OpenSessionAsync(UnityContext context, string sessionId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Failure();
            }

            if (!ValidateProvider(context, out var validationFailure))
            {
                return validationFailure(
                    new ChatSetModelLabelUpdate(ProviderConfigDto.Empty.DisplayName),
                    new ChatSetBusyStateUpdate(false));
            }

            try
            {
                var session = await _service.OpenSessionAsync(context, sessionId, cancellationToken).ConfigureAwait(false);
                var modelSelection = _service.GetSelectedModel(context);
                var resolvedSessionId = string.IsNullOrWhiteSpace(session.SessionId) ? sessionId : session.SessionId;
                SetActiveSession(resolvedSessionId, CreateSessionRequestSignature(context), session.Status);
                _isShowingSessions = false;
                _changedSessionIds.Remove(_activeSession.SessionId);
                EnsureEventStreamStarted(context);
                return Success(
                    new ChatSetModelLabelUpdate(modelSelection.DisplayName),
                    new ChatSetBusyStateUpdate(_activeSession.IsBusy),
                    new ChatShowMessagesUpdate(session.Messages));
            }
            catch (Exception exception)
            {
                EnsureEventStreamStarted(context);
                _log.Error(nameof(ChatEditorWindowClient), $"Opening session failed sessionId={sessionId}", exception);
                return Failure(new ChatShowErrorUpdate(exception.Message, exception.ToString()));
            }
        }

        public IReadOnlyList<ChatClientUpdate> DrainUpdates(UnityContext context)
        {
            StartSessionRequestRefreshIfNeeded(context);
            DrainServiceEvents(context);

            var updates = new List<ChatClientUpdate>();
            while (_pendingClientUpdates.TryDequeue(out var update))
            {
                updates.Add(update);
            }

            return updates;
        }

        public void Dispose()
        {
            _log.Debug(nameof(ChatEditorWindowClient), "Disposing chat window client.");
            StopEventStream();
        }

        private void ResetState()
        {
            StopEventStream();
            ClearPendingServiceEvents();

            while (_pendingClientUpdates.TryDequeue(out _))
            {
            }

            ClearActiveSession();
            _pendingPromptEcho = string.Empty;
            _streamGenerationId = string.Empty;
            _lastReceivedGlobalEventId = null;
            _isHydratingHistory = false;
            _isEventStreamStarted = false;
            _isShowingSessions = false;
            _isRefreshingSessionRequest = false;
            _nextSessionRequestRefreshUtc = DateTimeOffset.MinValue;
            _changedSessionIds.Clear();
        }

        private void EnsureEventStreamStarted(UnityContext context)
        {
            if (_isEventStreamStarted)
            {
                return;
            }

            _isEventStreamStarted = true;
            _eventStreamCancellation = new CancellationTokenSource();
            _log.Info(nameof(ChatEditorWindowClient), "Starting event stream pump.");
            _ = ObserveEventStreamAsync(context, _eventStreamCancellation.Token);
        }

        private async Task ObserveEventStreamAsync(UnityContext context, CancellationToken cancellationToken)
        {
            try
            {
                await _service.StreamEventsAsync(
                    context,
                    envelope => _pendingServiceEvents.Enqueue(envelope),
                    cancellationToken,
                    () => _lastReceivedGlobalEventId,
                    manifest => OnStreamManifestChanged(context, manifest)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _log.Debug(nameof(ChatEditorWindowClient), "Event stream observation cancelled.");
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                _log.Debug(nameof(ChatEditorWindowClient), "Event stream observer disposed during shutdown.");
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested)
            {
                _log.Debug(nameof(ChatEditorWindowClient), $"Suppressing event stream exception during shutdown. error={exception.GetType().Name}");
            }
            catch (Exception exception)
            {
                SetActiveSessionStatus("ready");
                _isEventStreamStarted = false;
                _log.Error(nameof(ChatEditorWindowClient), "Agent stream observation failed.", exception);
                EnqueueUpdate(new ChatShowErrorUpdate("Agent stream observation failed.", exception.ToString()));
                EnqueueUpdate(new ChatSetBusyStateUpdate(false));
            }
        }

        private void StopEventStream()
        {
            if (_eventStreamCancellation == null)
            {
                return;
            }

            try
            {
                _eventStreamCancellation.Cancel();
            }
            catch
            {
            }

            _log.Debug(nameof(ChatEditorWindowClient), "Stopping event stream pump.");
            _eventStreamCancellation.Dispose();
            _eventStreamCancellation = null;
            _isEventStreamStarted = false;
        }

        private void ClearPendingServiceEvents()
        {
            while (_pendingServiceEvents.TryDequeue(out _))
            {
            }
        }

        private void SelectStreamCursor(UnityContext context, EndpointManifest manifest)
        {
            var generationId = manifest?.StreamGenerationId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(generationId))
            {
                generationId = manifest?.ServiceProcessId > 0
                    ? manifest.ServiceProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "mock";
            }

            _streamGenerationId = generationId;
            var persistedCursor = _cursorStore.Load(context.Paths);
            if (string.Equals(persistedCursor.StreamGenerationId, _streamGenerationId, StringComparison.Ordinal))
            {
                _lastReceivedGlobalEventId = persistedCursor.LastAcceptedSequenceNumber;
                _log.Info(nameof(ChatEditorWindowClient), $"Loaded event stream cursor generation={_streamGenerationId} sequence={_lastReceivedGlobalEventId?.ToString() ?? "null"}");
                return;
            }

            _lastReceivedGlobalEventId = null;
            _cursorStore.Save(context.Paths, _streamGenerationId, null);
            _log.Info(nameof(ChatEditorWindowClient), $"Starting event stream without replay cursor generation={_streamGenerationId}");
        }

        private void OnStreamManifestChanged(UnityContext context, EndpointManifest manifest)
        {
            var generationId = manifest?.StreamGenerationId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(generationId))
            {
                generationId = manifest?.ServiceProcessId > 0
                    ? manifest.ServiceProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "mock";
            }

            if (string.Equals(_streamGenerationId, generationId, StringComparison.Ordinal))
            {
                return;
            }

            _streamGenerationId = generationId;
            _lastReceivedGlobalEventId = null;
            _cursorStore.Save(context.Paths, _streamGenerationId, null);
            _log.Info(nameof(ChatEditorWindowClient), $"Event stream generation changed; discarded cursor generation={_streamGenerationId}");
        }

        private void DrainServiceEvents(UnityContext context)
        {
            while (_pendingServiceEvents.TryDequeue(out var envelope))
            {
                if (envelope == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(envelope.SessionId)
                    && envelope.SequenceNumber == 0
                    && envelope.Type == AgentEventType.SessionStatusChanged
                    && !string.IsNullOrWhiteSpace(_activeSession.SessionId))
                {
                    envelope = envelope with { SessionId = _activeSession.SessionId };
                }

                if (string.IsNullOrWhiteSpace(envelope.SessionId))
                {
                    continue;
                }

                AcceptServiceEvent(context, envelope);

                if (!_isShowingSessions && IsActiveSession(envelope.SessionId))
                {
                    _log.Trace(nameof(ChatEditorWindowClient), $"Applying queued service event eventType={envelope.Type} sessionId={envelope.SessionId} sequence={envelope.SequenceNumber}");
                    ApplyServiceEvent(context, envelope);
                }
                else
                {
                    _log.Trace(nameof(ChatEditorWindowClient), $"Applying queued background service event eventType={envelope.Type} sessionId={envelope.SessionId} sequence={envelope.SequenceNumber}");
                    ApplyBackgroundServiceEvent(context, envelope);
                }
            }
        }

        private void ApplyServiceEvent(UnityContext context, AgentServiceEventEnvelope envelope)
        {
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.SessionId))
            {
                return;
            }

            bool hasBusyStateUpdate = false;
            bool nextBusyState = _activeSession.IsBusy;

            switch (envelope.Type)
            {
                case AgentEventType.Error:
                    nextBusyState = SetActiveSessionStatus("ready");
                    hasBusyStateUpdate = true;
                    _changedSessionIds.Remove(envelope.SessionId);
                    break;
                case AgentEventType.UserMessage:
                    if (string.Equals(envelope.Content.Trim(), _pendingPromptEcho, StringComparison.Ordinal))
                    {
                        _pendingPromptEcho = string.Empty;
                        return;
                    }
                    break;
                case AgentEventType.SessionIdle:
                    _log.Info(nameof(ChatEditorWindowClient), $"Session became idle sessionId={envelope.SessionId}");
                    nextBusyState = SetActiveSessionStatus("ready");
                    hasBusyStateUpdate = true;
                    _changedSessionIds.Remove(envelope.SessionId);
                    break;
                case AgentEventType.SessionStatusChanged:
                    nextBusyState = SetActiveSessionStatus(envelope.Content);
                    hasBusyStateUpdate = true;
                    _log.Debug(nameof(ChatEditorWindowClient), $"Session status changed sessionId={envelope.SessionId} status={envelope.Content}");
                    break;
                case AgentEventType.ToolInvocationRequest:
                    _ = ExecuteToolInvocationRequestAsync(context, envelope);
                    return;
            }

            if (hasBusyStateUpdate)
            {
                EnqueueUpdate(new ChatSetBusyStateUpdate(nextBusyState));
            }

            EnqueueUpdate(new ChatShowAgentEventUpdate(envelope));
        }

        private void ApplyBackgroundServiceEvent(UnityContext context, AgentServiceEventEnvelope envelope)
        {
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.SessionId))
            {
                return;
            }

            _changedSessionIds.Add(envelope.SessionId);

            if (envelope.Type == AgentEventType.ToolInvocationRequest)
            {
                _ = ExecuteToolInvocationRequestAsync(context, envelope);
                return;
            }

            if (envelope.Type == AgentEventType.Error)
            {
                return;
            }

            if (envelope.Type == AgentEventType.SessionIdle)
            {
                _log.Info(nameof(ChatEditorWindowClient), $"Background session became idle sessionId={envelope.SessionId}");
                return;
            }

            if (envelope.Type == AgentEventType.SessionStatusChanged)
            {
                _log.Debug(nameof(ChatEditorWindowClient), $"Background session status changed sessionId={envelope.SessionId} status={envelope.Content}");
            }
        }

        public async Task ExecuteToolInvocationRequestAsync(UnityContext context, AgentServiceEventEnvelope envelope)
        {
            try
            {
                await _service.ExecuteToolInvocationRequestAsync(context, envelope, _eventStreamCancellation?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _log.Debug(nameof(ChatEditorWindowClient), $"Unity tool invocation cancelled sequence={envelope.SequenceNumber}");
            }
            catch (Exception exception)
            {
                _log.Error(nameof(ChatEditorWindowClient), $"Unity tool invocation failed sequence={envelope.SequenceNumber}", exception);
                EnqueueUpdate(new ChatShowErrorUpdate("Unity tool invocation failed.", exception.ToString()));
            }
        }

        private void AcceptServiceEvent(UnityContext context, AgentServiceEventEnvelope envelope)
        {
            if (envelope == null || envelope.SequenceNumber <= 0)
            {
                return;
            }

            _lastReceivedGlobalEventId = Math.Max(_lastReceivedGlobalEventId ?? 0, envelope.SequenceNumber);
            _cursorStore.Save(context.Paths, _streamGenerationId, _lastReceivedGlobalEventId);
        }

        private bool IsActiveSession(string sessionId)
        {
            return
                !string.IsNullOrWhiteSpace(_activeSession.SessionId)
                && !string.IsNullOrWhiteSpace(sessionId)
                && string.Equals(sessionId, _activeSession.SessionId, StringComparison.Ordinal);
        }

        private void StartSessionRequestRefreshIfNeeded(UnityContext context)
        {
            var now = DateTimeOffset.UtcNow;
            if (context?.IsProviderValid != true)
            {
                if (now >= _nextSessionRequestRefreshUtc)
                {
                    _activeSession.ClearRequestSignature();
                    _nextSessionRequestRefreshUtc = now.AddMilliseconds(500);
                    EnqueueSelectedModelLabelUpdate(context);
                }

                return;
            }

            if (_isShowingSessions)
            {
                if (_isRefreshingSessionRequest || _isHydratingHistory || now < _nextSessionRequestRefreshUtc)
                {
                    return;
                }

                _nextSessionRequestRefreshUtc = now.AddMilliseconds(500);
                EnqueueSelectedModelLabelUpdate(context);
                return;
            }

            if (_isRefreshingSessionRequest || _activeSession.IsBusy || _isHydratingHistory || now < _nextSessionRequestRefreshUtc)
            {
                return;
            }

            _isRefreshingSessionRequest = true;
            _nextSessionRequestRefreshUtc = now.AddMilliseconds(500);
            _ = RefreshSessionRequestAsync(context);
        }

        private async Task RefreshSessionRequestAsync(UnityContext context)
        {
            try
            {
                await EnsureSessionRequestAppliedAsync(context, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _log.Warning(nameof(ChatEditorWindowClient), $"Session request refresh failed. error={exception.Message}");
            }
            finally
            {
                _isRefreshingSessionRequest = false;
            }
        }

        private async Task<bool> EnsureSessionRequestAppliedAsync(UnityContext context, CancellationToken cancellationToken)
        {
            EnqueueSelectedModelLabelUpdate(context);
            if (!ValidateProvider(context, out _))
            {
                _activeSession.ClearRequestSignature();
                return false;
            }

            var sessionRequestSignature = CreateSessionRequestSignature(context);

            if (string.Equals(sessionRequestSignature, _activeSession.RequestSignature, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(_activeSession.SessionId))
            {
                _activeSession.SetRequestSignature(sessionRequestSignature);
                return true;
            }

            try
            {
                EnqueueUpdate(new ChatSetBusyStateUpdate(true));
                var session = await _service.OpenSessionAsync(context, _activeSession.SessionId, cancellationToken).ConfigureAwait(false);
                var resolvedSessionId = string.IsNullOrWhiteSpace(session.SessionId) ? _activeSession.SessionId : session.SessionId;
                SetActiveSession(resolvedSessionId, sessionRequestSignature, session.Status);
                EnsureEventStreamStarted(context);

                _log.Info(nameof(ChatEditorWindowClient), $"Reconfigured active session after session-bound settings changed sessionId={_activeSession.SessionId}");
                return true;
            }
            catch (Exception exception)
            {
                EnsureEventStreamStarted(context);
                _log.Error(nameof(ChatEditorWindowClient), $"Failed to reopen active session after session-bound settings changed sessionId={_activeSession.SessionId}", exception);
                EnqueueUpdate(new ChatShowErrorUpdate(exception.Message, exception.ToString()));
                return false;
            }
            finally
            {
                EnqueueUpdate(new ChatSetBusyStateUpdate(_activeSession.IsBusy));
            }
        }

        private void EnqueueSelectedModelLabelUpdate(UnityContext context)
        {
            if (context?.IsProviderValid != true)
            {
                EnqueueUpdate(new ChatSetModelLabelUpdate(ProviderConfigDto.Empty.DisplayName));
                return;
            }

            var modelSelection = _service.GetSelectedModel(context);
            EnqueueUpdate(new ChatSetModelLabelUpdate(modelSelection.DisplayName));
        }

        private ProviderConfigDto GetSelectedModelOrDefault(UnityContext context)
        {
            try
            {
                return _service.GetSelectedModel(context);
            }
            catch
            {
                return ProviderConfigDto.Empty;
            }
        }

        private delegate ChatClientCallResult ChatValidationFailure(params ChatClientUpdate[] updates);

        private bool ValidateProvider(UnityContext context, out ChatValidationFailure validationFailure)
        {
            validationFailure = null;
            if (context?.IsProviderValid == true)
            {
                return true;
            }

            var message = string.IsNullOrWhiteSpace(context?.ProviderValidationMessage)
                ? "Select a model in Unity Code Agent settings."
                : context.ProviderValidationMessage;
            _log.Warning(nameof(ChatEditorWindowClient), $"Chat request blocked by invalid provider settings. message={message}");
            validationFailure = updates =>
            {
                var allUpdates = new List<ChatClientUpdate>(updates ?? Array.Empty<ChatClientUpdate>())
                {
                    new ChatShowErrorUpdate(message, string.Empty)
                };
                return Failure(allUpdates);
            };
            return false;
        }

        private static string CreateSessionRequestSignature(UnityContext context)
            => SessionRequestFactory.CreateOptions(context).Signature;

        private static string TryCreateSessionRequestSignature(UnityContext context)
        {
            try
            {
                return CreateSessionRequestSignature(context);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void EnqueueUpdate(ChatClientUpdate update)
        {
            if (update != null)
            {
                _pendingClientUpdates.Enqueue(update);
            }
        }

        private void ClearQueuedProgressUpdates()
        {
            var retainedUpdates = new List<ChatClientUpdate>();
            while (_pendingClientUpdates.TryDequeue(out var update))
            {
                if (!(update is ChatShowProgressMessageUpdate))
                {
                    retainedUpdates.Add(update);
                }
            }

            foreach (var update in retainedUpdates)
            {
                _pendingClientUpdates.Enqueue(update);
            }
        }

        private void ShowProgressMessage(string message)
            => EnqueueUpdate(new ChatShowProgressMessageUpdate(message));

        private void SetActiveSession(string sessionId, string requestSignature, string status)
        {
            _activeSession.Set(sessionId, requestSignature, status);
        }

        private bool SetActiveSessionStatus(string status)
        {
            _activeSession.SetStatus(status);
            return _activeSession.IsBusy;
        }

        private void ClearActiveSession()
        {
            _activeSession.Clear();
        }

        private static ChatClientCallResult Success(params ChatClientUpdate[] updates)
        {
            return new ChatClientCallResult(true, updates ?? Array.Empty<ChatClientUpdate>());
        }

        private static ChatClientCallResult Success(IReadOnlyList<ChatClientUpdate> updates)
        {
            return new ChatClientCallResult(true, updates ?? Array.Empty<ChatClientUpdate>());
        }

        private static ChatClientCallResult Failure(params ChatClientUpdate[] updates)
        {
            return new ChatClientCallResult(false, updates ?? Array.Empty<ChatClientUpdate>());
        }

        private static ChatClientCallResult Failure(IReadOnlyList<ChatClientUpdate> updates)
        {
            return new ChatClientCallResult(false, updates ?? Array.Empty<ChatClientUpdate>());
        }
    }
}
