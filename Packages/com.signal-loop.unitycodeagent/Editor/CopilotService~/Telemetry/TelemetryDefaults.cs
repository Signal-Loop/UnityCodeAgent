namespace UnityCodeCopilot.Service.Telemetry;

internal static class TelemetryDefaults
{
    public const string ServiceName = "UnityCodeCopilot.Service";
    public const string ActivitySourceName = "UnityCodeCopilot.Service";
    public const string MeterName = "UnityCodeCopilot.Service";
    public const string CliTelemetrySourceName = "UnityCodeCopilot.Cli";

    public static string ServiceVersion { get; } = typeof(TelemetryDefaults).Assembly.GetName().Version?.ToString() ?? "unknown";
}