using Microsoft.AspNetCore.Http;

namespace UnityCodeCopilot.Service.Infrastructure;

public static class HealthResponse
{
    public static int GetStatusCode(ServiceHealth serviceHealth)
    {
        ArgumentNullException.ThrowIfNull(serviceHealth);

        return string.Equals(serviceHealth.State, "healthy", StringComparison.Ordinal)
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;
    }
}