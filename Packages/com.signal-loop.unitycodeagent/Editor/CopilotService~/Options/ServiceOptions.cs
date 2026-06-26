using UnityCodeCopilot.Service.Settings;

namespace UnityCodeCopilot.Service.Options;

public sealed class ServiceOptions
{
    public string ProjectRoot { get; set; } = string.Empty;
    public int UnityProcessId { get; set; }
    public int OrphanTimeoutSeconds { get; set; } = 5;
    public UnityCodeCopilotServiceLogger.LogLevel MinLogLevel { get; set; } = UnityCodeCopilotServiceLogger.LogLevel.Info;
    public bool LogToFile { get; set; } = true;
    public bool EnableTelemetry { get; set; } = true;
    public string? OtlpEndpoint { get; set; }
    public string? CliTelemetryFilePath { get; set; }
    public bool TelemetryCaptureContent { get; set; } = true;
}