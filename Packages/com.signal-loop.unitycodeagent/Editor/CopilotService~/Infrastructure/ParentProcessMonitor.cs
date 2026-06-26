using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace UnityCodeCopilot.Service.Infrastructure;

public sealed class ParentProcessMonitor : BackgroundService
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    private readonly int _unityProcessId;
    private readonly TimeSpan _orphanTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IProcessInfoProvider _processInfoProvider;
    private readonly DateTimeOffset? _expectedUnityProcessStartTimeUtc;

    public ParentProcessMonitor(
        int unityProcessId,
        TimeSpan orphanTimeout,
        IHostApplicationLifetime lifetime,
        IProcessInfoProvider processInfoProvider)
        : this(unityProcessId, orphanTimeout, DefaultPollInterval, lifetime, processInfoProvider)
    {
    }

    public ParentProcessMonitor(
        int unityProcessId,
        TimeSpan orphanTimeout,
        TimeSpan pollInterval,
        IHostApplicationLifetime lifetime,
        IProcessInfoProvider processInfoProvider)
    {
        _unityProcessId = unityProcessId;
        _orphanTimeout = orphanTimeout;
        _pollInterval = pollInterval;
        _lifetime = lifetime;
        _processInfoProvider = processInfoProvider;
        _expectedUnityProcessStartTimeUtc =
            processInfoProvider.TryGetStartTimeUtc(unityProcessId, out var startTimeUtc)
                ? startTimeUtc
                : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!IsOriginalProcessAlive())
            {
                await Task.Delay(_orphanTimeout, stoppingToken);

                if (!IsOriginalProcessAlive())
                {
                    _lifetime.StopApplication();
                    return;
                }
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private bool IsOriginalProcessAlive()
    {
        if (_expectedUnityProcessStartTimeUtc is null)
        {
            return false;
        }

        return _processInfoProvider.TryGetStartTimeUtc(_unityProcessId, out var currentStartTimeUtc)
            && currentStartTimeUtc == _expectedUnityProcessStartTimeUtc.Value;
    }
}

public interface IProcessInfoProvider
{
    bool TryGetStartTimeUtc(int processId, out DateTimeOffset startTimeUtc);
}

public sealed class ProcessInfoProvider : IProcessInfoProvider
{
    public bool TryGetStartTimeUtc(int processId, out DateTimeOffset startTimeUtc)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                startTimeUtc = default;
                return false;
            }

            startTimeUtc = new DateTimeOffset(process.StartTime.ToUniversalTime());
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            startTimeUtc = default;
            return false;
        }
    }
}