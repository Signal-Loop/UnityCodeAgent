using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using UnityCodeCopilot.Service.Options;

namespace UnityCodeCopilot.Service.Telemetry;

internal static class TelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddCopilotTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        if (IsTelemetryEnabled(configuration))
        {
            ConfigureOpenTelemetry(services, configuration);
        }

        services.AddSingleton<CopilotTelemetry>();
        services.AddSingleton<CliTelemetryConfigFactory>();
        return services;
    }

    private static bool IsTelemetryEnabled(IConfiguration configuration)
        => !bool.TryParse(configuration[nameof(ServiceOptions.EnableTelemetry)], out var enabled) || enabled;

    private static void ConfigureOpenTelemetry(IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = ResolveOtlpEndpoint(configuration);

        var openTelemetry = services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: TelemetryDefaults.ServiceName,
                serviceVersion: TelemetryDefaults.ServiceVersion));

        openTelemetry.WithTracing(tracing =>
        {
            tracing
                .SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))
                .AddSource(TelemetryDefaults.ActivitySourceName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.Filter = static httpContext => !httpContext.Request.Path.StartsWithSegments("/health");
                });

            if (TryCreateOtlpSignalEndpoint(otlpEndpoint, "traces", out var tracesEndpoint))
            {
                tracing.AddOtlpExporter(options =>
                {
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    options.Endpoint = tracesEndpoint;
                });
            }
        });

        openTelemetry.WithMetrics(metrics =>
        {
            metrics
                .AddMeter(TelemetryDefaults.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation();

            if (TryCreateOtlpSignalEndpoint(otlpEndpoint, "metrics", out var metricsEndpoint))
            {
                metrics.AddOtlpExporter(options =>
                {
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    options.Endpoint = metricsEndpoint;
                });
            }
        });
    }

    private static string? ResolveOtlpEndpoint(IConfiguration configuration)
        => configuration[nameof(ServiceOptions.OtlpEndpoint)] ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

    private static bool TryCreateOtlpSignalEndpoint(string? baseEndpoint, string signal, out Uri endpoint)
    {
        endpoint = default!;
        if (!Uri.TryCreate(baseEndpoint, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var signalPath = string.IsNullOrEmpty(basePath)
            ? $"/v1/{signal}"
            : $"{basePath}/v1/{signal}";

        endpoint = new UriBuilder(baseUri)
        {
            Path = signalPath,
        }.Uri;
        return true;
    }
}