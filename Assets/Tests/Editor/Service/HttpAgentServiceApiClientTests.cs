using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;

// Test file goal: verify the Unity HTTP API client matches the shared unary contract examples.
// Scope: request serialization, route selection, response parsing, and error translation through a scripted handler.
// Boundaries: excludes live sockets, service endpoint behavior, and any Copilot runtime side effects.

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class HttpAgentServiceApiClientTests
    {
        [Test]
        [Description("Goal: verify each production client owns its own HttpClient instead of sharing a static per-port cache. Scope: constructor behavior in HttpAgentServiceApiClient only. Boundaries: does not send any requests or validate service communication.")]
        public void Constructor_SamePort_DoesNotReuseSharedHttpClientInstance()
        {
            var first = new HttpAgentServiceApiClient(CreateManifest());
            var second = new HttpAgentServiceApiClient(CreateManifest());

            var firstHttpClient = GetInnerHttpClient(first);
            var secondHttpClient = GetInnerHttpClient(second);

            Assert.That(firstHttpClient, Is.Not.Null);
            Assert.That(secondHttpClient, Is.Not.Null);
            Assert.That(ReferenceEquals(firstHttpClient, secondHttpClient), Is.False);
        }

        [Test]
        [Description("Goal: verify the models request body and parsed response match the OpenAPI contract example. Scope: HttpAgentServiceApiClient against a scripted in-memory handler only. Boundaries: does not call the live service or validate model availability beyond the mocked payload.")]
        public async Task GetModelsAsync_SendsSpecShapedRequestAndParsesResponse()
        {
            var handler = new ScriptedHttpMessageHandler();
            handler.EnqueueJsonResponse(HttpStatusCode.OK, "[{\"Id\":\"gpt-5-mini\",\"Name\":\"GPT-5 Mini\"}]");
            var client = new HttpAgentServiceApiClient(CreateManifest(), handler);
            var expectedJson = ContractSpecExampleReader.ReadOpenApiSchemaExampleAsJson("ListAgentModelsRequestDto");

            var models = await client.GetModelsAsync(
                new ListAgentModelsRequestDto(new ProviderConfigDto(null, "openai", "https://example.test", "secret", "chat")),
                CancellationToken.None);

            var request = handler.SingleRequest;

            Assert.That(models.Count, Is.EqualTo(1));
            Assert.That(models[0].Id, Is.EqualTo("gpt-5-mini"));
            Assert.That(request.HttpMethod, Is.EqualTo("POST"));
            Assert.That(request.RawUrl, Is.EqualTo("/api/models"));
            Assert.That(request.BodyText, Is.EqualTo(expectedJson));

            var body = request.ReadBodyAs<ListAgentModelsRequestDto>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.Provider, Is.Not.Null);
            Assert.That(body.Provider.Type, Is.EqualTo("openai"));
            Assert.That(body.Provider.BaseUrl, Is.EqualTo("https://example.test"));
            Assert.That(body.Provider.WireApi, Is.EqualTo("chat"));
        }

        [Test]
        [Description("Goal: verify the sessions snapshot call uses GET with no request body and parses the contract-shaped response. Scope: Unity client request construction and response deserialization only. Boundaries: excludes service-side filtering, persistence, and live transport behavior.")]
        public async Task GetSessionsAsync_UsesGetAndParsesSpecShapedResponse()
        {
            var handler = new ScriptedHttpMessageHandler();
            handler.EnqueueJsonResponse(HttpStatusCode.OK, "[{\"SessionId\":\"session-1\",\"StartTime\":\"2026-05-29T12:00:00Z\",\"ModifiedTime\":\"2026-05-29T12:05:00Z\",\"Summary\":\"Test session\",\"IsRemote\":false}]");
            var client = new HttpAgentServiceApiClient(CreateManifest(), handler);

            var sessions = await client.GetSessionsAsync(CancellationToken.None);

            var request = handler.SingleRequest;

            Assert.That(sessions.Count, Is.EqualTo(1));
            Assert.That(sessions[0].SessionId, Is.EqualTo("session-1"));
            Assert.That(sessions[0].Summary, Is.EqualTo("Test session"));
            Assert.That(request.HttpMethod, Is.EqualTo("GET"));
            Assert.That(request.RawUrl, Is.EqualTo("/api/sessions"));
            Assert.That(request.ContentLength64, Is.EqualTo(0));
        }

        [Test]
        [Description("Goal: verify the open-session request matches the OpenAPI example and the client can read the returned session payload. Scope: JSON serialization and response parsing in HttpAgentServiceApiClient only. Boundaries: does not validate server-side session lookup or runtime attachment behavior.")]
        public async Task OpenSessionAsync_SendsSpecExampleShapeAndParsesResponse()
        {
            var handler = new ScriptedHttpMessageHandler();
            handler.EnqueueJsonResponse(HttpStatusCode.OK, "{\"SessionId\":\"session-1\",\"Status\":\"ready\",\"Messages\":[{\"SequenceNumber\":1,\"SessionId\":\"session-1\",\"TimestampUtc\":\"2026-05-29T12:00:00Z\",\"Content\":\"hello\",\"StreamKey\":\"assistant-1\",\"Type\":\"AssistantMessage\",\"SourceJson\":\"{}\"}]}");
            var client = new HttpAgentServiceApiClient(CreateManifest(), handler);
            var expectedJson = ContractSpecExampleReader.ReadOpenApiSchemaExampleAsJson("OpenAgentSessionRequestDto");

            var response = await client.OpenSessionAsync(
                new OpenAgentSessionRequestDto("session-1", new ProviderConfigDto("gpt-5-mini"), true, new InfiniteSessionsDto(false, 0, 0), "C:/work", new[] { ".agents/skills" }, Array.Empty<string>()),
                CancellationToken.None);

            var request = handler.SingleRequest;
            var body = request.ReadBodyAs<OpenAgentSessionRequestDto>();

            Assert.That(response.SessionId, Is.EqualTo("session-1"));
            Assert.That(response.Messages.Count, Is.EqualTo(1));
            Assert.That(request.RawUrl, Is.EqualTo("/api/sessions/open"));
            Assert.That(request.BodyText, Is.EqualTo(expectedJson));
            Assert.That(body, Is.Not.Null);
            Assert.That(body.InfiniteSessions.Enabled, Is.False);
            Assert.That(body.InfiniteSessions.BackgroundCompactionThreshold, Is.EqualTo(0d));
            Assert.That(body.InfiniteSessions.BufferExhaustionThreshold, Is.EqualTo(0d));
        }

        [Test]
        [Description("Goal: verify the create-session request matches the OpenAPI example and the client can read the created session payload. Scope: client-side request formatting and successful response parsing only. Boundaries: excludes actual session creation, service orchestration, and runtime execution.")]
        public async Task CreateSessionAsync_SendsSpecExampleShapeAndParsesResponse()
        {
            var handler = new ScriptedHttpMessageHandler();
            handler.EnqueueJsonResponse(HttpStatusCode.OK, "{\"SessionId\":\"session-2\",\"Status\":\"ready\",\"Messages\":[]}");
            var client = new HttpAgentServiceApiClient(CreateManifest(), handler);
            var expectedJson = ContractSpecExampleReader.ReadOpenApiSchemaExampleAsJson("CreateAgentSessionRequestDto");

            var response = await client.CreateSessionAsync(
                new CreateAgentSessionRequestDto("session-1", new ProviderConfigDto("gpt-5-mini"), true, new InfiniteSessionsDto(true, 0.25, 0.75), "C:/work", new[] { ".agents/skills" }, new[] { "experimental-feature" }),
                CancellationToken.None);

            var request = handler.SingleRequest;
            var body = request.ReadBodyAs<CreateAgentSessionRequestDto>();

            Assert.That(response.SessionId, Is.EqualTo("session-2"));
            Assert.That(request.RawUrl, Is.EqualTo("/api/sessions/create"));
            Assert.That(request.BodyText, Is.EqualTo(expectedJson));
            Assert.That(body, Is.Not.Null);
            Assert.That(body.Streaming, Is.True);
        }

        [Test]
        [Description("Goal: verify a contract-shaped service error response is surfaced as the user-facing exception message. Scope: error-body parsing in the Unity HTTP client only. Boundaries: excludes HTTP transport failures and any server-side validation logic beyond the mocked payload.")]
        public void CreateSessionAsync_ServiceErrorResponse_ThrowsApiExceptionWithMessage()
        {
            var handler = new ScriptedHttpMessageHandler();
            handler.EnqueueJsonResponse(HttpStatusCode.BadRequest, "{\"Message\":\"session rejected\"}");
            var client = new HttpAgentServiceApiClient(CreateManifest(), handler);

            var exception = Assert.ThrowsAsync<AgentServiceApiException>(async () =>
                await client.CreateSessionAsync(
                    new CreateAgentSessionRequestDto("session-1", new ProviderConfigDto("gpt-5-mini"), true, new InfiniteSessionsDto(), "C:/work"),
                    CancellationToken.None));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.Message, Is.EqualTo("session rejected"));
            Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        [Description("Goal: verify the send-prompt request body matches the OpenAPI example. Scope: prompt request serialization and route selection only. Boundaries: does not execute a prompt or validate downstream session processing.")]
        public async Task SendPromptAsync_SendsSpecShapedRequest()
        {
            var handler = new ScriptedHttpMessageHandler();
            handler.EnqueueEmptyResponse(HttpStatusCode.Accepted, "Accepted");
            var client = new HttpAgentServiceApiClient(CreateManifest(), handler);
            var expectedJson = ContractSpecExampleReader.ReadOpenApiSchemaExampleAsJson("SendAgentPromptRequestDto");

            await client.SendPromptAsync(new SendAgentPromptRequestDto("session-1", "Reply with exactly VERIFIED"), CancellationToken.None);

            var request = handler.SingleRequest;
            var body = request.ReadBodyAs<SendAgentPromptRequestDto>();

            Assert.That(request.RawUrl, Is.EqualTo("/api/sessions/send"));
            Assert.That(request.BodyText, Is.EqualTo(expectedJson));
            Assert.That(body, Is.Not.Null);
            Assert.That(body.SessionId, Is.EqualTo("session-1"));
            Assert.That(body.Prompt, Is.EqualTo("Reply with exactly VERIFIED"));
        }

        [Test]
        [Description("Goal: verify non-JSON error bodies are surfaced as the exception message for send failures. Scope: Unity client error translation only. Boundaries: excludes response retries, service diagnostics, and runtime prompt execution.")]
        public void SendPromptAsync_PlainTextFailure_ThrowsApiExceptionWithBody()
        {
            var handler = new ScriptedHttpMessageHandler();
            handler.EnqueuePlainTextResponse(HttpStatusCode.BadRequest, "plain-text failure");
            var client = new HttpAgentServiceApiClient(CreateManifest(), handler);

            var exception = Assert.ThrowsAsync<AgentServiceApiException>(async () =>
                await client.SendPromptAsync(new SendAgentPromptRequestDto("session-1", "hello"), CancellationToken.None));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.Message, Is.EqualTo("plain-text failure"));
            Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        [Description("Goal: verify the abort request body matches the OpenAPI example. Scope: abort request serialization and route selection only. Boundaries: does not validate actual cancellation behavior in the service or runtime.")]
        public async Task AbortPromptAsync_SendsSpecShapedRequest()
        {
            var handler = new ScriptedHttpMessageHandler();
            handler.EnqueueEmptyResponse(HttpStatusCode.Accepted, "Accepted");
            var client = new HttpAgentServiceApiClient(CreateManifest(), handler);
            var expectedJson = ContractSpecExampleReader.ReadOpenApiSchemaExampleAsJson("AbortAgentPromptRequestDto");

            await client.AbortPromptAsync(new AbortAgentPromptRequestDto("session-1"), CancellationToken.None);

            var request = handler.SingleRequest;
            var body = request.ReadBodyAs<AbortAgentPromptRequestDto>();

            Assert.That(request.RawUrl, Is.EqualTo("/api/sessions/abort"));
            Assert.That(request.BodyText, Is.EqualTo(expectedJson));
            Assert.That(body, Is.Not.Null);
            Assert.That(body.SessionId, Is.EqualTo("session-1"));
        }

        [Test]
        [Description("Goal: verify empty-body abort failures fall back to the HTTP reason phrase. Scope: Unity client failure translation only. Boundaries: excludes service-side abort semantics and any network-level retry behavior.")]
        public void AbortPromptAsync_EmptyBodyFailure_ThrowsApiExceptionWithReasonPhrase()
        {
            var handler = new ScriptedHttpMessageHandler();
            handler.EnqueueEmptyResponse(HttpStatusCode.ServiceUnavailable, "Service Unavailable");
            var client = new HttpAgentServiceApiClient(CreateManifest(), handler);

            var exception = Assert.ThrowsAsync<AgentServiceApiException>(async () =>
                await client.AbortPromptAsync(new AbortAgentPromptRequestDto("session-1"), CancellationToken.None));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.Message, Is.EqualTo("Service Unavailable"));
            Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        }

        private static EndpointManifest CreateManifest()
            => new EndpointManifest { Port = 7777, ServiceProcessId = 1234, StartedAtUtc = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero) };

        private static HttpClient GetInnerHttpClient(HttpAgentServiceApiClient client)
        {
            var field = typeof(HttpAgentServiceApiClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic);
            return (HttpClient)field.GetValue(client);
        }

        private sealed class ScriptedHttpMessageHandler : HttpMessageHandler
        {
            private readonly Queue<ScriptedResponse> _responses = new Queue<ScriptedResponse>();
            public CapturedRequest SingleRequest { get; private set; }

            public void EnqueueJsonResponse(HttpStatusCode statusCode, string body)
                => _responses.Enqueue(new ScriptedResponse(statusCode, "application/json", body));

            public void EnqueuePlainTextResponse(HttpStatusCode statusCode, string body)
                => _responses.Enqueue(new ScriptedResponse(statusCode, "text/plain", body));

            public void EnqueueEmptyResponse(HttpStatusCode statusCode, string reasonPhrase)
                => _responses.Enqueue(new ScriptedResponse(statusCode, "text/plain", string.Empty, reasonPhrase));

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                SingleRequest = await CapturedRequest.CreateAsync(request).ConfigureAwait(false);

                var response = _responses.Dequeue();
                return new HttpResponseMessage(response.StatusCode)
                {
                    Content = new StringContent(response.Body, Encoding.UTF8, response.ContentType),
                    ReasonPhrase = response.ReasonPhrase,
                    RequestMessage = request,
                };
            }
        }

        private sealed class ScriptedResponse
        {
            public ScriptedResponse(HttpStatusCode statusCode, string contentType, string body, string reasonPhrase = null)
            {
                StatusCode = statusCode;
                ContentType = contentType;
                Body = body;
                ReasonPhrase = reasonPhrase;
            }

            public HttpStatusCode StatusCode { get; }

            public string ContentType { get; }

            public string Body { get; }

            public string ReasonPhrase { get; }
        }

        private sealed class CapturedRequest
        {
            private CapturedRequest(string httpMethod, string rawUrl, string bodyText, long contentLength64)
            {
                HttpMethod = httpMethod;
                RawUrl = rawUrl;
                BodyText = bodyText;
                ContentLength64 = contentLength64;
            }

            public string HttpMethod { get; }

            public string RawUrl { get; }

            public string BodyText { get; }

            public long ContentLength64 { get; }

            public T ReadBodyAs<T>()
                => JsonConvert.DeserializeObject<T>(BodyText);

            public static async Task<CapturedRequest> CreateAsync(HttpRequestMessage request)
            {
                var body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
                var contentLength = request.Content?.Headers.ContentLength ?? 0;
                return new CapturedRequest(request.Method.Method, request.RequestUri.PathAndQuery, body, contentLength);
            }
        }
    }
}
