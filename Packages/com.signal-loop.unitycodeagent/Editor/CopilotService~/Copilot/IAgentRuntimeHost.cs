using GitHub.Copilot;
using SignalLoop.UnityCodeAgent.Contracts;

namespace UnityCodeCopilot.Service.Copilot;

public interface IAgentRuntimeHost
{
    Task<AgentRuntimeAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken);

    Task<IAgentRuntimeSession> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken);

    Task<IAgentRuntimeSession> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken);
}

public sealed record AgentRuntimeAuthStatus(bool IsAuthenticated, string? StatusMessage = null, string? AuthType = null, string? Login = null);

public interface IAgentRuntimeSession : IAsyncDisposable
{
    string SessionId { get; }

    Task<IReadOnlyList<SessionEvent>> GetEventsAsync(CancellationToken cancellationToken);

    IDisposable OnSessionEvent(Action<SessionEvent> handler);

    Task SendPromptAsync(string prompt, CancellationToken cancellationToken);

    Task SteerScreenshotAsync(string base64Data, string mimeType, CancellationToken cancellationToken);

    Task AbortAsync(CancellationToken cancellationToken);
}
