using UnityCodeCopilot.Service.Settings;

namespace UnityCodeCopilot.Service.Options;

public sealed class ServiceOptions
{
    public string ProjectRoot { get; set; } = string.Empty;
    public int UnityProcessId { get; set; }
    public bool NoUnity { get; set; }
    public int OrphanTimeoutSeconds { get; set; } = 20;
    public UnityCodeCopilotServiceLogger.LogLevel MinLogLevel { get; set; } = UnityCodeCopilotServiceLogger.LogLevel.Info;
    public bool LogToFile { get; set; } = true;
    public bool EnableTelemetry { get; set; } = true;
    public string? OtlpEndpoint { get; set; }
    public string? TelemetryFilePath { get; set; }
    public bool TelemetryCaptureContent { get; set; } = true;
}
