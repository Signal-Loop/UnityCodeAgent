using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace UnityCodeCopilot.Service.Telemetry;

public sealed class CopilotTelemetry
{
    private readonly ActivitySource _activitySource = new(TelemetryDefaults.ActivitySourceName, TelemetryDefaults.ServiceVersion);
    private readonly Meter _meter = new(TelemetryDefaults.MeterName, TelemetryDefaults.ServiceVersion);
    private readonly Counter<long> _operationCount;
    private readonly Histogram<double> _operationDurationMs;
    private readonly Histogram<int> _promptLengthCharacters;
    private readonly Counter<long> _runtimeEventCount;

    public CopilotTelemetry()
    {
        _operationDurationMs = _meter.CreateHistogram<double>(
            "unitycodecopilot.service.operation.duration",
            unit: "ms",
            description: "Duration of UnityCodeCopilot service operations.");

        _promptLengthCharacters = _meter.CreateHistogram<int>(
            "unitycodecopilot.service.prompt.length",
            unit: "{char}",
            description: "Prompt length in characters for session send operations.");

        _operationCount = _meter.CreateCounter<long>(
            "unitycodecopilot.service.operation.count",
            unit: "{operation}",
            description: "Count of UnityCodeCopilot service operations.");

        _runtimeEventCount = _meter.CreateCounter<long>(
            "unitycodecopilot.service.runtime.event.count",
            unit: "{event}",
            description: "Count of runtime events received from the Copilot SDK session.");
    }

    public TelemetryOperation StartOperation(string operationName, ActivityKind kind = ActivityKind.Internal)
        => new TelemetryOperation(operationName, _activitySource.StartActivity(operationName, kind), _operationCount, _operationDurationMs);

    public async Task ExecuteAsync(string operationName, Func<TelemetryOperation, Task> action, ActivityKind kind = ActivityKind.Internal)
    {
        using var operation = StartOperation(operationName, kind);

        try
        {
            await action(operation);
        }
        catch (OperationCanceledException)
        {
            operation.MarkCancelled();
            throw;
        }
        catch (Exception exception)
        {
            operation.MarkError(exception);
            throw;
        }
    }

    public async Task<T> ExecuteAsync<T>(string operationName, Func<TelemetryOperation, Task<T>> action, ActivityKind kind = ActivityKind.Internal)
    {
        using var operation = StartOperation(operationName, kind);

        try
        {
            return await action(operation);
        }
        catch (OperationCanceledException)
        {
            operation.MarkCancelled();
            throw;
        }
        catch (Exception exception)
        {
            operation.MarkError(exception);
            throw;
        }
    }

    public void RecordPromptLength(int promptLength)
        => _promptLengthCharacters.Record(promptLength);

    public void RecordRuntimeEvent(string eventType)
    {
        var tags = new TagList
        {
            { "event.type", eventType },
        };

        _runtimeEventCount.Add(1, tags);
    }
}