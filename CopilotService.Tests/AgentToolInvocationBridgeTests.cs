using System.Text.Json;
using GitHub.Copilot;
using Newtonsoft.Json;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Api;
using UnityCodeCopilot.Service.Copilot;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Options;
using UnityCodeCopilot.Service.Settings;

namespace UnityCodeCopilot.Service.Tests;

public sealed class AgentToolInvocationBridgeTests
{
    [Test]
    public async Task InvokeAsync_PublishesRequestAndCompletesWithUnityResult()
    {
        var broker = new EventStreamBroker();
        var bridge = new AgentToolInvocationBridge(broker, CreateLogger());
        var invocation = new ToolInvocation
        {
            SessionId = "session-1",
            ToolCallId = "call-1",
            ToolName = "get_unity_info",
            Arguments = System.Text.Json.JsonSerializer.SerializeToElement(new { })
        };

        var invocationTask = bridge.InvokeAsync(invocation, CancellationToken.None);

        using var subscription = broker.Subscribe(0);
        var envelope = subscription.RetainedEvents.Single();
        Assert.That(envelope.Type, Is.EqualTo(AgentEventType.ToolInvocationRequest));
        Assert.That(envelope.SessionId, Is.EqualTo("session-1"));

        var request = JsonConvert.DeserializeObject<AgentToolInvocationRequestDto>(envelope.SourceJson);
        Assert.That(request, Is.Not.Null);
        Assert.That(request!.CallId, Is.EqualTo("call-1"));
        Assert.That(request.ToolName, Is.EqualTo("get_unity_info"));
        Assert.That(request.ArgumentsJson, Is.EqualTo("{}"));

        var completed = bridge.TryComplete(new AgentToolInvocationResultDto(
            "call-1",
            "session-1",
            "get_unity_info",
            false,
            "Unity project info"));

        Assert.That(completed, Is.True);

        var content = await invocationTask;
        Assert.That(content.Result.ResultType, Is.EqualTo("success"));
        Assert.That(content.Result.TextResultForLlm, Is.EqualTo("Unity project info"));
    }

    [Test]
    public async Task TryComplete_RejectsResultForDifferentSessionOrTool()
    {
        var broker = new EventStreamBroker();
        var bridge = new AgentToolInvocationBridge(broker, CreateLogger());
        var invocation = new ToolInvocation
        {
            SessionId = "session-1",
            ToolCallId = "call-1",
            ToolName = "get_unity_info",
            Arguments = System.Text.Json.JsonSerializer.SerializeToElement(new { })
        };

        var invocationTask = bridge.InvokeAsync(invocation, CancellationToken.None);

        Assert.That(bridge.TryComplete(new AgentToolInvocationResultDto(
            "call-1",
            "session-2",
            "get_unity_info",
            false,
            "Wrong session")), Is.False);

        Assert.That(bridge.TryComplete(new AgentToolInvocationResultDto(
            "call-1",
            "session-1",
            "read_unity_console_logs",
            false,
            "Wrong tool")), Is.False);

        Assert.That(bridge.TryComplete(new AgentToolInvocationResultDto(
            "call-1",
            "session-1",
            "get_unity_info",
            false,
            "Unity project info")), Is.True);

        var content = await invocationTask;
        Assert.That(content.Result.TextResultForLlm, Is.EqualTo("Unity project info"));
    }

    private static UnityCodeCopilotServiceLogger CreateLogger()
        => new(
            new ProjectPaths(TestContext.CurrentContext.WorkDirectory),
            new ServiceOptions
            {
                LogToFile = false,
                MinLogLevel = UnityCodeCopilotServiceLogger.LogLevel.Off,
            });
}
