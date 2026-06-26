using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Contracts;

namespace SignalLoop.UnityCodeAgent.Interfaces
{
    public interface IAgentServiceApiClient
    {
        Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken);

        Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken);

        Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken);

        Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken);

        Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken);

        Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken);

        Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken);
    }
}
