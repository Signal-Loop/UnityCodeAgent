using System.Net;
using GitHub.Copilot;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Api;

namespace UnityCodeCopilot.Service.Copilot;

internal static class CopilotAuthFailureClassifier
{
    public static bool IsAuthenticationFailure(Exception exception)
        => HasAuthenticationStatusCode(exception) || HasAuthenticationMessage(exception);

    public static AgentServiceAuthenticationException CreateAuthenticationException(ProviderConfigDto? provider, Exception exception)
        => new(AgentServiceAuthMessages.ForProvider(provider), exception);

    public static bool IsByokAuthenticationFailure(SessionErrorData data)
        => data.StatusCode == 401;

    public static bool IsByokProviderConfigurationFailure(SessionErrorData data)
        => data.StatusCode == 404;

    public static string GetInnermostMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException != null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    private static bool HasAuthenticationStatusCode(Exception exception)
        => EnumerateExceptions(exception)
            .OfType<HttpRequestException>()
            .Any(httpException => httpException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

    private static bool HasAuthenticationMessage(Exception exception)
        => EnumerateMessages(exception).Any(message =>
            message.Contains("401", StringComparison.OrdinalIgnoreCase)
            || message.Contains("403", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not authenticated", StringComparison.OrdinalIgnoreCase)
            || message.Contains("authentication", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static IEnumerable<string> EnumerateMessages(Exception exception)
    {
        foreach (var current in EnumerateExceptions(exception))
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                yield return current.Message;
            }
        }
    }
}
