using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace UnityCodeCopilot.Service.Telemetry;

public sealed class TelemetryOperation : IDisposable
{
    private readonly Activity? _activity;
    private readonly Counter<long> _operationCount;
    private readonly Histogram<double> _operationDurationMs;
    private readonly string _operationName;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private bool _disposed;
    private string _outcome = "success";

    public TelemetryOperation(
        string operationName,
        Activity? activity,
        Counter<long> operationCount,
        Histogram<double> operationDurationMs)
    {
        _operationName = operationName;
        _activity = activity;
        _operationCount = operationCount;
        _operationDurationMs = operationDurationMs;
    }

    public void SetTag(string name, object? value)
        => _activity?.SetTag(name, value);

    public void SetTags(params (string Name, object? Value)[] tags)
    {
        if (_activity == null)
        {
            return;
        }

        foreach (var (name, value) in tags)
        {
            _activity.SetTag(name, value);
        }
    }

    public void SetStatus(ActivityStatusCode code, string? description = null)
        => _activity?.SetStatus(code, description);

    public void SetOutcome(string outcome)
    {
        if (!string.IsNullOrWhiteSpace(outcome))
        {
            _outcome = outcome;
        }
    }

    public void MarkCancelled()
    {
        _outcome = "cancelled";
        _activity?.SetTag("operation.cancelled", true);
    }

    public void MarkError(Exception exception)
    {
        _outcome = "error";
        _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var tags = new TagList
        {
            { "operation", _operationName },
            { "outcome", _outcome },
        };

        _operationCount.Add(1, tags);
        _operationDurationMs.Record(_stopwatch.Elapsed.TotalMilliseconds, tags);
        _activity?.Dispose();
    }
}