using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Service.Mock;
using SignalLoop.UnityCodeAgent.Settings;
using SignalLoop.UnityCodeAgent.Tools;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class AgentService
    {
        private static readonly SemaphoreSlim ServiceLifecycleGate = new SemaphoreSlim(1, 1);

        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();
        private readonly IServiceBootstrap _bootstrap;
        private readonly IServiceBootstrap _mockBootstrap;
        private readonly Func<UnityContext, EndpointManifest, IAgentServiceApiClient> _apiClientFactory;
        private readonly Func<UnityContext, EndpointManifest, IAgentServiceEventStreamClient> _eventStreamClientFactory;
        private readonly Func<UnityContext, EndpointManifest> _manifestLoader;
        private readonly Action<string> _showProgressMessage;

        public AgentService(Action<string> showProgressMessage = null)
            : this(
                new ServiceBootstrap(),
                CreateMockBootstrap(),
                (context, manifest) => context.MockAgentService
                    ? CreateMockApiClient(manifest)
                    : new HttpAgentServiceApiClient(manifest),
                (context, manifest) => context.MockAgentService
                    ? CreateMockEventStreamClient(manifest)
                    : new SseAgentServiceEventStreamClient(manifest),
                LoadManifest,
                showProgressMessage)
        {
        }

        public AgentService(
            IServiceBootstrap bootstrap,
            IServiceBootstrap mockBootstrap,
            Func<UnityContext, EndpointManifest, IAgentServiceApiClient> apiClientFactory,
            Func<UnityContext, EndpointManifest, IAgentServiceEventStreamClient> eventStreamClientFactory,
            Func<UnityContext, EndpointManifest> manifestLoader,
            Action<string> showProgressMessage = null)
        {
            _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
            _mockBootstrap = mockBootstrap ?? throw new ArgumentNullException(nameof(mockBootstrap));
            _apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));
            _eventStreamClientFactory = eventStreamClientFactory ?? throw new ArgumentNullException(nameof(eventStreamClientFactory));
            _manifestLoader = manifestLoader ?? throw new ArgumentNullException(nameof(manifestLoader));
            _showProgressMessage = showProgressMessage ?? (_ => { });
        }

        private static IServiceBootstrap CreateMockBootstrap()
            => new MockServiceBootstrap();

        private static IAgentServiceApiClient CreateMockApiClient(EndpointManifest manifest)
            => new MockAgentServiceApiClient(manifest);

        private static IAgentServiceEventStreamClient CreateMockEventStreamClient(EndpointManifest manifest)
            => new MockAgentServiceEventStreamClient(manifest);

        public void StartInBackground(UnityContext context)
            => _ = StartCoreAsync(context, "StartInBackground", rethrowOnFailure: false);

        public async Task<EndpointManifest> StartAsync(UnityContext context)
            => await StartCoreAsync(context, "StartAsync", true).ConfigureAwait(false);

        public void RestartInBackground(UnityContext context)
            => _ = RestartCoreAsync(context, CancellationToken.None, "RestartInBackground", rethrowOnFailure: false);

        public async Task<EndpointManifest> RestartAsync(UnityContext context, CancellationToken cancellationToken = default)
            => await RestartCoreAsync(context, cancellationToken, "RestartAsync", rethrowOnFailure: true).ConfigureAwait(false);

        private async Task<EndpointManifest> StartCoreAsync(UnityContext context, string operationName, bool rethrowOnFailure)
        {
            await ServiceLifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return await StartCoreAsyncWithGateHeld(context, operationName, rethrowOnFailure).ConfigureAwait(false);
            }
            finally
            {
                ServiceLifecycleGate.Release();
            }
        }

        private async Task<EndpointManifest> RestartCoreAsync(UnityContext context, CancellationToken cancellationToken, string operationName, bool rethrowOnFailure)
        {
            var totalStopwatch = Stopwatch.StartNew();
            _log.Info(nameof(AgentService), $"{operationName} begin");
            _showProgressMessage("Restarting agent service...");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ServiceLifecycleGate.Wait(0))
                {
                    _log.Info(nameof(AgentService), $"{operationName} skipped elapsedMs={totalStopwatch.ElapsedMilliseconds} reason=lifecycle_operation_in_progress");
                    _showProgressMessage("Agent service restart is already in progress.");
                    return null;
                }

                try
                {
                    Stop(context);
                    cancellationToken.ThrowIfCancellationRequested();
                    var manifest = await StartCoreAsyncWithGateHeld(context, operationName, rethrowOnFailure: true).ConfigureAwait(false);
                    _log.Info(nameof(AgentService), $"{operationName} completed elapsedMs={totalStopwatch.ElapsedMilliseconds}");
                    return manifest;
                }
                finally
                {
                    ServiceLifecycleGate.Release();
                }
            }
            catch (Exception exception)
            {
                _log.Error(nameof(AgentService), $"{operationName} failed elapsedMs={totalStopwatch.ElapsedMilliseconds}", exception);
                _showProgressMessage("Agent service failed to restart.");
                if (rethrowOnFailure)
                {
                    throw;
                }

                return null;
            }
        }

        private async Task<EndpointManifest> StartCoreAsyncWithGateHeld(UnityContext context, string operationName, bool rethrowOnFailure)
        {
            var paths = context.Paths;
            var totalStopwatch = Stopwatch.StartNew();
            _log.Info(nameof(AgentService), $"{operationName} begin");
            _showProgressMessage("Starting agent service...");

            try
            {
                var pathsStopwatch = Stopwatch.StartNew();
                _log.Info(nameof(AgentService), $"{operationName} paths ready elapsedMs={pathsStopwatch.ElapsedMilliseconds} projectRoot={paths.ProjectRoot}");

                var bootstrapStopwatch = Stopwatch.StartNew();
                var bootstrap = context.MockAgentService ? _mockBootstrap : _bootstrap;
                var manifest = await bootstrap.ConnectOrStartAsync(context).ConfigureAwait(false);
                var baseUrl = $"http://127.0.0.1:{manifest.Port}";
                _log.Info(nameof(AgentService), $"{operationName} bootstrap completed elapsedMs={bootstrapStopwatch.ElapsedMilliseconds} baseUrl={baseUrl} port={manifest.Port} serviceProcessId={manifest.ServiceProcessId}");
                _log.Info(nameof(AgentService), $"{operationName} completed elapsedMs={totalStopwatch.ElapsedMilliseconds}");
                return manifest;
            }
            catch (Exception exception)
            {
                _log.Error(nameof(AgentService), $"{operationName} failed elapsedMs={totalStopwatch.ElapsedMilliseconds}", exception);
                _showProgressMessage("Agent service failed to start.");
                if (rethrowOnFailure)
                {
                    throw;
                }

                return null;
            }
        }

        public bool Stop(UnityContext context)
        {
            var totalStopwatch = Stopwatch.StartNew();
            _log.Trace(nameof(AgentService), "Stop begin");

            var paths = context.Paths;
            var manifestStopwatch = Stopwatch.StartNew();
            var manifest = _manifestLoader(context);
            _log.Debug(nameof(AgentService), $"Stop manifest ready elapsedMs={manifestStopwatch.ElapsedMilliseconds} projectRoot={paths.ProjectRoot} serviceProcessId={(manifest == null ? 0 : manifest.ServiceProcessId)}");

            if (manifest == null || manifest.ServiceProcessId <= 0)
            {
                _log.Warning(nameof(AgentService), $"Stop no-op elapsedMs={totalStopwatch.ElapsedMilliseconds} reason=no_manifest");
                _log.Trace(nameof(AgentService), $"Stop completed elapsedMs={totalStopwatch.ElapsedMilliseconds}");
                return false;
            }

            var killStopwatch = Stopwatch.StartNew();
            var stopped = KillProcess(manifest.ServiceProcessId);
            _log.Info(nameof(AgentService), $"Stop kill completed elapsedMs={killStopwatch.ElapsedMilliseconds} serviceProcessId={manifest.ServiceProcessId} stopped={stopped}");
            _log.Trace(nameof(AgentService), $"Stop completed elapsedMs={totalStopwatch.ElapsedMilliseconds}");
            return stopped;
        }

        public string Status(UnityContext context)
        {
            var totalStopwatch = Stopwatch.StartNew();
            _log.Trace(nameof(AgentService), "Status begin");

            var paths = context.Paths;
            var manifest = _manifestLoader(context);
            var serviceProcessId = manifest?.ServiceProcessId ?? 0;
            var isRunning = serviceProcessId > 0 && IsProcessAlive(serviceProcessId);

            var status = JsonConvert.SerializeObject(new
            {
                paths.ProjectRoot,
                ManifestPath = paths.EndpointManifestPath,
                HasManifest = manifest != null,
                IsRunning = isRunning,
                BaseUrl = manifest == null || manifest.Port <= 0 ? string.Empty : $"http://127.0.0.1:{manifest.Port}",
                ServiceProcessId = serviceProcessId,
                Port = manifest?.Port ?? 0,
                UnityProcessId = manifest?.UnityProcessId ?? 0,
                ProjectId = manifest?.ProjectId ?? string.Empty,
                manifest?.StartedAtUtc,
                StreamGenerationId = manifest?.StreamGenerationId ?? string.Empty,
            }, Formatting.Indented);

            _log.Info(nameof(AgentService), $"Status result {status}");
            _log.Trace(nameof(AgentService), $"Status completed elapsedMs={totalStopwatch.ElapsedMilliseconds}");
            return status;
        }

        public async Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(UnityContext context, CancellationToken cancellationToken = default)
        {
            var totalStopwatch = Stopwatch.StartNew();
            _log.Trace(nameof(AgentService), "GetSessionsAsync begin");
            _showProgressMessage("Loading chat sessions...");

            var manifest = await EnsureManifestAsync(context).ConfigureAwait(false);
            IReadOnlyList<SessionSummaryDto> sessions;

            try
            {
                sessions = await _apiClientFactory(context, manifest).GetSessionsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (ShouldReconnectForCurrentSession(exception))
            {
                _log.Warning(nameof(AgentService), $"GetSessionsAsync failed against the current manifest; retrying after service start. error={exception.GetType().Name}");
                _showProgressMessage("Agent service connection lost. Restarting service connection...");
                var restartedManifest = await StartAsync(context).ConfigureAwait(false);
                _showProgressMessage("Agent service connection restored.");
                sessions = await _apiClientFactory(context, restartedManifest).GetSessionsAsync(cancellationToken).ConfigureAwait(false);
            }

            _log.Debug(nameof(AgentService), $"GetSessionsAsync result sessions={sessions.Count}");
            _log.Trace(nameof(AgentService), $"GetSessionsAsync completed elapsedMs={totalStopwatch.ElapsedMilliseconds}");
            return sessions;
        }

        public async Task<AgentSessionResponseDto> GetCurrentSessionAsync(UnityContext context, CancellationToken cancellationToken = default)
        {
            var totalStopwatch = Stopwatch.StartNew();
            _log.Trace(nameof(AgentService), "GetCurrentSessionAsync begin");
            _showProgressMessage("Loading current chat session...");

            var requestOptions = CreateSessionRequestOptions(context);
            ShowAuthenticationProgress(requestOptions.Provider);
            var manifest = _manifestLoader(context);
            if (!IsUsableManifest(manifest))
            {
                _log.Info(nameof(AgentService), "Current session requested without a usable manifest; starting service first.");
                manifest = await StartAsync(context).ConfigureAwait(false);
            }

            try
            {
                var history = await OpenCurrentSessionAsync(context, manifest, requestOptions, totalStopwatch, cancellationToken).ConfigureAwait(false);
                _log.Debug(nameof(AgentService), $"GetCurrentSessionAsync result sessionId={history.SessionId} messages={history.Messages.Count}");
                _log.Trace(nameof(AgentService), $"GetCurrentSessionAsync completed elapsedMs={totalStopwatch.ElapsedMilliseconds}");
                return history;
            }
            catch (Exception exception) when (ShouldReconnectForCurrentSession(exception))
            {
                _log.Debug(nameof(AgentService), $"Current session load failed against the current manifest; retrying after service start. error={exception.GetType().Name}");
                _showProgressMessage("Agent service connection lost. Restarting service connection...");
                var restartedManifest = await StartAsync(context).ConfigureAwait(false);
                _showProgressMessage("Agent service connection restored.");
                var history = await OpenCurrentSessionAsync(context, restartedManifest, requestOptions, totalStopwatch, cancellationToken).ConfigureAwait(false);
                _log.Debug(nameof(AgentService), $"GetCurrentSessionAsync result sessionId={history.SessionId} messages={history.Messages.Count}");
                _log.Trace(nameof(AgentService), $"GetCurrentSessionAsync completed elapsedMs={totalStopwatch.ElapsedMilliseconds}");
                return history;
            }
        }

        public async Task<AgentSessionResponseDto> OpenSessionAsync(UnityContext context, string sessionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentNullException(nameof(sessionId));
            }

            var requestOptions = CreateSessionRequestOptions(context);
            ShowAuthenticationProgress(requestOptions.Provider);
            var openRequest = SessionRequestFactory.CreateOpenSessionRequest(requestOptions, sessionId);
            var manifest = await EnsureManifestAsync(context).ConfigureAwait(false);
            _showProgressMessage("Opening chat session...");

            try
            {
                return await OpenSessionAsync(_apiClientFactory(context, manifest), openRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (ShouldReconnectForCurrentSession(exception))
            {
                _log.Warning(nameof(AgentService), $"OpenSessionAsync failed against the current manifest; retrying after service start. sessionId={sessionId} error={exception.GetType().Name}");
                _showProgressMessage("Agent service connection lost. Restarting service connection...");
                var restartedManifest = await StartAsync(context).ConfigureAwait(false);
                _showProgressMessage("Agent service connection restored.");
                return await OpenSessionAsync(_apiClientFactory(context, restartedManifest), openRequest, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<AgentSessionResponseDto> CreateSessionAsync(UnityContext context, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var sessionId = CreateSessionId(context.Paths, now);
            var request = SessionRequestFactory.CreateNewSessionRequest(CreateSessionRequestOptions(context), sessionId);
            _log.Info(nameof(AgentService), $"CreateSessionAsync begin sessionId={request.SessionId} model={request.Model}");
            ShowAuthenticationProgress(request.Provider);
            _showProgressMessage("Creating chat session...");
            var response = await ExecuteWithReconnectAsync(
                manifest => _apiClientFactory(context, manifest).CreateSessionAsync(request, cancellationToken),
                "CreateSessionAsync",
                $"sessionId={request.SessionId}",
                context).ConfigureAwait(false);
            _log.Debug(nameof(AgentService), $"CreateSessionAsync result sessionId={response.SessionId} status={response.Status}");
            return response;
        }

        public async Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(UnityContext context, ListAgentModelsRequestDto request, CancellationToken cancellationToken)
        {
            _log.Info(nameof(AgentService), $"GetModelsAsync begin byokEnabled={request.Provider?.HasByok == true}");
            ShowAuthenticationProgress(request.Provider);
            _showProgressMessage("Loading agent models...");
            var response = await ExecuteWithReconnectAsync(
                manifest => _apiClientFactory(context, manifest).GetModelsAsync(request, cancellationToken),
                "GetModelsAsync",
                $"byokEnabled={request.Provider?.HasByok == true}",
                context).ConfigureAwait(false);
            _log.Debug(nameof(AgentService), $"GetModelsAsync result count={response.Count}");
            return response;
        }

        public ProviderConfigDto GetSelectedModel(UnityContext context)
            => context?.Provider ?? ProviderConfigDto.Empty;

        public async Task<EndpointManifest> GetEndpointManifestAsync(UnityContext context)
            => await EnsureManifestAsync(context).ConfigureAwait(false);

        public async Task SendPromptAsync(UnityContext context, SendAgentPromptRequestDto request, CancellationToken cancellationToken)
        {
            _log.Info(nameof(AgentService), $"SendPromptAsync begin sessionId={request.SessionId} promptLength={request.Prompt.Length}");
            _showProgressMessage("Sending prompt...");
            await ExecuteSessionOperationWithRecoveryAsync(
                request.SessionId,
                client => client.SendPromptAsync(request, cancellationToken),
                "SendPromptAsync",
                $"sessionId={request.SessionId}",
                context,
                cancellationToken).ConfigureAwait(false);
            _log.Debug(nameof(AgentService), $"SendPromptAsync completed sessionId={request.SessionId}");
        }

        public async Task AbortPromptAsync(UnityContext context, AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
        {
            _log.Info(nameof(AgentService), $"AbortPromptAsync begin sessionId={request.SessionId}");
            _showProgressMessage("Stopping response...");
            await ExecuteSessionOperationWithRecoveryAsync(
                request.SessionId,
                client => client.AbortPromptAsync(request, cancellationToken),
                "AbortPromptAsync",
                $"sessionId={request.SessionId}",
                context,
                cancellationToken).ConfigureAwait(false);
            _log.Debug(nameof(AgentService), $"AbortPromptAsync completed sessionId={request.SessionId}");
        }

        public async Task ExecuteToolInvocationRequestAsync(UnityContext context, AgentServiceEventEnvelope envelope, CancellationToken cancellationToken)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            var request = JsonConvert.DeserializeObject<AgentToolInvocationRequestDto>(envelope.SourceJson);
            if (request == null)
            {
                throw new InvalidOperationException("Unity tool invocation event did not contain a valid request payload.");
            }

            _log.Info(nameof(AgentService), $"ExecuteToolInvocationRequestAsync begin sessionId={request.SessionId} toolName={request.ToolName} callId={request.CallId}");

            AgentToolInvocationResultDto result;
            try
            {
                result = await UnityEditorThread.RunAsync(
                    () => UnityAgentToolRegistry.Shared.ExecuteAsync(request, context),
                    cancellationToken).Unwrap().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _log.Error(nameof(AgentService), $"Unity tool execution failed toolName={request.ToolName} callId={request.CallId}", exception);
                result = new AgentToolInvocationResultDto(
                    request.CallId,
                    request.SessionId,
                    request.ToolName,
                    true,
                    exception.Message,
                    null,
                    exception.Message);
            }

            await SendToolInvocationResultAsync(context, result, cancellationToken).ConfigureAwait(false);
            _log.Info(nameof(AgentService), $"ExecuteToolInvocationRequestAsync completed sessionId={request.SessionId} toolName={request.ToolName} callId={request.CallId} isError={result.IsError}");
        }

        public async Task SendToolInvocationResultAsync(UnityContext context, AgentToolInvocationResultDto request, CancellationToken cancellationToken)
        {
            _log.Info(nameof(AgentService), $"SendToolInvocationResultAsync begin sessionId={request.SessionId} toolName={request.ToolName} callId={request.CallId} isError={request.IsError}");
            await ExecuteSessionOperationWithRecoveryAsync(
                request.SessionId,
                client => client.SendToolInvocationResultAsync(request, cancellationToken),
                "SendToolInvocationResultAsync",
                $"sessionId={request.SessionId} toolName={request.ToolName} callId={request.CallId}",
                context,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task StreamEventsAsync(
            UnityContext context,
            Action<AgentServiceEventEnvelope> onEvent,
            CancellationToken cancellationToken,
            Func<long?> getLastAcceptedSequenceNumber = null,
            Action<EndpointManifest> onManifestChanged = null)
        {
            if (onEvent == null)
            {
                throw new ArgumentNullException(nameof(onEvent));
            }

            var manifest = await EnsureManifestAsync(context).ConfigureAwait(false);
            string lastSessionId = string.Empty;
            onManifestChanged?.Invoke(manifest);

            while (true)
            {
                try
                {
                    var lastEventId = getLastAcceptedSequenceNumber == null
                        ? null
                        : getLastAcceptedSequenceNumber();
                    _log.Info(nameof(AgentService), $"StreamEventsAsync begin port={manifest.Port} lastEventId={(lastEventId.HasValue ? lastEventId.Value.ToString(CultureInfo.InvariantCulture) : "null")}");
                    await _eventStreamClientFactory(context, manifest).StreamEventsAsync(envelope =>
                    {
                        if (envelope != null && !string.IsNullOrWhiteSpace(envelope.SessionId))
                        {
                            lastSessionId = envelope.SessionId;
                        }

                        onEvent(envelope);
                    }, lastEventId, cancellationToken).ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    _log.Warning(nameof(AgentService), $"StreamEventsAsync ended without cancellation; retrying after service start. lastEventId={(lastEventId.HasValue ? lastEventId.Value.ToString(CultureInfo.InvariantCulture) : "null")}");
                    _showProgressMessage("Agent service event stream ended. Restarting service connection...");
                    PublishSessionReadyEvent(onEvent, lastSessionId);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested && ShouldReconnectForCurrentSession(exception))
                {
                    var lastEventId = getLastAcceptedSequenceNumber == null
                        ? null
                        : getLastAcceptedSequenceNumber();
                    _log.Warning(nameof(AgentService), $"StreamEventsAsync failed against the current manifest; retrying after service start. error={exception.GetType().Name} lastEventId={(lastEventId.HasValue ? lastEventId.Value.ToString(CultureInfo.InvariantCulture) : "null")}");
                    _showProgressMessage("Agent service connection lost. Restarting service connection...");
                    PublishSessionReadyEvent(onEvent, lastSessionId);
                }

                var previousGenerationId = manifest.StreamGenerationId ?? string.Empty;
                manifest = await StartAsync(context).ConfigureAwait(false);
                if (!string.Equals(previousGenerationId, manifest.StreamGenerationId ?? string.Empty, StringComparison.Ordinal))
                {
                    onManifestChanged?.Invoke(manifest);
                }
                _showProgressMessage("Agent service connection restored.");
            }
        }

        private async Task<AgentSessionResponseDto> OpenCurrentSessionAsync(UnityContext context, EndpointManifest manifest, SessionRequestFactory.SessionRequestOptions requestOptions, Stopwatch totalStopwatch, CancellationToken cancellationToken)
        {
            _log.Debug(nameof(AgentService), $"OpenCurrentSessionAsync using manifest port={manifest.Port} serviceProcessId={manifest.ServiceProcessId}");
            var client = _apiClientFactory(context, manifest);
            var sessions = await client.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
            if (sessions.Count == 0)
            {
                throw new InvalidOperationException("Copilot service snapshot did not contain any sessions.");
            }

            var currentSession = sessions[0];
            _log.Info(nameof(AgentService), $"Selected current session sessionId={currentSession.SessionId} sessionSummary={currentSession.Summary}");
            return await OpenSessionAsync(client, SessionRequestFactory.CreateOpenSessionRequest(requestOptions, currentSession.SessionId), cancellationToken, totalStopwatch).ConfigureAwait(false);
        }

        private static bool ShouldReconnectForCurrentSession(Exception exception)
            => exception is HttpRequestException or TaskCanceledException or WebException or IOException;

        private static bool IsSessionUnavailable(Exception exception)
            => exception is AgentServiceApiException apiException
                && (string.Equals(apiException.ErrorCode, AgentServiceErrorCodes.SessionUnavailable, StringComparison.Ordinal)
                    || apiException.StatusCode == HttpStatusCode.NotFound);

        private void ShowAuthenticationProgress(ProviderConfigDto provider)
            => _showProgressMessage(provider?.HasByok == true
                ? "Checking BYOK provider credentials..."
                : "Checking GitHub Copilot authentication...");

        private static void PublishSessionReadyEvent(Action<AgentServiceEventEnvelope> onEvent, string sessionId)
        {
            onEvent(CreateLocalEvent(sessionId ?? string.Empty, DateTimeOffset.UtcNow, "ready", AgentEventType.SessionStatusChanged));
        }

        private static AgentServiceEventEnvelope CreateLocalEvent(string sessionId, DateTimeOffset timestampUtc, string content, AgentEventType type)
        {
            return new AgentServiceEventEnvelope(
                0,
                sessionId,
                timestampUtc,
                content,
                null,
                type,
                string.Empty,
                false);
        }

        private async Task<EndpointManifest> EnsureManifestAsync(UnityContext context)
        {
            var manifest = _manifestLoader(context);
            if (IsUsableManifest(manifest))
            {
                return manifest;
            }

            _log.Info(nameof(AgentService), "Service request received without a usable manifest; starting service first.");
            return await StartAsync(context).ConfigureAwait(false);
        }

        private static bool IsUsableManifest(EndpointManifest manifest)
            => manifest != null && (manifest.Port > 0 || manifest.ServiceProcessId < 0);

        private async Task<AgentSessionResponseDto> OpenSessionAsync(IAgentServiceApiClient client, OpenAgentSessionRequestDto openRequest, CancellationToken cancellationToken, Stopwatch stopwatch = null)
        {
            var openResponse = await client.OpenSessionAsync(openRequest, cancellationToken).ConfigureAwait(false);
            _log.Trace(nameof(AgentService), $"OpenSessionAsync session opened sessionId={openResponse.SessionId} messages={openResponse.Messages.Count} elapsedMs={(stopwatch == null ? 0 : stopwatch.ElapsedMilliseconds)}");
            return openResponse;
        }

        private static SessionRequestFactory.SessionRequestOptions CreateSessionRequestOptions(UnityContext context)
            => SessionRequestFactory.CreateOptions(context);

        private async Task ExecuteSessionOperationWithRecoveryAsync(string sessionId, Func<IAgentServiceApiClient, Task> operation, string operationName, string operationContext, UnityContext context, CancellationToken cancellationToken)
        {
            var requestOptions = CreateSessionRequestOptions(context);
            ShowAuthenticationProgress(requestOptions.Provider);
            var openRequest = SessionRequestFactory.CreateOpenSessionRequest(requestOptions, sessionId);
            var manifest = await EnsureManifestAsync(context).ConfigureAwait(false);
            var client = _apiClientFactory(context, manifest);

            try
            {
                await operation(client).ConfigureAwait(false);
            }
            catch (Exception exception) when (ShouldReconnectForCurrentSession(exception))
            {
                _log.Warning(nameof(AgentService), $"{operationName} failed against the current manifest; restarting service, reopening session, and retrying. {operationContext} error={exception.GetType().Name}");
                _showProgressMessage("Agent service connection lost. Restarting service connection...");
                var restartedManifest = await StartAsync(context).ConfigureAwait(false);
                _showProgressMessage("Agent service connection restored.");
                _showProgressMessage("Reopening chat session...");
                var restartedClient = _apiClientFactory(context, restartedManifest);
                await OpenSessionAsync(restartedClient, openRequest, cancellationToken).ConfigureAwait(false);
                await operation(restartedClient).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsSessionUnavailable(exception))
            {
                _log.Warning(nameof(AgentService), $"{operationName} failed because the session is not attached; reopening session and retrying. {operationContext} error={exception.GetType().Name}");
                _showProgressMessage("Reopening chat session...");
                try
                {
                    await OpenSessionAsync(client, openRequest, cancellationToken).ConfigureAwait(false);
                    await operation(client).ConfigureAwait(false);
                }
                catch (Exception reopenException) when (ShouldReconnectForCurrentSession(reopenException))
                {
                    _log.Warning(nameof(AgentService), $"{operationName} session reopen failed against the current manifest; restarting service, reopening session, and retrying. {operationContext} error={reopenException.GetType().Name}");
                    _showProgressMessage("Agent service connection lost. Restarting service connection...");
                    var restartedManifest = await StartAsync(context).ConfigureAwait(false);
                    _showProgressMessage("Agent service connection restored.");
                    _showProgressMessage("Reopening chat session...");
                    var restartedClient = _apiClientFactory(context, restartedManifest);
                    await OpenSessionAsync(restartedClient, openRequest, cancellationToken).ConfigureAwait(false);
                    await operation(restartedClient).ConfigureAwait(false);
                }
            }
        }

        private async Task<T> ExecuteWithReconnectAsync<T>(Func<EndpointManifest, Task<T>> operation, string operationName, string operationContext, UnityContext context)
        {
            var manifest = await EnsureManifestAsync(context).ConfigureAwait(false);

            try
            {
                return await operation(manifest).ConfigureAwait(false);
            }
            catch (Exception exception) when (ShouldReconnectForCurrentSession(exception))
            {
                _log.Warning(nameof(AgentService), $"{operationName} failed against the current manifest; retrying after service start. {operationContext} error={exception.GetType().Name}");
                _showProgressMessage("Agent service connection lost. Restarting service connection...");
                var restartedManifest = await StartAsync(context).ConfigureAwait(false);
                _showProgressMessage("Agent service connection restored.");
                return await operation(restartedManifest).ConfigureAwait(false);
            }
        }

        private static string CreateSessionId(UnityCodeAgentPaths paths, DateTimeOffset timestampUtc)
        {
            return string.IsNullOrWhiteSpace(paths.SafeProjectRoot)
                ? $"UnityCodeAgentSession-{timestampUtc:yyyyMMddHHmmssfff}"
                : $"UnityCodeAgentSession-{timestampUtc:yyyyMMddHHmmssfff}-{paths.SafeProjectRoot}";
        }

        private static EndpointManifest LoadManifest(UnityContext context)
        {
            var paths = context.Paths;
            if (!File.Exists(paths.EndpointManifestPath))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<EndpointManifest>(File.ReadAllText(paths.EndpointManifestPath));
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static bool KillProcess(int processId)
        {
            if (processId <= 0)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return false;
                }

                process.Kill();
                process.WaitForExit(5000);
                return process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool IsProcessAlive(int processId)
        {
            if (processId <= 0)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

    }
}
