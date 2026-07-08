using System;
using System.Collections.Generic;
using SignalLoop.UnityCodeAgent.Contracts;

namespace SignalLoop.UnityCodeAgent.UI
{
    public sealed class ChatClientCallResult
    {
        public ChatClientCallResult(bool success, IReadOnlyList<ChatClientUpdate> updates)
        {
            Success = success;
            Updates = updates ?? Array.Empty<ChatClientUpdate>();
        }

        public bool Success { get; }

        public IReadOnlyList<ChatClientUpdate> Updates { get; }
    }

    public abstract class ChatClientUpdate
    {
    }

    public enum ChatProgressIndicatorCommand
    {
        Default,
        Next,
    }

    public sealed class ChatSetProgressIndicatorUpdate : ChatClientUpdate
    {
        public ChatSetProgressIndicatorUpdate(ChatProgressIndicatorCommand command)
        {
            Command = command;
        }

        public ChatProgressIndicatorCommand Command { get; }
    }

    public sealed class ChatSetBusyStateUpdate : ChatClientUpdate
    {
        public ChatSetBusyStateUpdate(bool isBusy)
        {
            IsBusy = isBusy;
        }

        public bool IsBusy { get; }
    }

    public sealed class ChatShowSessionsUpdate : ChatClientUpdate
    {
        public ChatShowSessionsUpdate(IReadOnlyList<SessionSummaryDto> sessions, IReadOnlyCollection<string> unfinishedSessionIds = null)
        {
            Sessions = sessions ?? Array.Empty<SessionSummaryDto>();
            UnfinishedSessionIds = unfinishedSessionIds ?? Array.Empty<string>();
        }

        public IReadOnlyList<SessionSummaryDto> Sessions { get; }

        public IReadOnlyCollection<string> UnfinishedSessionIds { get; }
    }

    public sealed class ChatSetUserInput : ChatClientUpdate
    {
        public ChatSetUserInput(string userInput)
        {
            UserInput = userInput ?? string.Empty;
        }

        public string UserInput { get; }
    }

    public sealed class ChatSetModelLabelUpdate : ChatClientUpdate
    {
        public ChatSetModelLabelUpdate(string modelLabel)
        {
            ModelLabel = modelLabel ?? string.Empty;
        }

        public string ModelLabel { get; }
    }

    public sealed class ChatShowProgressMessageUpdate : ChatClientUpdate
    {
        public ChatShowProgressMessageUpdate(string message)
        {
            Message = message ?? string.Empty;
        }

        public string Message { get; }
    }

    public sealed class ChatShowMessagesUpdate : ChatClientUpdate
    {
        public ChatShowMessagesUpdate(IReadOnlyList<AgentServiceEventEnvelope> messages)
        {
            Messages = messages ?? Array.Empty<AgentServiceEventEnvelope>();
        }

        public IReadOnlyList<AgentServiceEventEnvelope> Messages { get; }
    }

    public sealed class ChatShowAgentEventUpdate : ChatClientUpdate
    {
        public ChatShowAgentEventUpdate(AgentServiceEventEnvelope agentEvent)
        {
            AgentEvent = agentEvent;
        }

        public AgentServiceEventEnvelope AgentEvent { get; }
    }

    public sealed class ChatShowErrorUpdate : ChatClientUpdate
    {
        public ChatShowErrorUpdate(string message, string stackTrace)
        {
            Message = message ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
        }

        public string Message { get; }

        public string StackTrace { get; }
    }

}
