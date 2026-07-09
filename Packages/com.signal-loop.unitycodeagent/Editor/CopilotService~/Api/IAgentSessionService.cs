using SignalLoop.UnityCodeAgent.Contracts;

namespace UnityCodeCopilot.Service.Api;

public interface IAgentSessionService
{
    Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken);

    Task<AgentSessionResponseDto> CreateAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken);

    Task<AgentSessionResponseDto> OpenAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken);

    Task SendAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken);

    Task SteerScreenshotAsync(string sessionId, AgentToolBinaryResultDto screenshot, CancellationToken cancellationToken);

    Task AbortAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken);
}
