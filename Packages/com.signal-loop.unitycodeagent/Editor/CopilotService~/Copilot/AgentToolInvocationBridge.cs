using System.Collections.Concurrent;
using GitHub.Copilot;
using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Api;
using UnityCodeCopilot.Service.Settings;

namespace UnityCodeCopilot.Service.Copilot;

public sealed class AgentToolInvocationBridge
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<PendingToolCallKey, PendingToolCall> _pendingCalls = new();
    private readonly EventStreamBroker _broker;
    private readonly UnityCodeCopilotServiceLogger _log;

    public AgentToolInvocationBridge(EventStreamBroker broker, UnityCodeCopilotServiceLogger log)
    {
        _broker = broker;
        _log = log;
    }

    public async Task<ToolResultAIContent> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        if (string.IsNullOrWhiteSpace(invocation.SessionId))
        {
            throw new InvalidOperationException("Tool invocation did not include a session id.");
        }

        if (string.IsNullOrWhiteSpace(invocation.ToolName))
        {
            throw new InvalidOperationException("Tool invocation did not include a tool name.");
        }

        var callId = string.IsNullOrWhiteSpace(invocation.ToolCallId)
            ? Guid.NewGuid().ToString("N")
            : invocation.ToolCallId;
        var argumentsJson = invocation.Arguments.HasValue
            ? invocation.Arguments.Value.GetRawText()
            : "{}";

        var request = new AgentToolInvocationRequestDto(
            callId,
            invocation.SessionId,
            invocation.ToolName,
            argumentsJson);

        var pendingCall = new PendingToolCall(request);
        var key = PendingToolCallKey.From(request);
        if (!_pendingCalls.TryAdd(key, pendingCall))
        {
            throw new InvalidOperationException($"A Unity tool invocation with call id '{callId}' is already pending for session '{request.SessionId}'.");
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(DefaultTimeout);
        using var cancellationRegistration = timeoutCancellation.Token.Register(
            static state =>
            {
                var registration = (CancellationRegistration)state!;
                registration.PendingCall.TryCancel(registration.CancellationToken);
            },
            new CancellationRegistration(pendingCall, timeoutCancellation.Token));

        try
        {
            _log.Info(nameof(AgentToolInvocationBridge), "Publishing Unity tool invocation request.",
                ("sessionId", request.SessionId),
                ("toolName", request.ToolName),
                ("callId", request.CallId));

            _broker.Publish(new AgentServiceEventEnvelope(
                0,
                request.SessionId,
                DateTimeOffset.UtcNow,
                $"Calling Unity tool '{request.ToolName}'",
                $"unity-tool:{request.CallId}",
                AgentEventType.ToolInvocationRequest,
                JsonConvert.SerializeObject(request),
                false));

            var result = await pendingCall.Task.ConfigureAwait(false);
            return new ToolResultAIContent(ToToolResultObject(result));
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Unity tool '{request.ToolName}' did not return a result within {DefaultTimeout.TotalSeconds:0} seconds.");
        }
        finally
        {
            _pendingCalls.TryRemove(key, out _);
        }
    }

    public bool TryComplete(AgentToolInvocationResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (string.IsNullOrWhiteSpace(result.CallId)
            || string.IsNullOrWhiteSpace(result.SessionId)
            || string.IsNullOrWhiteSpace(result.ToolName))
        {
            return false;
        }

        var key = PendingToolCallKey.From(result);
        if (!_pendingCalls.TryGetValue(key, out var pendingCall))
        {
            _log.Warning(nameof(AgentToolInvocationBridge), "Received result for unknown Unity tool invocation.",
                ("sessionId", result.SessionId),
                ("toolName", result.ToolName),
                ("callId", result.CallId));
            return false;
        }

        _log.Info(nameof(AgentToolInvocationBridge), "Received Unity tool invocation result.",
            ("sessionId", result.SessionId),
            ("toolName", result.ToolName),
            ("callId", result.CallId),
            ("isError", result.IsError));

        return pendingCall.TryComplete(result);
    }

    public bool TryClaim(
        AgentToolInvocationResultDto result,
        out AgentToolInvocationCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(result);
        completion = default!;

        if (string.IsNullOrWhiteSpace(result.CallId)
            || string.IsNullOrWhiteSpace(result.SessionId)
            || string.IsNullOrWhiteSpace(result.ToolName)
            || !_pendingCalls.TryRemove(PendingToolCallKey.From(result), out var pendingCall))
        {
            return false;
        }

        completion = new AgentToolInvocationCompletion(pendingCall.TryComplete);
        return true;
    }

    private static ToolResultObject ToToolResultObject(AgentToolInvocationResultDto result)
    {
        return new ToolResultObject
        {
            TextResultForLlm = result.TextResult ?? string.Empty,
            ResultType = result.IsError ? "failure" : "success",
            Error = result.IsError ? result.Error ?? result.TextResult : null,
            BinaryResultsForLlm = result.BinaryResults?.Select(ToToolBinaryResult).ToList(),
        };
    }

    private static ToolBinaryResult ToToolBinaryResult(AgentToolBinaryResultDto binary)
        => new()
        {
            Data = binary.Data,
            MimeType = binary.MimeType,
            Type = new ToolBinaryResultType(binary.Type),
            Description = binary.Description,
        };

    private readonly record struct PendingToolCallKey(string SessionId, string CallId, string ToolName)
    {
        public static PendingToolCallKey From(AgentToolInvocationRequestDto request)
            => new(request.SessionId, request.CallId, request.ToolName);

        public static PendingToolCallKey From(AgentToolInvocationResultDto result)
            => new(result.SessionId, result.CallId, result.ToolName);
    }

    private sealed class PendingToolCall
    {
        private readonly TaskCompletionSource<AgentToolInvocationResultDto> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingToolCall(AgentToolInvocationRequestDto request)
        {
            Request = request;
        }

        public AgentToolInvocationRequestDto Request { get; }

        public Task<AgentToolInvocationResultDto> Task => _completion.Task;

        public bool TryComplete(AgentToolInvocationResultDto result)
            => _completion.TrySetResult(result);

        public bool TryCancel(CancellationToken cancellationToken)
            => _completion.TrySetCanceled(cancellationToken);
    }

    public sealed class AgentToolInvocationCompletion
    {
        private readonly Func<AgentToolInvocationResultDto, bool> _tryComplete;

        internal AgentToolInvocationCompletion(Func<AgentToolInvocationResultDto, bool> tryComplete)
        {
            _tryComplete = tryComplete;
        }

        public bool TryComplete(AgentToolInvocationResultDto result)
            => _tryComplete(result);
    }

    private readonly record struct CancellationRegistration(PendingToolCall PendingCall, CancellationToken CancellationToken);
}
