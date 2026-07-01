using System.Collections.Generic;
using GitHub.Copilot;
using Microsoft.Extensions.Hosting;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Api;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Settings;
using UnityCodeCopilot.Service.Telemetry;

namespace UnityCodeCopilot.Service.Copilot;

public sealed class CopilotClientHost : IHostedService, IAsyncDisposable, IAgentRuntimeHost
{
    private const string ClientName = "UnityCodeCopilot.Service";

    private readonly ByokOpenAiProvider _byokOpenAiProvider;
    private readonly UnityCodeCopilotServiceLogger _log;
    private readonly TelemetryConfigFactory _cliTelemetryConfigFactory;
    private readonly McpConfigLoader _mcpConfigLoader;
    private readonly ProjectPaths _paths;
    private readonly CopilotTelemetry _telemetry;
    private readonly AgentToolInvocationBridge _toolInvocationBridge;
    private CopilotClient? _client;

    public CopilotClientHost(
        ByokOpenAiProvider byokOpenAiProvider,
        UnityCodeCopilotServiceLogger log,
        McpConfigLoader mcpConfigLoader,
        AgentToolInvocationBridge toolInvocationBridge,
        ProjectPaths paths,
        TelemetryConfigFactory cliTelemetryConfigFactory,
        CopilotTelemetry telemetry)
    {
        _byokOpenAiProvider = byokOpenAiProvider;
        _log = log;
        _mcpConfigLoader = mcpConfigLoader;
        _toolInvocationBridge = toolInvocationBridge;
        _paths = paths;
        _cliTelemetryConfigFactory = cliTelemetryConfigFactory;
        _telemetry = telemetry;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var telemetry = _cliTelemetryConfigFactory.Create();

        return _telemetry.ExecuteAsync(TelemetryOperations.SdkClientStart, async operation =>
        {
            operation.SetTags(
                ("copilot.telemetry.enabled", telemetry != null),
                ("copilot.telemetry.exporter_type", telemetry?.ExporterType),
                ("copilot.telemetry.otlp_endpoint", telemetry?.OtlpEndpoint),
                ("copilot.telemetry.file_path", telemetry?.FilePath));

            _log.Info(nameof(CopilotClientHost), "Starting Copilot client host with telemetry.",
                ("enabled", telemetry != null),
                ("exporterType", telemetry?.ExporterType),
                ("otlpEndpoint", telemetry?.OtlpEndpoint),
                ("filePath", telemetry?.FilePath));

            var options = new CopilotClientOptions
            {
                WorkingDirectory = _paths.ProjectRoot,
            };

            if (telemetry != null)
            {
                options.Telemetry = telemetry;
            }

            _client ??= new CopilotClient(options);

            await _client.StartAsync(cancellationToken);
            _log.Info(nameof(CopilotClientHost), "Copilot client host started.");
        });
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client != null)
        {
            await _telemetry.ExecuteAsync(TelemetryOperations.SdkClientStop, async _ =>
            {
                _log.Info(nameof(CopilotClientHost), "Stopping Copilot client host.");
                await _client.DisposeAsync();
                _client = null;
                _log.Info(nameof(CopilotClientHost), "Copilot client host stopped.");
            });
        }
    }

    public Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.SdkSessionsList, async operation =>
        {
            var client = GetClient();
            var sessions = await client.ListSessionsAsync(cancellationToken: cancellationToken);

            operation.SetTag("session.count", sessions.Count);

            _log.Debug(nameof(CopilotClientHost), "Listed runtime sessions.", ("count", sessions.Count));

            return (IReadOnlyList<SessionMetadata>)sessions.ToArray();
        });

    public Task<AgentRuntimeAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.SdkAuthStatus, async operation =>
        {
            var client = GetClient();
            var status = await client.GetAuthStatusAsync(cancellationToken);

            operation.SetTags(
                ("copilot.authenticated", status.IsAuthenticated),
                ("copilot.auth_type", status.AuthType),
                ("copilot.auth_host", status.Host));

            _log.Info(nameof(CopilotClientHost), "Checked GitHub Copilot authentication status.",
                ("isAuthenticated", status.IsAuthenticated),
                ("authType", status.AuthType),
                ("login", status.Login));

            return new AgentRuntimeAuthStatus(status.IsAuthenticated, status.StatusMessage, status.AuthType, status.Login);
        });

    public Task<IReadOnlyList<ModelInfo>> ListRuntimeModelsAsync(CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.SdkModelsList, async operation =>
        {
            var client = GetClient();
            await EnsureGitHubCopilotAuthenticatedAsync(cancellationToken);
            var models = await client.ListModelsAsync(cancellationToken);

            operation.SetTag("model.count", models.Count);

            _log.Info(nameof(CopilotClientHost), "Listed runtime client models.", ("count", models.Count));

            return (IReadOnlyList<ModelInfo>)models.ToArray();
        });

    public Task<IAgentRuntimeSession> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.SdkSessionCreate, async operation =>
        {
            var client = GetClient();
            if (request.Provider?.HasByok != true)
            {
                await EnsureGitHubCopilotAuthenticatedAsync(cancellationToken);
            }

            operation.SetTags(
                ("gen_ai.conversation.id", request.SessionId),
                ("gen_ai.request.model", request.Model),
                ("session.streaming", request.Streaming));

            var mcp = await _mcpConfigLoader.LoadAsync(cancellationToken);

            operation.SetTag("mcp.server.count", mcp.Servers.Count);
            operation.SetTag("skill.directory.count", request.SkillDirectories?.Count ?? 0);
            operation.SetTag("skill.disabled.count", request.DisabledSkills?.Count ?? 0);
            operation.SetTag("unity.tool.count", request.Tools?.Count ?? 0);

            _log.Info(nameof(CopilotClientHost), "Creating runtime session.",
                ("sessionId", request.SessionId),
                ("model", request.Model),
                ("mcpServerCount", mcp.Servers.Count),
                ("unityToolCount", request.Tools?.Count ?? 0),
                ("skillDirectoryCount", request.SkillDirectories?.Count ?? 0),
                ("disabledSkillCount", request.DisabledSkills?.Count ?? 0),
                ("workingDirectory", request.WorkingDirectory));

            CopilotSession session;
            try
            {
                session = await client.CreateSessionAsync(new SessionConfig
                {
                    SessionId = request.SessionId,
                    ClientName = ClientName,
                    Model = request.Model,
                    Provider = _byokOpenAiProvider.ToProviderConfig(request.Provider),
                    Streaming = request.Streaming,
                    Tools = CreateUnityTools(request.Tools),
                    McpServers = CopilotSdkConfigMapper.ToMcpServers(mcp.Servers),
                    SkillDirectories = ToMutableList(request.SkillDirectories),
                    DisabledSkills = ToMutableList(request.DisabledSkills),
                    InfiniteSessions = ToInfiniteSessionConfig(request.InfiniteSessions),
                    OnPermissionRequest = PermissionHandler.ApproveAll,
                    WorkingDirectory = request.WorkingDirectory,
                }, cancellationToken);
            }
            catch (Exception exception) when (request.Provider?.HasByok == true && CopilotAuthFailureClassifier.IsAuthenticationFailure(exception))
            {
                throw CopilotAuthFailureClassifier.CreateAuthenticationException(request.Provider, exception);
            }

            return (IAgentRuntimeSession)new CopilotRuntimeSession(session);
        });

    public Task<IAgentRuntimeSession> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.SdkSessionResume, async operation =>
        {
            var client = GetClient();
            if (request.Provider?.HasByok != true)
            {
                await EnsureGitHubCopilotAuthenticatedAsync(cancellationToken);
            }

            operation.SetTags(
                ("gen_ai.conversation.id", request.SessionId),
                ("gen_ai.request.model", request.Model),
                ("session.streaming", request.Streaming));

            var mcp = await _mcpConfigLoader.LoadAsync(cancellationToken);

            operation.SetTag("mcp.server.count", mcp.Servers.Count);
            operation.SetTag("skill.directory.count", request.SkillDirectories?.Count ?? 0);
            operation.SetTag("skill.disabled.count", request.DisabledSkills?.Count ?? 0);
            operation.SetTag("unity.tool.count", request.Tools?.Count ?? 0);

            _log.Info(nameof(CopilotClientHost), "Resuming runtime session.",
                ("sessionId", request.SessionId),
                ("model", request.Model),
                ("mcpServerCount", mcp.Servers.Count),
                ("unityToolCount", request.Tools?.Count ?? 0),
                ("skillDirectoryCount", request.SkillDirectories?.Count ?? 0),
                ("disabledSkillCount", request.DisabledSkills?.Count ?? 0),
                ("workingDirectory", request.WorkingDirectory));

            CopilotSession session;
            try
            {
                session = await client.ResumeSessionAsync(request.SessionId, new ResumeSessionConfig
                {
                    ClientName = ClientName,
                    Model = request.Model,
                    Provider = _byokOpenAiProvider.ToProviderConfig(request.Provider),
                    Streaming = request.Streaming,
                    Tools = CreateUnityTools(request.Tools),
                    McpServers = CopilotSdkConfigMapper.ToMcpServers(mcp.Servers),
                    SkillDirectories = ToMutableList(request.SkillDirectories),
                    DisabledSkills = ToMutableList(request.DisabledSkills),
                    InfiniteSessions = ToInfiniteSessionConfig(request.InfiniteSessions),
                    OnPermissionRequest = PermissionHandler.ApproveAll,
                    WorkingDirectory = request.WorkingDirectory,
                }, cancellationToken);
            }
            catch (Exception exception) when (request.Provider?.HasByok == true && CopilotAuthFailureClassifier.IsAuthenticationFailure(exception))
            {
                throw CopilotAuthFailureClassifier.CreateAuthenticationException(request.Provider, exception);
            }

            return (IAgentRuntimeSession)new CopilotRuntimeSession(session);
        });

    public async ValueTask DisposeAsync()
    {
        if (_client != null)
        {
            _log.Info(nameof(CopilotClientHost), "Disposing Copilot client host.");
            await _client.DisposeAsync();
            _client = null;
        }
    }

    private CopilotClient GetClient()
        => _client ?? throw new InvalidOperationException("Copilot client host has not been started.");

    private async Task EnsureGitHubCopilotAuthenticatedAsync(CancellationToken cancellationToken)
    {
        AgentRuntimeAuthStatus status;
        try
        {
            status = await GetAuthStatusAsync(cancellationToken);
        }
        catch (Exception exception) when (CopilotAuthFailureClassifier.IsAuthenticationFailure(exception))
        {
            throw CopilotAuthFailureClassifier.CreateAuthenticationException(null, exception);
        }

        if (!status.IsAuthenticated)
        {
            throw new AgentServiceAuthenticationException(AgentServiceAuthMessages.ForProvider(null));
        }
    }

    private static List<string>? ToMutableList(IReadOnlyList<string>? values)
        => values == null ? null : new List<string>(values);

    private List<Microsoft.Extensions.AI.AIFunctionDeclaration>? CreateUnityTools(IReadOnlyList<AgentToolDefinitionDto>? tools)
        => tools == null || tools.Count == 0
            ? null
            : tools.Select(tool => (Microsoft.Extensions.AI.AIFunctionDeclaration)new UnityAgentToolFunction(tool, _toolInvocationBridge)).ToList();

    private static InfiniteSessionConfig ToInfiniteSessionConfig(InfiniteSessionsDto infiniteSessions)
        => new()
        {
            Enabled = infiniteSessions.Enabled,
            BackgroundCompactionThreshold = infiniteSessions.BackgroundCompactionThreshold,
            BufferExhaustionThreshold = infiniteSessions.BufferExhaustionThreshold,
        };

    private sealed class CopilotRuntimeSession : IAgentRuntimeSession
    {
        private const string EnqueueMode = "enqueue";
        private readonly CopilotSession _session;

        public CopilotRuntimeSession(CopilotSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public string SessionId => _session.SessionId;

        public Task<IReadOnlyList<SessionEvent>> GetEventsAsync(CancellationToken cancellationToken)
            => _session.GetEventsAsync(cancellationToken);

        public IDisposable OnSessionEvent(Action<SessionEvent> handler)
            => _session.On(handler);

        public Task SendPromptAsync(string prompt, CancellationToken cancellationToken)
            => _session.SendAsync(new MessageOptions
            {
                Prompt = prompt,
                Mode = EnqueueMode,
            }, cancellationToken);

        public Task AbortAsync(CancellationToken cancellationToken)
            => _session.AbortAsync(cancellationToken);

        public ValueTask DisposeAsync()
            => _session.DisposeAsync();
    }
}
