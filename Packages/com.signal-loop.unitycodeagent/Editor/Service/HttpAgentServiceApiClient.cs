using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Interfaces;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class HttpAgentServiceApiClient : IAgentServiceApiClient
    {
        private static readonly JsonSerializerSettings RequestSerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
        };
        private readonly HttpClient _httpClient;

        public HttpAgentServiceApiClient(EndpointManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            _httpClient = CreateHttpClient(manifest.Port);
        }

        public HttpAgentServiceApiClient(EndpointManifest manifest, HttpMessageHandler messageHandler)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            _httpClient = CreateHttpClient(manifest.Port, messageHandler);
        }

        public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
            => SendJsonAsync<IReadOnlyList<SessionSummaryDto>>(
                HttpMethod.Get,
                "/api/sessions",
                null,
                TimeSpan.FromSeconds(5),
                "Agent service snapshot response was empty.",
                cancellationToken);

        public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
            => SendJsonAsync<IReadOnlyList<ModelInfoDto>>(
                HttpMethod.Post,
                "/api/models",
                request,
                TimeSpan.FromSeconds(30),
                "Agent service models response was empty.",
                cancellationToken);

        public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
            => SendJsonAsync<AgentSessionResponseDto>(
                HttpMethod.Post,
                "/api/sessions/open",
                request,
                TimeSpan.FromSeconds(5),
                "Agent service open session response was empty.",
                cancellationToken);

        public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
            => SendJsonAsync<AgentSessionResponseDto>(
                HttpMethod.Post,
                "/api/sessions/create",
                request,
                TimeSpan.FromSeconds(30),
                "Agent service create session response was empty.",
                cancellationToken);

        public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
            => SendWithoutResponseAsync(HttpMethod.Post, "/api/sessions/send", request, TimeSpan.FromSeconds(30), cancellationToken);

        public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
            => SendWithoutResponseAsync(HttpMethod.Post, "/api/sessions/abort", request, TimeSpan.FromSeconds(30), cancellationToken);

        public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
            => SendWithoutResponseAsync(HttpMethod.Post, "/api/tools/results", request, TimeSpan.FromSeconds(30), cancellationToken);

        private async Task<T> SendJsonAsync<T>(
            HttpMethod method,
            string relativePath,
            object payload,
            TimeSpan timeout,
            string emptyResponseMessage,
            CancellationToken cancellationToken)
        {
            using var request = CreateRequest(method, relativePath, payload);
            using var timeoutCancellation = CreateRequestCancellationSource(cancellationToken, timeout);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCancellation.Token).ConfigureAwait(false);
            return await ReadJsonAsync<T>(response, emptyResponseMessage).ConfigureAwait(false);
        }

        private async Task SendWithoutResponseAsync(
            HttpMethod method,
            string relativePath,
            object payload,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var request = CreateRequest(method, relativePath, payload);
            using var timeoutCancellation = CreateRequestCancellationSource(cancellationToken, timeout);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCancellation.Token).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);
        }

        private static HttpClient CreateHttpClient(int port, HttpMessageHandler messageHandler = null)
            => messageHandler == null
                ? new HttpClient
                {
                    BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                    Timeout = Timeout.InfiniteTimeSpan,
                }
                : new HttpClient(messageHandler)
                {
                    BaseAddress = new Uri($"http://127.0.0.1:{port}"),
                    Timeout = Timeout.InfiniteTimeSpan,
                };

        private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, object payload = null)
        {
            var request = new HttpRequestMessage(method, relativePath);
            if (payload != null)
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(payload, RequestSerializerSettings), Encoding.UTF8, "application/json");
            }

            return request;
        }

        private static CancellationTokenSource CreateRequestCancellationSource(CancellationToken cancellationToken, TimeSpan timeout)
        {
            var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(timeout);
            return timeoutCancellation;
        }

        private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, string emptyResponseMessage)
        {
            await EnsureSuccessAsync(response).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var payload = JsonConvert.DeserializeObject<T>(responseText);
            return payload == null
                ? throw new InvalidOperationException(emptyResponseMessage)
                : payload;
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            try
            {
                var error = JsonConvert.DeserializeObject<AgentServiceErrorResponse>(responseText);
                var message = error?.Message;
                if (!string.IsNullOrWhiteSpace(message))
                {
                    throw new AgentServiceApiException(response.StatusCode, message, error?.Code);
                }
            }
            catch (JsonException)
            {
            }

            throw new AgentServiceApiException(
                response.StatusCode,
                string.IsNullOrWhiteSpace(responseText) ? response.ReasonPhrase : responseText);
        }
    }
}
