using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnityCodeCopilot.Service.Options;
using UnityCodeCopilot.Service.Settings;

namespace UnityCodeCopilot.Service.Infrastructure;

public sealed class ServiceRuntimeLifecycle
{
    private readonly EndpointManifestStore _manifestStore;
    private readonly UnityCodeCopilotServiceLogger _log;
    private readonly ServiceHealth _serviceHealth;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptions<ServiceOptions> _options;

    public ServiceRuntimeLifecycle(
        EndpointManifestStore manifestStore,
        UnityCodeCopilotServiceLogger log,
        ServiceHealth serviceHealth,
        IHostApplicationLifetime applicationLifetime,
        IHostEnvironment hostEnvironment,
        IOptions<ServiceOptions> options)
    {
        _manifestStore = manifestStore;
        _log = log;
        _serviceHealth = serviceHealth;
        _applicationLifetime = applicationLifetime;
        _hostEnvironment = hostEnvironment;
        _options = options;
    }

    public void OnStarted(IEnumerable<string> addresses)
    {
        try
        {
            if (!TryResolvePort(addresses, out var port))
            {
                if (_hostEnvironment.IsEnvironment("Testing"))
                {
                    _serviceHealth.SetHealthy();
                    _log.Info(nameof(ServiceRuntimeLifecycle), "Skipping endpoint manifest publication for the in-memory test host.",
                        ("environment", _hostEnvironment.EnvironmentName));
                    return;
                }

                throw new InvalidOperationException("The service endpoint port could not be determined from the bound addresses.");
            }

            _log.Info(nameof(ServiceRuntimeLifecycle), "Publishing endpoint manifest.",
                ("port", port),
                ("unityProcessId", _options.Value.UnityProcessId),
                ("serviceProcessId", Environment.ProcessId));

            //TODO: consoder replacing writing manifest to file with returning manifest in service start output and let the host process manage it
            _manifestStore.WriteAsync(
                port,
                _options.Value.UnityProcessId,
                Environment.ProcessId,
                CancellationToken.None).GetAwaiter().GetResult();
            _serviceHealth.SetHealthy();
            _log.Info(nameof(ServiceRuntimeLifecycle), "Service marked healthy.", ("port", port));
        }
        catch (Exception exception)
        {
            _serviceHealth.SetDegraded("Endpoint manifest could not be published.");
            _log.Error(nameof(ServiceRuntimeLifecycle), "Endpoint manifest publication failed.", exception);
            _applicationLifetime.StopApplication();
        }
    }

    public void OnStopping()
    {
        _serviceHealth.SetStopping();
        _log.Info(nameof(ServiceRuntimeLifecycle), "Service stopping.", ("serviceProcessId", Environment.ProcessId));

        try
        {
            _manifestStore.DeleteIfOwned(Environment.ProcessId);
            _log.Info(nameof(ServiceRuntimeLifecycle), "Manifest cleanup attempted.", ("serviceProcessId", Environment.ProcessId));
        }
        catch (Exception exception)
        {
            _log.Error(nameof(ServiceRuntimeLifecycle), "Manifest cleanup failed.", exception, ("serviceProcessId", Environment.ProcessId));
        }
    }

    private static bool TryResolvePort(IEnumerable<string> addresses, out int port)
    {
        foreach (var address in addresses)
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Port > 0)
            {
                port = uri.Port;
                return true;
            }
        }

        port = 0;
        return false;
    }
}