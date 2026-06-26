using System.Net.Http.Headers;
using System.Text.Json;
using GitHub.Copilot;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Settings;

namespace UnityCodeCopilot.Service.Copilot;

public sealed class ByokOpenAiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private UnityCodeCopilotServiceLogger _log;

    public ByokOpenAiProvider(UnityCodeCopilotServiceLogger log)
    {
        _log = log;
    }

    public ProviderConfig? ToProviderConfig(ProviderConfigDto? provider)
    {
        if (provider == null || !provider.HasByok)
        {
            return null;
        }

        return new ProviderConfig
        {
            BaseUrl = provider.BaseUrl!,
            ApiKey = provider.ApiKey,
        };
    }

    public async Task<IList<ModelInfo>> ListModelsAsync(ProviderConfigDto provider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
        {
            throw new InvalidOperationException("BYOK BaseUrl is required to list provider models.");
        }

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{provider.BaseUrl.Trim().TrimEnd('/')}/models");
        if (!string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureProviderResponseSuccess(response, body);

        var payload = JsonSerializer.Deserialize<OpenAiModelsResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("The BYOK model list response was empty.");

        var results = new List<ModelInfo>();
        if (payload.Data == null)
        {
            return results;
        }

        for (var index = 0; index < payload.Data.Count; index++)
        {
            var model = payload.Data[index];
            var modelId = (model?.Id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(modelId))
            {
                continue;
            }

            var modelInfo = new ModelInfo
            {
                Id = modelId,
                Name = string.IsNullOrWhiteSpace(model?.Name) ? modelId : model.Name.Trim(),
            };

            results.Add(modelInfo);
        }

        return results;
    }

    private static void EnsureProviderResponseSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body);
    }

    private sealed class OpenAiModelsResponse
    {
        public List<OpenAiModel>? Data { get; set; }
    }

    private sealed class OpenAiModel
    {
        public string? Id { get; set; }

        public string? Name { get; set; }
    }
}
