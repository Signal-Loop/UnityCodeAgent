using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Api;
using UnityCodeCopilot.Service.Infrastructure;

// Test file goal: verify the hosted service endpoints match the shared OpenAPI and AsyncAPI artifacts.
// Scope: Program-hosted route behavior, request binding, response shape, and SSE framing through an in-process test server.
// Boundaries: excludes the real Copilot SDK runtime, process bootstrap, endpoint manifest publication, and live network deployment concerns.

namespace UnityCodeCopilot.Service.Tests;

public sealed class AgentServiceEndpointContractTests
{
    [Test]
    [Description("Goal: verify the health endpoint returns the documented healthy response body and status. Scope: in-process Program hosting and /health response serialization only. Boundaries: excludes startup manifest publication, external liveness checks, and degraded-state transitions.")]
    public async Task Health_ReturnsOpenApiHealthyExample()
    {
        var specs = ContractSpecExampleCatalog.Load();
        await using var factory = new AgentServiceApplicationFactory(specs);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(JToken.Parse(body), Is.EqualTo(JToken.Parse(specs.GetOpenApiResponseExampleJson("/health", "get", "200"))));
    }

    [Test]
    [Description("Goal: verify the sessions inventory endpoint returns the OpenAPI example shape. Scope: route binding, JSON serialization, and fake session-service integration only. Boundaries: excludes real session persistence, ordering rules beyond the fake data, and Unity-side consumption.")]
    public async Task ListSessions_ReturnsOpenApiExample()
    {
        var specs = ContractSpecExampleCatalog.Load();
        await using var factory = new AgentServiceApplicationFactory(specs);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/sessions");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(JToken.Parse(body), Is.EqualTo(JToken.Parse(specs.GetOpenApiResponseExampleJson("/api/sessions", "get", "200"))));
    }

    [Test]
    [Description("Goal: verify the models endpoint binds the request example and returns the documented response payload. Scope: Program-hosted endpoint behavior with the fake model catalog only. Boundaries: excludes the real Copilot client host, provider discovery, and external BYOK calls.")]
    public async Task ListModels_ReturnsOpenApiExample()
    {
        var specs = ContractSpecExampleCatalog.Load();
        await using var factory = new AgentServiceApplicationFactory(specs);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/models", CreateJsonContent(specs.GetOpenApiSchemaExampleJson("ListAgentModelsRequestDto")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Models.LastRequest, Is.Not.Null);
        Assert.That(JToken.Parse(body), Is.EqualTo(JToken.Parse(specs.GetOpenApiResponseExampleJson("/api/models", "post", "200"))));
    }

    [Test]
    [Description("Goal: verify the create-session endpoint accepts the OpenAPI request example and returns the documented response shape. Scope: endpoint binding and JSON serialization against the fake session service only. Boundaries: excludes real runtime session creation, event streaming, and persistence effects.")]
    public async Task CreateSession_ReturnsOpenApiExample()
    {
        var specs = ContractSpecExampleCatalog.Load();
        await using var factory = new AgentServiceApplicationFactory(specs);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/sessions/create", CreateJsonContent(specs.GetOpenApiSchemaExampleJson("CreateAgentSessionRequestDto")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Sessions.LastCreateRequest, Is.Not.Null);
        Assert.That(JToken.Parse(body), Is.EqualTo(JToken.Parse(specs.GetOpenApiResponseExampleJson("/api/sessions/create", "post", "200"))));
    }

    [Test]
    [Description("Goal: verify the open-session endpoint accepts the OpenAPI request example and returns the documented response shape. Scope: in-process endpoint binding and fake session-service integration only. Boundaries: excludes runtime resume behavior, transcript loading, and Unity-side client parsing.")]
    public async Task OpenSession_ReturnsOpenApiExample()
    {
        var specs = ContractSpecExampleCatalog.Load();
        await using var factory = new AgentServiceApplicationFactory(specs);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/sessions/open", CreateJsonContent(specs.GetOpenApiSchemaExampleJson("OpenAgentSessionRequestDto")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Sessions.LastOpenRequest, Is.Not.Null);
        Assert.That(JToken.Parse(body), Is.EqualTo(JToken.Parse(specs.GetOpenApiResponseExampleJson("/api/sessions/open", "post", "200"))));
    }

    [Test]
    [Description("Goal: verify the send endpoint binds the OpenAPI request example and returns HTTP 202. Scope: request binding and status-code behavior through the fake session service only. Boundaries: excludes real prompt execution, queueing, and downstream event emission.")]
    public async Task SendPrompt_ReturnsAcceptedStatusAndBindsSpecExample()
    {
        var specs = ContractSpecExampleCatalog.Load();
        await using var factory = new AgentServiceApplicationFactory(specs);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/sessions/send", CreateJsonContent(specs.GetOpenApiSchemaExampleJson("SendAgentPromptRequestDto")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(factory.Sessions.LastSendRequest, Is.Not.Null);
    }

    [Test]
    [Description("Goal: verify the abort endpoint binds the OpenAPI request example and returns HTTP 202. Scope: request binding and accepted-response behavior with the fake session service only. Boundaries: excludes actual runtime abort semantics, cancellation races, and emitted service messages.")]
    public async Task AbortPrompt_ReturnsAcceptedStatusAndBindsSpecExample()
    {
        var specs = ContractSpecExampleCatalog.Load();
        await using var factory = new AgentServiceApplicationFactory(specs);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/sessions/abort", CreateJsonContent(specs.GetOpenApiSchemaExampleJson("AbortAgentPromptRequestDto")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(factory.Sessions.LastAbortRequest, Is.Not.Null);
    }

    [Test]
    [Description("Goal: verify the tool-result endpoint rejects malformed completion payloads before pending-call lookup. Scope: request validation and error response shape only. Boundaries: excludes live Unity tool execution and Copilot SDK invocation flow.")]
    public async Task CompleteToolInvocation_RejectsMissingRequiredIdentifiers()
    {
        var specs = ContractSpecExampleCatalog.Load();
        await using var factory = new AgentServiceApplicationFactory(specs);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/tools/results", CreateJsonContent("""
            {
              "IsError": false,
              "TextResult": "missing identifiers"
            }
            """));
        var body = await response.Content.ReadAsStringAsync();
        var error = JsonConvert.DeserializeObject<AgentServiceErrorResponse>(body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(error?.Code, Is.EqualTo(AgentServiceErrorCodes.OperationFailed));
        Assert.That(error?.Message, Does.Contain("call id"));
    }

    [Test]
    [Description("Goal: verify GitHub Copilot auth failures return actionable sign-in guidance. Scope: endpoint error translation only with a fake model catalog. Boundaries: excludes live Copilot SDK auth state and Unity UI rendering.")]
    public async Task ListModels_GitHubCopilotAuthFailure_ReturnsCopilotGuidance()
    {
        await using var factory = new AgentServiceApplicationFactory();
        factory.Models.ExceptionToThrow = new AgentServiceAuthenticationException(
            AgentServiceAuthMessages.ForProvider(null),
            new IOException("Communication error with Copilot CLI: 401 Unauthorized"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/models", CreateJsonContent("""{"Provider":{"Model":"gpt-4o"}}"""));
        var body = await response.Content.ReadAsStringAsync();
        var error = JsonConvert.DeserializeObject<AgentServiceErrorResponse>(body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(error?.Message, Does.Contain("GitHub Copilot authentication failed."));
        Assert.That(error?.Message, Does.Not.Contain("401 Unauthorized"));
        Assert.That(error?.Message, Does.Not.Contain("Details:"));
    }

    [Test]
    [Description("Goal: verify BYOK auth failures return provider request guidance without leaking the configured API key. Scope: endpoint error translation only with a fake session service. Boundaries: excludes live provider calls and Unity settings drawing.")]
    public async Task CreateSession_ByokAuthFailure_ReturnsByokGuidanceWithoutApiKey()
    {
        await using var factory = new AgentServiceApplicationFactory();
        factory.Sessions.CreateExceptionToThrow = new AgentServiceAuthenticationException(
            AgentServiceAuthMessages.ForProvider(new ProviderConfigDto("gpt-4o", BaseUrl: "https://provider.example.test/v1", ApiKey: "secret-test-key")),
            new IOException("provider returned 403 Forbidden for invalid key secret-test-key"));
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/sessions/create", CreateJsonContent("""
            {
              "SessionId": "session-1",
              "Provider": {
                "Model": "gpt-4o",
                "BaseUrl": "https://provider.example.test/v1",
                "ApiKey": "secret-test-key"
              },
              "Streaming": true,
              "InfiniteSessions": {},
              "WorkingDirectory": "C:/work"
            }
            """));
        var body = await response.Content.ReadAsStringAsync();
        var error = JsonConvert.DeserializeObject<AgentServiceErrorResponse>(body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(error?.Message, Does.Contain("BYOK provider request failed."));
        Assert.That(error?.Message, Does.Contain("BaseUrl"));
        Assert.That(error?.Message, Does.Contain("selected model"));
        Assert.That(error?.Message, Does.Not.Contain("secret-test-key"));
        Assert.That(error?.Message, Does.Not.Contain("403 Forbidden"));
        Assert.That(error?.Message, Does.Not.Contain("Details:"));
    }

    [Test]
    [Description("Goal: verify the SSE endpoint replays the AsyncAPI example using the documented SSE frame layout. Scope: Program-hosted /events framing, content type, and retained-event replay with the in-memory broker only. Boundaries: excludes live runtime event generation, reconnection loops, and long-running stream behavior.")]
    public async Task EventsStream_ReplaysAsyncApiExample()
    {
        var specs = ContractSpecExampleCatalog.Load();
        await using var factory = new AgentServiceApplicationFactory(specs);
        using var client = factory.CreateClient();
        factory.PublishAsyncApiExample();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "0");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(stream);
        var payload = new StringBuilder();

        while (!cancellation.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellation.Token);
            if (line == null)
            {
                break;
            }

            payload.Append(line).Append('\n');
            if (line.Length == 0)
            {
                break;
            }
        }

        var expectedJson = specs.GetAsyncApiEnvelopeExampleJson();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));
        Assert.That(payload.ToString(), Is.EqualTo($"data: {expectedJson}\n\n"));
    }

    private static StringContent CreateJsonContent(string json)
        => new(json, Encoding.UTF8, "application/json");

    private sealed class AgentServiceApplicationFactory(ContractSpecExampleCatalog? specs = null) : WebApplicationFactory<Program>
    {
        public FakeAgentSessionService Sessions { get; } = new(specs);

        public FakeAgentModelCatalog Models { get; } = new(specs);

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey, "Testing");
            builder.UseSetting("ProjectRoot", AppContext.BaseDirectory);
            builder.UseSetting("UnityProcessId", "1");
            builder.UseSetting("OrphanTimeoutSeconds", "90");
            builder.UseSetting("EnableTelemetry", "false");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IAgentSessionService>();
                services.RemoveAll<IAgentModelCatalog>();
                services.AddSingleton<IAgentSessionService>(Sessions);
                services.AddSingleton<IAgentModelCatalog>(Models);
            });
        }

        public void PublishAsyncApiExample()
            => Services.GetRequiredService<EventStreamBroker>().Publish(RequireSpecs().GetAsyncApiEnvelopeExample());

        private ContractSpecExampleCatalog RequireSpecs()
            => specs ?? throw new InvalidOperationException("Contract specs are required for this fake response.");
    }

    private sealed class FakeAgentModelCatalog(ContractSpecExampleCatalog? specs) : IAgentModelCatalog
    {
        public ListAgentModelsRequestDto? LastRequest { get; private set; }

        public Exception? ExceptionToThrow { get; set; }

        public Task<IReadOnlyList<ModelInfoDto>> ListModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(RequireSpecs().Deserialize<IReadOnlyList<ModelInfoDto>>(RequireSpecs().GetOpenApiResponseExampleJson("/api/models", "post", "200")));
        }

        private ContractSpecExampleCatalog RequireSpecs()
            => specs ?? throw new InvalidOperationException("Contract specs are required for this fake response.");
    }

    private sealed class FakeAgentSessionService(ContractSpecExampleCatalog? specs) : IAgentSessionService
    {
        public CreateAgentSessionRequestDto? LastCreateRequest { get; private set; }

        public OpenAgentSessionRequestDto? LastOpenRequest { get; private set; }

        public SendAgentPromptRequestDto? LastSendRequest { get; private set; }

        public AbortAgentPromptRequestDto? LastAbortRequest { get; private set; }

        public Exception? CreateExceptionToThrow { get; set; }

        public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
            => Task.FromResult(RequireSpecs().Deserialize<IReadOnlyList<SessionSummaryDto>>(RequireSpecs().GetOpenApiResponseExampleJson("/api/sessions", "get", "200")));

        public Task<AgentSessionResponseDto> CreateAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
        {
            LastCreateRequest = request;
            if (CreateExceptionToThrow != null)
            {
                throw CreateExceptionToThrow;
            }

            return Task.FromResult(RequireSpecs().Deserialize<AgentSessionResponseDto>(RequireSpecs().GetOpenApiResponseExampleJson("/api/sessions/create", "post", "200")));
        }

        public Task<AgentSessionResponseDto> OpenAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
        {
            LastOpenRequest = request;
            return Task.FromResult(RequireSpecs().Deserialize<AgentSessionResponseDto>(RequireSpecs().GetOpenApiResponseExampleJson("/api/sessions/open", "post", "200")));
        }

        public Task SendAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
        {
            LastSendRequest = request;
            return Task.CompletedTask;
        }

        public Task AbortAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
        {
            LastAbortRequest = request;
            return Task.CompletedTask;
        }

        private ContractSpecExampleCatalog RequireSpecs()
            => specs ?? throw new InvalidOperationException("Contract specs are required for this fake response.");
    }
}
