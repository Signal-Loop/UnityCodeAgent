using Microsoft.Extensions.Hosting;
using UnityCodeCopilot.Service.Settings;

namespace UnityCodeCopilot.Service.Infrastructure;

public sealed class ManifestOwnershipMonitor : BackgroundService
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    private readonly ProjectPaths _paths;
    private readonly EndpointManifestStore _manifestStore;
    private readonly UnityCodeCopilotServiceLogger _log;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TimeSpan _pollInterval;
    private readonly int _currentProcessId;
    private readonly string _currentProjectId;
    private bool _hasObservedOwnManifest;

    public ManifestOwnershipMonitor(
        ProjectPaths paths,
        EndpointManifestStore manifestStore,
        UnityCodeCopilotServiceLogger log,
        IHostApplicationLifetime lifetime)
        : this(paths, manifestStore, log, lifetime, DefaultPollInterval, Environment.ProcessId)
    {
    }

    public ManifestOwnershipMonitor(
        ProjectPaths paths,
        EndpointManifestStore manifestStore,
        UnityCodeCopilotServiceLogger log,
        IHostApplicationLifetime lifetime,
        TimeSpan pollInterval,
        int currentProcessId)
    {
        _paths = paths;
        _manifestStore = manifestStore;
        _log = log;
        _lifetime = lifetime;
        _pollInterval = pollInterval;
        _currentProcessId = currentProcessId;
        _currentProjectId = EndpointManifestStore.CreateProjectId(paths.ProjectRoot);
    }

    public bool CheckOwnership()
    {
        var readResult = _manifestStore.ReadCurrentIdentity();
        if (readResult.Status == EndpointManifestReadStatus.Missing)
        {
            if (!_hasObservedOwnManifest)
            {
                _log.Debug(nameof(ManifestOwnershipMonitor), "Endpoint manifest not present before ownership was established.");
                return true;
            }

            _log.Warning(nameof(ManifestOwnershipMonitor), "Endpoint manifest disappeared after ownership was established; stopping service.",
                ("currentServiceProcessId", _currentProcessId));
            _lifetime.StopApplication();
            return false;
        }

        if (readResult.Status == EndpointManifestReadStatus.Invalid)
        {
            _log.Warning(nameof(ManifestOwnershipMonitor), "Endpoint manifest could not be read during ownership check.");
            return true;
        }

        var identity = readResult.Identity;
        if (!IsSameProject(identity))
        {
            _log.Debug(nameof(ManifestOwnershipMonitor), "Endpoint manifest belongs to a different project.",
                ("manifestProjectRoot", identity.ProjectRoot),
                ("manifestProjectId", identity.ProjectId));
            return true;
        }

        if (identity.ServiceProcessId > 0 && identity.ServiceProcessId != _currentProcessId)
        {
            _log.Warning(nameof(ManifestOwnershipMonitor), "Endpoint manifest ownership lost; stopping service.",
                ("currentServiceProcessId", _currentProcessId),
                ("manifestServiceProcessId", identity.ServiceProcessId));
            _lifetime.StopApplication();
            return false;
        }

        if (identity.ServiceProcessId == _currentProcessId)
        {
            _hasObservedOwnManifest = true;
        }

        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!CheckOwnership())
            {
                return;
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private bool IsSameProject(EndpointManifestIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.ProjectId)
            && string.Equals(identity.ProjectId, _currentProjectId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            NormalizeManifestProjectRoot(identity.ProjectRoot),
            _paths.ProjectRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeManifestProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return string.Empty;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot)).Replace('\\', '/');
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }
}
