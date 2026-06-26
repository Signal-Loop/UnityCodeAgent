using GitHub.Copilot;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Api;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Settings;
using UnityCodeCopilot.Service.Telemetry;

namespace UnityCodeCopilot.Service.Copilot;

public sealed class CopilotModelCatalog : IAgentModelCatalog
{
    private readonly ByokOpenAiProvider _byokOpenAiProvider;
    private readonly CopilotClientHost _clientHost;
    private readonly UnityCodeCopilotServiceLogger _log;
    private readonly ProjectPaths _paths;
    private readonly CopilotTelemetry _telemetry;

    public CopilotModelCatalog(
        ByokOpenAiProvider byokOpenAiProvider,
        CopilotClientHost clientHost,
        UnityCodeCopilotServiceLogger log,
        ProjectPaths paths,
        CopilotTelemetry telemetry)
    {
        _byokOpenAiProvider = byokOpenAiProvider;
        _clientHost = clientHost;
        _log = log;
        _paths = paths;
        _telemetry = telemetry;
    }

    public Task<IReadOnlyList<ModelInfoDto>> ListModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
        => _telemetry.ExecuteAsync(TelemetryOperations.SdkModelsList, async operation =>
        {
            IReadOnlyList<ModelInfo> models;

            if (request.Provider?.HasByok != true)
            {
                models = await _clientHost.ListRuntimeModelsAsync(cancellationToken);
            }
            else
            {
                try
                {
                    await using var byokClient = new CopilotClient(new CopilotClientOptions
                    {
                        WorkingDirectory = _paths.ProjectRoot,
                        OnListModels = ct => _byokOpenAiProvider.ListModelsAsync(request.Provider, ct),
                    });

                    models = (await byokClient.ListModelsAsync(cancellationToken)).ToArray();
                }
                catch (Exception exception) when (CopilotAuthFailureClassifier.IsAuthenticationFailure(exception))
                {
                    throw CopilotAuthFailureClassifier.CreateAuthenticationException(request.Provider, exception);
                }
            }

            operation.SetTags(
                ("byok.enabled", request.Provider?.HasByok == true),
                ("model.count", models.Count));

            _log.Info(nameof(CopilotModelCatalog), "Listed available models.", ("count", models.Count), ("byokEnabled", request.Provider?.HasByok == true));

            return (IReadOnlyList<ModelInfoDto>)models.Select(ToModelInfoDto).ToArray();
        });

    private static ModelInfoDto ToModelInfoDto(ModelInfo model)
        => new(model.Id, string.IsNullOrWhiteSpace(model.Name) ? model.Id : model.Name);

}
