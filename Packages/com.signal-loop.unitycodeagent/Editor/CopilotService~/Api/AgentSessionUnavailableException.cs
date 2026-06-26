namespace UnityCodeCopilot.Service.Api;

public sealed class AgentSessionUnavailableException : InvalidOperationException
{
    public AgentSessionUnavailableException(string sessionId)
        : base($"Session '{sessionId}' is not attached.")
    {
        SessionId = sessionId;
    }

    public string SessionId { get; }
}
