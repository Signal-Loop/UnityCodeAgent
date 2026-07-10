using System.Text.Json;
using GitHub.Copilot;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Copilot;

namespace UnityCodeCopilot.Service.Tests;

public sealed class ServiceEventEnvelopeFactoryTests
{
    [Test]
    public void Create_ModelCallFailure_WithRejectedImageInputUsesVisionCapabilityGuidance()
    {
        var envelope = ServiceEventEnvelopeFactory.Create(
            1,
            "session-1",
            CreateModelCallFailureEvent(
                """{"message":"No endpoints found that support image input","code":404}""",
                imagePartCount: 1));

        Assert.That(envelope, Is.Not.Null);
        Assert.That(envelope!.Type, Is.EqualTo(AgentEventType.Error));
        Assert.That(envelope.Content, Does.Contain("not available for image input"));
        Assert.That(envelope.Content, Does.Contain("vision-capable model"));
        Assert.That(envelope.Content, Does.Not.Contain("BaseUrl"));
    }

    [Test]
    public void Create_ModelCallFailure_WithoutImageRequestDoesNotClaimVisionFailure()
    {
        var envelope = ServiceEventEnvelopeFactory.Create(
            1,
            "session-1",
            CreateModelCallFailureEvent(
                """{"message":"No endpoints found that support image input","code":404}""",
                imagePartCount: 0));

        Assert.That(envelope, Is.Not.Null);
        Assert.That(envelope!.Content, Is.Empty);
    }

    [Test]
    public void Create_ExactScreenshotSteeringMessage_IsSuppressed()
    {
        var sessionEvent = new UserMessageEvent
        {
            Data = new UserMessageData
            {
                Content = "The Game View screenshot returned by the current Unity tool call is attached. Analyze it to answer the user's current request."
            },
            Timestamp = DateTimeOffset.UnixEpoch,
        };

        Assert.That(ServiceEventEnvelopeFactory.Create(1, "session-1", sessionEvent), Is.Null);
    }

    [Test]
    public void Create_NormalScreenshotUserMessage_RemainsVisible()
    {
        var sessionEvent = new UserMessageEvent
        {
            Data = new UserMessageData { Content = "Describe the Game View screenshot." },
            Timestamp = DateTimeOffset.UnixEpoch,
        };

        var envelope = ServiceEventEnvelopeFactory.Create(1, "session-1", sessionEvent);

        Assert.That(envelope, Is.Not.Null);
        Assert.That(envelope!.Content, Is.EqualTo("Describe the Game View screenshot."));
    }

    [Test]
    public void Create_ToolExecutionStartEvent_WithIntentUsesIntentText()
    {
        var envelope = ServiceEventEnvelopeFactory.Create(1, "session-1", CreateToolExecutionStartEvent(
            toolName: "report_intent",
            arguments: CreateArguments(new { intent = "Getting Unity info" })));

        Assert.That(envelope, Is.Not.Null);
        Assert.That(envelope!.Content, Is.EqualTo("Getting Unity info"));
        Assert.That(envelope.StreamKey, Is.EqualTo("tool:call-1"));
    }

    [Test]
    public void Create_ToolExecutionStartEvent_WithMcpToolNameUsesMcpToolName()
    {
        var envelope = ServiceEventEnvelopeFactory.Create(1, "session-1", CreateToolExecutionStartEvent(
            toolName: "unity-code-mcp-stdio-over-http-get_unity_info",
            mcpToolName: "get_unity_info",
            arguments: CreateArguments(new { })));

        Assert.That(envelope, Is.Not.Null);
        Assert.That(envelope!.Content, Is.EqualTo("Calling 'get_unity_info' tool"));
    }

    [Test]
    public void Create_ToolExecutionStartEvent_WithTaskAgentUsesDescription()
    {
        var envelope = ServiceEventEnvelopeFactory.Create(1, "session-1", CreateToolExecutionStartEvent(
            toolName: "task",
            arguments: CreateArguments(new
            {
                agent_type = "task",
                description = "Get Unity Editor info"
            })));

        Assert.That(envelope, Is.Not.Null);
        Assert.That(envelope!.Content, Is.EqualTo("Calling task 'Get Unity Editor info'"));
    }

    [Test]
    public void Create_ToolExecutionCompleteEvent_WithDetailedContentUsesDetailedContent()
    {
        var envelope = ServiceEventEnvelopeFactory.Create(1, "session-1", CreateToolExecutionCompleteEvent());

        Assert.That(envelope, Is.Not.Null);
        Assert.That(envelope!.Content, Is.EqualTo("Result: Getting Unity info"));
        Assert.That(envelope.StreamKey, Is.EqualTo("tool:call-1"));
    }

    [Test]
    public void Create_SessionErrorEvent_WithProviderAuthenticationFailureUsesUnityCodeAgentSettingsGuidance()
    {
        var envelope = ServiceEventEnvelopeFactory.Create(1, "session-1", CreateSessionErrorEvent(
            "Provider rejected the request.",
            "authentication",
            401));

        Assert.That(envelope, Is.Not.Null);
        Assert.That(envelope!.Content, Does.Contain("BYOK provider authentication failed."));
        Assert.That(envelope.Content, Does.Contain("ApiKey"));
        Assert.That(envelope.Content, Does.Contain("UnityCodeAgent settings"));
        Assert.That(envelope.Content, Does.Not.Contain("COPILOT_PROVIDER_API_KEY"));
        Assert.That(envelope.Content, Does.Not.Contain("COPILOT_PROVIDER_BEARER_TOKEN"));
    }

    [Test]
    public void Create_SessionErrorEvent_WithProviderResourceNotFoundUsesUnityCodeAgentSettingsGuidance()
    {
        var envelope = ServiceEventEnvelopeFactory.Create(1, "session-1", CreateSessionErrorEvent(
            "Provider resource was not found.",
            "query",
            404));

        Assert.That(envelope, Is.Not.Null);
        Assert.That(envelope!.Content, Does.Contain("BYOK provider request failed."));
        Assert.That(envelope.Content, Does.Contain("BaseUrl"));
        Assert.That(envelope.Content, Does.Contain("selected model"));
        Assert.That(envelope.Content, Does.Contain("UnityCodeAgent settings"));
        Assert.That(envelope.Content, Does.Not.Contain("base URL"));
    }

    private static ToolExecutionStartEvent CreateToolExecutionStartEvent(string toolName, object arguments, string? mcpToolName = null)
    {
        var eventData = new ToolExecutionStartData
        {
            ToolCallId = "call-1",
            ToolName = toolName
        };
        SetProperty(eventData, "Arguments", arguments);

        if (mcpToolName is not null)
        {
            SetProperty(eventData, "McpToolName", mcpToolName);
        }

        return new ToolExecutionStartEvent
        {
            Data = eventData,
            Timestamp = DateTimeOffset.UnixEpoch
        };
    }

    private static ToolExecutionCompleteEvent CreateToolExecutionCompleteEvent()
    {
        var result = new ToolExecutionCompleteResult
        {
            Content = "Intent logged"
        };
        SetProperty(result, "DetailedContent", "Getting Unity info");

        var eventData = new ToolExecutionCompleteData
        {
            ToolCallId = "call-1",
            Success = true
        };
        SetProperty(eventData, "Result", result);

        return new ToolExecutionCompleteEvent
        {
            Data = eventData,
            Timestamp = DateTimeOffset.UnixEpoch
        };
    }

    private static SessionErrorEvent CreateSessionErrorEvent(string message, string errorType = "authentication", int? statusCode = null)
    {
        return new SessionErrorEvent
        {
            Data = new SessionErrorData
            {
                ErrorType = errorType,
                Message = message,
                StatusCode = statusCode
            },
            Timestamp = DateTimeOffset.UnixEpoch
        };
    }

    private static ModelCallFailureEvent CreateModelCallFailureEvent(string errorMessage, long imagePartCount)
    {
        return new ModelCallFailureEvent
        {
            Data = new ModelCallFailureData
            {
                ErrorMessage = errorMessage,
                StatusCode = 404,
                Source = ModelCallFailureSource.TopLevel,
                RequestFingerprint = new ModelCallFailureRequestFingerprint
                {
                    ImagePartCount = imagePartCount,
                    ImagePartsMissingMediaType = 0,
                    MessageCount = 5,
                    NamelessToolCallCount = 0,
                    ToolCallCount = 1,
                    ToolResultMessageCount = 1,
                },
            },
            Timestamp = DateTimeOffset.UnixEpoch,
        };
    }

    private static JsonElement CreateArguments(object value)
        => JsonSerializer.SerializeToElement(value);

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName);
        Assert.That(property, Is.Not.Null, $"Expected {target.GetType().Name} to expose property '{propertyName}'.");
        property!.SetValue(target, value);
    }
}
