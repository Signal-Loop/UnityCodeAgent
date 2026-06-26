namespace UnityCodeCopilot.Service.Api;

public sealed class AgentServiceAuthenticationException : InvalidOperationException
{
    public AgentServiceAuthenticationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
