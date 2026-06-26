using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnityCodeCopilot.Service.Api;
using UnityCodeCopilot.Service.Copilot;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Options;
using UnityCodeCopilot.Service.Settings;
using UnityCodeCopilot.Service.Telemetry;

namespace UnityCodeCopilot.Service;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUnityCodeCopilotService(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<ServiceOptions>, ServiceOptionsValidator>();
        services.AddCopilotTelemetry(configuration);

        services
            .AddOptions<ServiceOptions>()
            .Bind(configuration)
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceOptions>>().Value;
            return new ProjectPaths(options.ProjectRoot);
        });

        services.AddSingleton<EndpointManifestStore>();
        services.AddSingleton<IProcessInfoProvider, ProcessInfoProvider>();
        services.AddSingleton(sp => new ServiceRuntimeLifecycle(
            sp.GetRequiredService<EndpointManifestStore>(),
            sp.GetRequiredService<UnityCodeCopilotServiceLogger>(),
            sp.GetRequiredService<ServiceHealth>(),
            sp.GetRequiredService<IHostApplicationLifetime>(),
            sp.GetRequiredService<IHostEnvironment>(),
            sp.GetRequiredService<IOptions<ServiceOptions>>()));
        services.AddHostedService(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceOptions>>().Value;
            return new ParentProcessMonitor(
                options.UnityProcessId,
                TimeSpan.FromSeconds(options.OrphanTimeoutSeconds),
                sp.GetRequiredService<IHostApplicationLifetime>(),
                sp.GetRequiredService<IProcessInfoProvider>());
        });
        services.AddHostedService(sp => new ManifestOwnershipMonitor(
            sp.GetRequiredService<ProjectPaths>(),
            sp.GetRequiredService<EndpointManifestStore>(),
            sp.GetRequiredService<UnityCodeCopilotServiceLogger>(),
            sp.GetRequiredService<IHostApplicationLifetime>()));

        services.AddSingleton<ServiceHealth>();
        services.AddSingleton(sp => new UnityCodeCopilotServiceLogger(
            sp.GetRequiredService<ProjectPaths>(),
            sp.GetRequiredService<IOptions<ServiceOptions>>().Value));
        services.AddSingleton<EventStreamBroker>();
        services.AddSingleton<McpConfigLoader>();
        services.AddSingleton<AgentToolInvocationBridge>();
        services.AddSingleton<ByokOpenAiProvider>();
        services.AddSingleton<CopilotClientHost>();
        services.AddSingleton<IAgentRuntimeHost>(sp => sp.GetRequiredService<CopilotClientHost>());
        services.AddSingleton<CopilotModelCatalog>();
        services.AddSingleton<IAgentModelCatalog>(sp => sp.GetRequiredService<CopilotModelCatalog>());
        services.AddHostedService(sp => sp.GetRequiredService<CopilotClientHost>());
        services.AddSingleton<CopilotSessionManager>();
        services.AddSingleton<IAgentSessionService>(sp => sp.GetRequiredService<CopilotSessionManager>());

        return services;
    }
}
