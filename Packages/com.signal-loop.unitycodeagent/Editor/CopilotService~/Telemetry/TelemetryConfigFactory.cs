using GitHub.Copilot;
using Microsoft.Extensions.Options;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Options;

namespace UnityCodeCopilot.Service.Telemetry;

public sealed class TelemetryConfigFactory
{
    private readonly bool _enableTelemetry;
    private readonly ServiceOptions _options;
    private readonly ProjectPaths _paths;

    public TelemetryConfigFactory(ProjectPaths paths, IOptions<ServiceOptions> options)
    {
        _paths = paths;
        _options = options.Value;
        _enableTelemetry = _options.EnableTelemetry;
    }

    public TelemetryConfig? Create()
    {
        if (!_enableTelemetry)
        {
            return null;
        }

        var otlpEndpoint = ResolveOtlpEndpoint();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            return new TelemetryConfig
            {
                OtlpEndpoint = otlpEndpoint,
                ExporterType = "otlp-http",
                SourceName = TelemetryDefaults.CliTelemetrySourceName,
                CaptureContent = _options.TelemetryCaptureContent,
            };
        }

        Directory.CreateDirectory(_paths.LogsRoot.Replace('/', Path.DirectorySeparatorChar));
        return new TelemetryConfig
        {
            FilePath = ResolveTelemetryFilePath(),
            ExporterType = "file",
            SourceName = TelemetryDefaults.CliTelemetrySourceName,
            CaptureContent = _options.TelemetryCaptureContent,
        };
    }

    private string? ResolveOtlpEndpoint()
        => string.IsNullOrWhiteSpace(_options.OtlpEndpoint)
            ? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            : _options.OtlpEndpoint;

    private string ResolveTelemetryFilePath()
    {
        if (string.IsNullOrWhiteSpace(_options.TelemetryFilePath))
        {
            return $"{_paths.LogsRoot}/telemetry.jsonl";
        }

        var filePath = Path.IsPathRooted(_options.TelemetryFilePath)
            ? Path.GetFullPath(_options.TelemetryFilePath)
            : Path.GetFullPath(_options.TelemetryFilePath, _paths.ProjectRoot.Replace('/', Path.DirectorySeparatorChar));

        return filePath.Replace('\\', '/');
    }
}
