using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Interfaces;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class SseAgentServiceEventStreamClient : IAgentServiceEventStreamClient
    {
        private readonly EndpointManifest _manifest;

        public SseAgentServiceEventStreamClient(EndpointManifest manifest)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        }

        public async Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken)
        {
            if (onEvent == null)
            {
                throw new ArgumentNullException(nameof(onEvent));
            }

            using var httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl("/events"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            if (lastEventId.HasValue)
            {
                request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response).ConfigureAwait(false);

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var reader = new StreamReader(stream);

            string line;
            string data = null;

            while (!cancellationToken.IsCancellationRequested && (line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                if (string.IsNullOrEmpty(line))
                {
                    if (!string.IsNullOrWhiteSpace(data))
                    {
                        var envelope = JsonConvert.DeserializeObject<AgentServiceEventEnvelope>(data);
                        if (envelope != null)
                        {
                            onEvent(envelope);
                        }
                    }

                    data = null;
                    continue;
                }

                if (line.StartsWith(":", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var segment = line.Substring(5).TrimStart();
                    data = data == null ? segment : data + "\n" + segment;
                }
            }
        }

        private string BuildUrl(string relativePath)
            => $"http://127.0.0.1:{_manifest.Port}{relativePath}";

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
                    throw new InvalidOperationException(message);
                }
            }
            catch (JsonException)
            {
            }

            throw new InvalidOperationException(string.IsNullOrWhiteSpace(responseText) ? response.ReasonPhrase : responseText);
        }
    }
}