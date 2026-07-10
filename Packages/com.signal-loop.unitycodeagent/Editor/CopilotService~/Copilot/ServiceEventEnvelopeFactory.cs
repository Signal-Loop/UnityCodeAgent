using System.Text.Json;
using GitHub.Copilot;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Api;

namespace UnityCodeCopilot.Service.Copilot
{
    public static class ServiceEventEnvelopeFactory
    {
        public static AgentServiceEventEnvelope? Create(long sequenceNumber, string sessionId, SessionEvent sessionEvent)
        {
            ArgumentNullException.ThrowIfNull(sessionEvent);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID must be provided.", nameof(sessionId));
            }
            if (sessionEvent.Type.Equals("assistant.streaming_delta", StringComparison.Ordinal))
            {
                return null;
            }
            if (sessionEvent is UserMessageEvent { Data.Content: ScreenshotSteering.Prompt })
            {
                return null;
            }

            var (content, streamKey) = BuildSessionEventContent(sessionEvent);
            return new AgentServiceEventEnvelope(
                sequenceNumber,
                sessionId,
                sessionEvent.Timestamp,
                content,
                streamKey ?? string.Empty,
                ResolveType(sessionEvent.Type),
                sessionEvent.ToJson() ?? string.Empty,
                !string.IsNullOrEmpty(sessionEvent.AgentId));
        }

        public static AgentServiceEventEnvelope? Create(
            long sequenceNumber,
            AgentEventType eventType,
            string sessionId,
            DateTimeOffset timestampUtc,
            string content,
            string? streamKey,
            string sourceJson)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID must be provided.", nameof(sessionId));
            }

            return new AgentServiceEventEnvelope(
                sequenceNumber,
                sessionId,
                timestampUtc,
                content,
                streamKey ?? string.Empty,
                eventType,
                sourceJson,
                false);
        }

        private static AgentEventType ResolveType(string? eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return AgentEventType.Unknown;
            }

            var normalizedEventType = eventType!;

            if (normalizedEventType.Contains("error", StringComparison.Ordinal))
            {
                return AgentEventType.Error;
            }

            if (normalizedEventType.Equals("model.call_failure", StringComparison.Ordinal))
            {
                return AgentEventType.Error;
            }

            if (normalizedEventType.Equals("user.message", StringComparison.Ordinal))
            {
                return AgentEventType.UserMessage;
            }

            if (normalizedEventType.Equals("session.idle", StringComparison.Ordinal))
            {
                return AgentEventType.SessionIdle;
            }

            if (normalizedEventType.Equals("assistant.reasoning_delta", StringComparison.Ordinal))
            {
                return AgentEventType.ReasoningDelta;
            }

            if (normalizedEventType.Equals("assistant.reasoning", StringComparison.Ordinal))
            {
                return AgentEventType.Reasoning;
            }

            if (normalizedEventType.Equals("assistant.message_delta", StringComparison.Ordinal))
            {
                return AgentEventType.AssistantDelta;
            }

            if (normalizedEventType.Equals("assistant.message", StringComparison.Ordinal))
            {
                return AgentEventType.AssistantMessage;
            }

            if (normalizedEventType.StartsWith("tool.", StringComparison.Ordinal)
                || normalizedEventType.StartsWith("external_tool.", StringComparison.Ordinal)
                || string.Equals(normalizedEventType, "mcp_app.tool_call_complete", StringComparison.Ordinal))
            {
                return AgentEventType.Tool;
            }

            if (normalizedEventType.StartsWith("skill.", StringComparison.Ordinal))
            {
                return AgentEventType.Skill;
            }

            if (normalizedEventType.StartsWith("subagent.", StringComparison.Ordinal))
            {
                return AgentEventType.Subagent;
            }

            if (normalizedEventType.StartsWith("mcp.", StringComparison.Ordinal))
            {
                return AgentEventType.Mcp;
            }

            return AgentEventType.Unknown;
        }



        private static (string Content, string? StreamKey) BuildSessionEventContent(SessionEvent sessionEvent)
            => sessionEvent switch
            {
                UserMessageEvent { Data: { } data } => (GetContentOrFallback(data.Content), null),
                AssistantReasoningDeltaEvent { Data: { } data } => (
                    GetContentOrFallback(data.DeltaContent),
                    BuildStreamKey("reasoning", data.ReasoningId)),
                AssistantReasoningEvent { Data: { } data } => (
                    GetContentOrFallback(data.Content),
                    BuildStreamKey("reasoning", data.ReasoningId)),
                AssistantMessageDeltaEvent { Data: { } data } => (
                    GetContentOrFallback(data.DeltaContent),
                    BuildStreamKey("assistant", data.MessageId)),
                AssistantMessageEvent { Data: { } data } => (
                    GetContentOrFallback(data.Content),
                    BuildStreamKey("assistant", data.MessageId)),
                ToolExecutionStartEvent { Data: { } data } => (
                    BuildToolStartSummary(data),
                    BuildStreamKey("tool", data.ToolCallId)),
                ToolExecutionCompleteEvent { Data: { } data } => (
                    BuildToolCompleteSummary(data),
                    BuildStreamKey("tool", data.ToolCallId)),
                SessionIdleEvent { Data: { } data } => (
                    data.Aborted == true ? "Session became idle after abort." : "Session became idle.",
                    null),
                SessionErrorEvent { Data: { } data } => (
                    BuildSessionErrorSummary(data),
                    null),
                ModelCallFailureEvent { Data: { } data } when IsUnsupportedImageInputFailure(data) => (
                    AgentServiceAuthMessages.ForUnsupportedImageInput(),
                    null),
                _ => (GetContentOrFallback(null), null),
            };

        internal static bool IsUnsupportedImageInputFailure(ModelCallFailureData data)
            => data.RequestFingerprint?.ImagePartCount > 0
                && !string.IsNullOrWhiteSpace(data.ErrorMessage)
                && data.ErrorMessage.Contains("image", StringComparison.OrdinalIgnoreCase);



        private static string GetContentOrFallback(string? content)
            => content ?? string.Empty;

        private static string BuildSessionErrorSummary(SessionErrorData data)
        {
            var message = GetContentOrFallback(data.Message);
            if (CopilotAuthFailureClassifier.IsByokAuthenticationFailure(data))
            {
                return AgentServiceAuthMessages.ForByokAuthenticationFailure();
            }

            if (CopilotAuthFailureClassifier.IsByokProviderConfigurationFailure(data))
            {
                return AgentServiceAuthMessages.ForByokProviderConfigurationFailure();
            }

            return message;
        }

        private static bool IsAuthenticationError(SessionErrorData data)
            => string.Equals(data.ErrorType, "authentication", StringComparison.OrdinalIgnoreCase)
                || string.Equals(data.ErrorType, "authorization", StringComparison.OrdinalIgnoreCase);

        private static string BuildStreamKey(string prefix, string value)
            => $"{prefix}:{value}";

        private static string? SerializeJson(object? value)
            => value == null ? null : JsonSerializer.Serialize(value);

        private static string BuildToolStartSummary(ToolExecutionStartData data)
        {
            if (!string.IsNullOrWhiteSpace(data.McpToolName))
            {
                return $"Calling '{data.McpToolName}' tool";
            }

            if (TryGetStringArgument(data.Arguments, "intent", out var intent)
                && !string.IsNullOrWhiteSpace(intent)
                && string.Equals(data.ToolName, "report_intent", StringComparison.Ordinal))
            {
                return intent;
            }

            if (TryGetStringArgument(data.Arguments, "agent_type", out var agentType)
                && string.Equals(agentType, "task", StringComparison.OrdinalIgnoreCase)
                && TryGetStringArgument(data.Arguments, "description", out var description)
                && !string.IsNullOrWhiteSpace(description))
            {
                return $"Calling task '{description}'";
            }

            var argumentsJson = SerializeJson(data.Arguments);
            return string.IsNullOrWhiteSpace(argumentsJson)
                ? $"Calling {data.ToolName}"
                : $"Calling {data.ToolName} with {argumentsJson}";
        }

        private static string BuildToolCompleteSummary(ToolExecutionCompleteData data)
        {
            if (data.Success)
            {
                var resultText = data.Result?.DetailedContent ?? data.Result?.Content;
                return string.IsNullOrWhiteSpace(resultText)
                    ? "Completed"
                    : $"Result: {resultText}";
            }

            return $"Tool failed: {data.Error?.Message ?? "Unknown tool error."}";
        }

        private static bool TryGetStringArgument(object? arguments, string propertyName, out string? value)
        {
            value = null;

            if (arguments is null)
            {
                return false;
            }

            JsonElement argumentsElement;
            try
            {
                argumentsElement = arguments is JsonElement jsonElement
                    ? jsonElement
                    : JsonSerializer.SerializeToElement(arguments);
            }
            catch (NotSupportedException)
            {
                return false;
            }

            if (argumentsElement.ValueKind != JsonValueKind.Object
                || !argumentsElement.TryGetProperty(propertyName, out var propertyValue)
                || propertyValue.ValueKind == JsonValueKind.Null
                || propertyValue.ValueKind == JsonValueKind.Undefined)
            {
                return false;
            }

            value = propertyValue.ValueKind == JsonValueKind.String
                ? propertyValue.GetString()
                : propertyValue.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

    }
}
