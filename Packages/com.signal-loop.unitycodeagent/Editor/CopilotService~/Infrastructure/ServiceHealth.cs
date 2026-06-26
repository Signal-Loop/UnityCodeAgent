namespace UnityCodeCopilot.Service.Infrastructure;

public sealed class ServiceHealth
{
    private readonly object sync = new();
    private string state = "starting";
    private string? detail;

    public string State
    {
        get
        {
            lock (sync)
            {
                return state;
            }
        }
    }

    public string? Detail
    {
        get
        {
            lock (sync)
            {
                return detail;
            }
        }
    }

    public void SetHealthy()
    {
        lock (sync)
        {
            state = "healthy";
            detail = null;
        }
    }

    public void SetDegraded(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A degradation reason is required.", nameof(reason));
        }

        lock (sync)
        {
            state = "degraded";
            detail = reason;
        }
    }

    public void SetStopping()
    {
        lock (sync)
        {
            state = "stopping";
            detail = null;
        }
    }
}