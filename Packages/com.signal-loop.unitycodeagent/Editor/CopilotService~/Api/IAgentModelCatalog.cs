using SignalLoop.UnityCodeAgent.Contracts;

namespace UnityCodeCopilot.Service.Api;

public interface IAgentModelCatalog
{
    Task<IReadOnlyList<ModelInfoDto>> ListModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken);
}