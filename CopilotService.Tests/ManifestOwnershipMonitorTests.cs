using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Options;
using UnityCodeCopilot.Service.Settings;

namespace UnityCodeCopilot.Service.Tests;

public sealed class ManifestOwnershipMonitorTests
{
    private string? _testRoot;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "ManifestOwnershipMonitorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [TearDown]
    public void TearDown()
    {
        TryDeleteDirectory(_testRoot);
        _testRoot = null;
    }

    [Test]
    public async Task CheckOwnership_KeepsRunning_WhenManifestNamesCurrentProcess()
    {
        var paths = CreatePaths(_testRoot!);
        var store = new EndpointManifestStore(paths);
        await store.WriteAsync(41000, unityProcessId: 10, serviceProcessId: 200, CancellationToken.None);
        var lifetime = new RecordingHostApplicationLifetime();
        var monitor = CreateMonitor(paths, store, lifetime, currentProcessId: 200);

        var keepRunning = monitor.CheckOwnership();

        Assert.That(keepRunning, Is.True);
        Assert.That(lifetime.StopRequested, Is.False);
    }

    [Test]
    public void CheckOwnership_StopsApplication_WhenSameProjectManifestNamesDifferentProcess()
    {
        var paths = CreatePaths(_testRoot!);
        var store = new EndpointManifestStore(paths);
        WriteManifest(paths, paths.ProjectRoot, EndpointManifestStore.CreateProjectId(paths.ProjectRoot), serviceProcessId: 201);
        var lifetime = new RecordingHostApplicationLifetime();
        var monitor = CreateMonitor(paths, store, lifetime, currentProcessId: 200);

        var keepRunning = monitor.CheckOwnership();

        Assert.That(keepRunning, Is.False);
        Assert.That(lifetime.StopRequested, Is.True);
    }

    [Test]
    public void CheckOwnership_KeepsRunning_WhenManifestIsMissingBeforeOwnershipIsEstablished()
    {
        var paths = CreatePaths(_testRoot!);
        var store = new EndpointManifestStore(paths);
        var lifetime = new RecordingHostApplicationLifetime();
        var monitor = CreateMonitor(paths, store, lifetime, currentProcessId: 200);

        var keepRunning = monitor.CheckOwnership();

        Assert.That(keepRunning, Is.True);
        Assert.That(lifetime.StopRequested, Is.False);
    }

    [Test]
    public async Task CheckOwnership_StopsApplication_WhenOwnedManifestIsRemoved()
    {
        var paths = CreatePaths(_testRoot!);
        var store = new EndpointManifestStore(paths);
        await store.WriteAsync(41000, unityProcessId: 10, serviceProcessId: 200, CancellationToken.None);
        var lifetime = new RecordingHostApplicationLifetime();
        var monitor = CreateMonitor(paths, store, lifetime, currentProcessId: 200);
        Assert.That(monitor.CheckOwnership(), Is.True);

        File.Delete(paths.EndpointManifestPath);
        var keepRunning = monitor.CheckOwnership();

        Assert.That(keepRunning, Is.False);
        Assert.That(lifetime.StopRequested, Is.True);
    }

    [Test]
    public void CheckOwnership_KeepsRunning_WhenManifestIsMalformed()
    {
        var paths = CreatePaths(_testRoot!);
        Directory.CreateDirectory(paths.RuntimeRoot);
        File.WriteAllText(paths.EndpointManifestPath, "{ not-json");
        var store = new EndpointManifestStore(paths);
        var lifetime = new RecordingHostApplicationLifetime();
        var monitor = CreateMonitor(paths, store, lifetime, currentProcessId: 200);

        var keepRunning = monitor.CheckOwnership();

        Assert.That(keepRunning, Is.True);
        Assert.That(lifetime.StopRequested, Is.False);
    }

    [Test]
    public void CheckOwnership_KeepsRunning_WhenManifestBelongsToDifferentProject()
    {
        var paths = CreatePaths(_testRoot!);
        var otherRoot = Path.Combine(_testRoot!, "OtherProject");
        Directory.CreateDirectory(otherRoot);
        var otherPaths = CreatePaths(otherRoot);
        WriteManifest(paths, otherPaths.ProjectRoot, EndpointManifestStore.CreateProjectId(otherPaths.ProjectRoot), serviceProcessId: 201);
        var store = new EndpointManifestStore(paths);
        var lifetime = new RecordingHostApplicationLifetime();
        var monitor = CreateMonitor(paths, store, lifetime, currentProcessId: 200);

        var keepRunning = monitor.CheckOwnership();

        Assert.That(keepRunning, Is.True);
        Assert.That(lifetime.StopRequested, Is.False);
    }

    [Test]
    public async Task DeleteIfOwned_DoesNotDeleteManifestOwnedByAnotherProcess()
    {
        var paths = CreatePaths(_testRoot!);
        var store = new EndpointManifestStore(paths);
        await store.WriteAsync(41000, unityProcessId: 10, serviceProcessId: 201, CancellationToken.None);

        store.DeleteIfOwned(200);

        Assert.That(File.Exists(paths.EndpointManifestPath), Is.True);
    }

    private static ManifestOwnershipMonitor CreateMonitor(
        ProjectPaths paths,
        EndpointManifestStore store,
        RecordingHostApplicationLifetime lifetime,
        int currentProcessId)
        => new(
            paths,
            store,
            CreateLogger(paths.ProjectRoot),
            lifetime,
            TimeSpan.FromMilliseconds(10),
            currentProcessId);

    private static ProjectPaths CreatePaths(string projectRoot)
        => new(projectRoot);

    private static UnityCodeCopilotServiceLogger CreateLogger(string projectRoot)
        => new(
            new ProjectPaths(projectRoot),
            new ServiceOptions
            {
                ProjectRoot = projectRoot,
                MinLogLevel = UnityCodeCopilotServiceLogger.LogLevel.Off,
                LogToFile = false,
            });

    private static void WriteManifest(ProjectPaths paths, string projectRoot, string projectId, int serviceProcessId)
    {
        Directory.CreateDirectory(paths.RuntimeRoot);
        File.WriteAllText(paths.EndpointManifestPath, $$"""
        {
          "version": 1,
          "projectRoot": "{{Escape(projectRoot)}}",
          "projectId": "{{Escape(projectId)}}",
          "unityProcessId": 10,
          "serviceProcessId": {{serviceProcessId}},
          "port": 41000,
          "startedAtUtc": "2026-06-23T00:00:00+00:00",
          "streamGenerationId": "test"
        }
        """);
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void TryDeleteDirectory(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class RecordingHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public bool StopRequested { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            StopRequested = true;
            _stopping.Cancel();
        }
    }
}
