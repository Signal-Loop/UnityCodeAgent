using System.Text;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Copilot;
using UnityCodeCopilot.Service.Options;
using UnityCodeCopilot.Service.Settings;
using UnityCodeCopilot.Service.Telemetry;

namespace UnityCodeCopilot.Service.Api;

public static class ServiceEndpoints
{
    private static readonly TimeSpan SseKeepAliveInterval = TimeSpan.FromSeconds(2);

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/service/stop", (
            IOptions<ServiceOptions> options,
            IHostApplicationLifetime applicationLifetime,
            UnityCodeCopilotServiceLogger log) =>
        {
            if (!options.Value.NoUnity)
            {
                log.Warning(nameof(ServiceEndpoints), "Service stop request rejected because no-Unity mode is disabled.");
                return CreateJsonResult(
                    new AgentServiceErrorResponse("Service self-stop is available only in no-Unity mode.", AgentServiceErrorCodes.OperationFailed),
                    StatusCodes.Status422UnprocessableEntity);
            }

            log.Info(nameof(ServiceEndpoints), "Service stop requested in no-Unity mode.");
            _ = Task.Run(applicationLifetime.StopApplication);
            return Results.Accepted();
        });

        app.MapGet("/api/sessions", async (IAgentSessionService sessions, UnityCodeCopilotServiceLogger log, CopilotTelemetry telemetry, CancellationToken cancellationToken) =>
        {
            using var operation = StartHttpOperation(telemetry, TelemetryOperations.HttpSessionsList, "/api/sessions");

            log.Info(nameof(ServiceEndpoints), "Session inventory requested.");

            return CreateJsonResult(await sessions.GetSessionsAsync(cancellationToken));
        });

        app.MapPost("/api/models", async (ListAgentModelsRequestDto request, IAgentModelCatalog modelCatalog, UnityCodeCopilotServiceLogger log, CopilotTelemetry telemetry, CancellationToken cancellationToken) =>
        {
            using var operation = StartHttpOperation(telemetry, TelemetryOperations.HttpModelsList, "/api/models");

            operation.SetTags(("byok.enabled", request.Provider?.HasByok == true));

            try
            {
                log.Info(nameof(ServiceEndpoints), "Model inventory requested.", ("byokEnabled", request.Provider?.HasByok == true));

                var models = await modelCatalog.ListModelsAsync(request, cancellationToken);

                return CreateJsonResult(models);
            }
            catch (Exception exception) when (IsExpectedRequestFailure(exception))
            {
                operation.MarkError(exception);
                return CreateErrorResult(log, nameof(ServiceEndpoints), "Model inventory request failed.", exception, request.Provider, ("byokEnabled", request.Provider?.HasByok == true));
            }
        });

        app.MapGet("/events", StreamEventsAsync);

        app.MapPost("/api/sessions/create", async (CreateAgentSessionRequestDto request, IAgentSessionService sessions, UnityCodeCopilotServiceLogger log, CopilotTelemetry telemetry, CancellationToken cancellationToken) =>
        {
            using var operation = StartHttpOperation(telemetry, TelemetryOperations.HttpSessionCreate, "/api/sessions/create");

            operation.SetTags(
                ("gen_ai.conversation.id", request.SessionId),
                ("gen_ai.request.model", request.Model));

            try
            {
                log.Info(nameof(ServiceEndpoints), "HTTP create session request received.",
                    ("sessionId", request.SessionId),
                    ("model", request.Model));

                return CreateJsonResult(await sessions.CreateAsync(request, cancellationToken));
            }
            catch (Exception exception) when (IsExpectedRequestFailure(exception))
            {
                operation.MarkError(exception);
                return CreateErrorResult(log, nameof(ServiceEndpoints), "Create session request failed.", exception, request.Provider, ("sessionId", request.SessionId));
            }
        });

        app.MapPost("/api/sessions/open", async (OpenAgentSessionRequestDto request, IAgentSessionService sessions, UnityCodeCopilotServiceLogger log, CopilotTelemetry telemetry, CancellationToken cancellationToken) =>
        {
            using var operation = StartHttpOperation(telemetry, TelemetryOperations.HttpSessionOpen, "/api/sessions/open");

            operation.SetTags(
                ("gen_ai.conversation.id", request.SessionId),
                ("gen_ai.request.model", request.Model));

            try
            {
                log.Info(nameof(ServiceEndpoints), "HTTP open session request received.",
                    ("sessionId", request.SessionId),
                    ("model", request.Model));

                return CreateJsonResult(await sessions.OpenAsync(request, cancellationToken));
            }
            catch (Exception exception) when (IsExpectedRequestFailure(exception))
            {
                operation.MarkError(exception);
                return CreateErrorResult(log, nameof(ServiceEndpoints), "Open session request failed.", exception, request.Provider, ("sessionId", request.SessionId));
            }
        });

        app.MapPost("/api/sessions/send", async (SendAgentPromptRequestDto request, IAgentSessionService sessions, UnityCodeCopilotServiceLogger log, CopilotTelemetry telemetry, CancellationToken cancellationToken) =>
        {
            using var operation = StartHttpOperation(telemetry, TelemetryOperations.HttpSessionSend, "/api/sessions/send");

            operation.SetTags(
                ("gen_ai.conversation.id", request.SessionId),
                ("service.prompt.length", request.Prompt?.Length ?? 0));

            try
            {
                log.Info(nameof(ServiceEndpoints), "HTTP send prompt request received.",
                    ("sessionId", request.SessionId),
                    ("promptLength", request.Prompt?.Length ?? 0));

                await sessions.SendAsync(request, cancellationToken);

                return Results.Accepted();
            }
            catch (Exception exception) when (IsExpectedRequestFailure(exception))
            {
                operation.MarkError(exception);
                return CreateErrorResult(log, nameof(ServiceEndpoints), "Send prompt request failed.", exception, null, ("sessionId", request.SessionId));
            }
        });

        app.MapPost("/api/sessions/abort", async (AbortAgentPromptRequestDto request, IAgentSessionService sessions, UnityCodeCopilotServiceLogger log, CopilotTelemetry telemetry, CancellationToken cancellationToken) =>
        {
            using var operation = StartHttpOperation(telemetry, TelemetryOperations.HttpSessionAbort, "/api/sessions/abort");

            operation.SetTags(("gen_ai.conversation.id", request.SessionId));

            try
            {
                log.Info(nameof(ServiceEndpoints), "HTTP abort request received.", ("sessionId", request.SessionId));

                await sessions.AbortAsync(request, cancellationToken);

                return Results.Accepted();
            }
            catch (Exception exception) when (IsExpectedRequestFailure(exception))
            {
                operation.MarkError(exception);
                return CreateErrorResult(log, nameof(ServiceEndpoints), "Abort request failed.", exception, null, ("sessionId", request.SessionId));
            }
        });

        app.MapPost("/api/tools/results", (AgentToolInvocationResultDto request, AgentToolInvocationBridge tools, UnityCodeCopilotServiceLogger log, CopilotTelemetry telemetry) =>
        {
            using var operation = StartHttpOperation(telemetry, TelemetryOperations.HttpToolInvocationResult, "/api/tools/results");

            try
            {
                ValidateToolInvocationResult(request);

                operation.SetTags(
                    ("gen_ai.conversation.id", request.SessionId),
                    ("unity.tool.name", request.ToolName),
                    ("unity.tool.call_id", request.CallId),
                    ("unity.tool.is_error", request.IsError));

                log.Info(nameof(ServiceEndpoints), "HTTP Unity tool result received.",
                    ("sessionId", request.SessionId),
                    ("toolName", request.ToolName),
                    ("callId", request.CallId),
                    ("isError", request.IsError));

                return tools.TryComplete(request)
                    ? Results.Accepted()
                    : CreateJsonResult(new AgentServiceErrorResponse("The tool invocation is no longer pending.", AgentServiceErrorCodes.OperationFailed), StatusCodes.Status404NotFound);
            }
            catch (Exception exception) when (IsExpectedRequestFailure(exception))
            {
                operation.MarkError(exception);
                return CreateErrorResult(log, nameof(ServiceEndpoints), "Unity tool result request failed.", exception, null, ("sessionId", request?.SessionId));
            }
        });
    }

    private static void ValidateToolInvocationResult(AgentToolInvocationResultDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.CallId))
        {
            throw new ArgumentException("Tool invocation result must include a call id.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("Tool invocation result must include a session id.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            throw new ArgumentException("Tool invocation result must include a tool name.", nameof(request));
        }
    }

    private static IResult CreateErrorResult(UnityCodeCopilotServiceLogger log, string category, string message, Exception exception, ProviderConfigDto? provider, params (string Key, object? Value)[] properties)
    {
        var sessionUnavailable = exception is AgentSessionUnavailableException;
        if (sessionUnavailable)
        {
            log.Warning(category, message, properties);
        }
        else
        {
            log.Error(category, message, exception, properties);
        }

        var statusCode = sessionUnavailable
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
        var errorCode = sessionUnavailable
            ? AgentServiceErrorCodes.SessionUnavailable
            : AgentServiceErrorCodes.OperationFailed;

        return CreateJsonResult(new AgentServiceErrorResponse(GetUserFacingMessage(exception, provider), errorCode), statusCode);
    }

    private static IResult CreateJsonResult(object payload, int statusCode = StatusCodes.Status200OK)
        => Results.Content(JsonConvert.SerializeObject(payload), "application/json", Encoding.UTF8, statusCode);

    private static bool IsExpectedRequestFailure(Exception exception)
        => exception is ArgumentException or InvalidOperationException or IOException or HttpRequestException;

    private static string GetUserFacingMessage(Exception exception, ProviderConfigDto? provider)
    {
        if (exception is AgentServiceAuthenticationException)
        {
            return string.IsNullOrWhiteSpace(exception.Message) ? AgentServiceAuthMessages.ForProvider(provider) : exception.Message;
        }

        var message = CopilotAuthFailureClassifier.GetInnermostMessage(exception);
        return string.IsNullOrWhiteSpace(message) ? "The request failed." : message;
    }

    private static async Task StreamEventsAsync(HttpContext context, EventStreamBroker broker, UnityCodeCopilotServiceLogger log, CopilotTelemetry telemetry)
    {
        using var operation = StartHttpOperation(telemetry, TelemetryOperations.HttpEventsStream, context.Request.Path.ToString());
        var cancellationToken = context.RequestAborted;
        var lastEventId = GetLastEventId(context);

        operation.SetTag("sse.last_event_id", lastEventId);

        log.Info(nameof(ServiceEndpoints), "SSE client connected.",
            ("path", context.Request.Path.ToString()),
            ("lastEventId", lastEventId));

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        await context.Response.StartAsync(cancellationToken);
        using var subscription = broker.Subscribe(lastEventId, cancellationToken);

        try
        {
            foreach (var retainedEnvelope in subscription.RetainedEvents)
            {
                await WriteEventAsync(context.Response, retainedEnvelope, cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var waitResult = await WaitForEventAsync(subscription, cancellationToken);
                if (waitResult == SseWaitResult.Completed)
                {
                    break;
                }

                if (waitResult == SseWaitResult.HeartbeatElapsed)
                {
                    await WriteKeepAliveAsync(context.Response, cancellationToken);
                    continue;
                }

                while (subscription.TryRead(out var envelope))
                {
                    await WriteEventAsync(context.Response, envelope, cancellationToken);
                }
            }

            log.Info(nameof(ServiceEndpoints), "SSE stream completed.", ("path", context.Request.Path.ToString()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation.MarkCancelled();
            log.Info(nameof(ServiceEndpoints), "SSE client disconnected.", ("path", context.Request.Path.ToString()));
        }
    }

    private static TelemetryOperation StartHttpOperation(CopilotTelemetry telemetry, string operationName, string path)
    {
        var operation = telemetry.StartOperation(operationName);
        operation.SetTags(("url.path", path));
        return operation;
    }

    private static long? GetLastEventId(HttpContext context)
    {
        var rawValue = context.Request.Headers["Last-Event-ID"].ToString();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return long.TryParse(rawValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var sequenceNumber)
            ? sequenceNumber
            : null;
    }

    private static async Task WriteEventAsync(HttpResponse response, AgentServiceEventEnvelope envelope, CancellationToken cancellationToken)
    {
        await response.WriteAsync("data: ", cancellationToken);
        await response.WriteAsync(JsonConvert.SerializeObject(envelope), cancellationToken);
        await response.WriteAsync("\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteKeepAliveAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        await response.WriteAsync(": keep-alive\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task<SseWaitResult> WaitForEventAsync(EventStreamBroker.EventStreamSubscription subscription, CancellationToken cancellationToken)
    {
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeatCancellation.CancelAfter(SseKeepAliveInterval);

        try
        {
            return await subscription.WaitToReadAsync(heartbeatCancellation.Token)
                ? SseWaitResult.EventAvailable
                : SseWaitResult.Completed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SseWaitResult.HeartbeatElapsed;
        }
    }

    private enum SseWaitResult
    {
        EventAvailable,
        HeartbeatElapsed,
        Completed,
    }
}
