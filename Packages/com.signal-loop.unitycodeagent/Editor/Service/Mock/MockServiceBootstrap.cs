using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Settings;

namespace SignalLoop.UnityCodeAgent.Service.Mock
{
    public sealed class MockServiceBootstrap : IServiceBootstrap
    {
        private static readonly SemaphoreSlim ManifestWriteGate = new SemaphoreSlim(1, 1);
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();

        public async Task<EndpointManifest> ConnectOrStartAsync(UnityContext context)
        {
            var paths = context.Paths;
            var stopwatch = Stopwatch.StartNew();
            _log.Info(nameof(MockServiceBootstrap), $"ConnectOrStartAsync begin (mock) projectRoot={paths.ProjectRoot}");

            // Simulate startup delay to exercise the real WaitForManifestAsync path
            await Task.Delay(200).ConfigureAwait(false);

            var manifest = new EndpointManifest
            {
                Version = 1,
                ProjectRoot = paths.ProjectRoot,
                ProjectId = "mock-project",
                UnityProcessId = Process.GetCurrentProcess().Id,
                ServiceProcessId = -1, // mock sentinel — Stop() will no-op
                Port = 0,
                StartedAtUtc = DateTimeOffset.UtcNow,
                StreamGenerationId = MockServiceRuntime.StreamGenerationId,
            };

            var manifestDir = Path.GetDirectoryName(paths.EndpointManifestPath);
            if (!string.IsNullOrEmpty(manifestDir) && !Directory.Exists(manifestDir))
            {
                Directory.CreateDirectory(manifestDir);
            }

            await ManifestWriteGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
                File.WriteAllText(paths.EndpointManifestPath, json);
            }
            finally
            {
                ManifestWriteGate.Release();
            }

            _log.Info(nameof(MockServiceBootstrap), $"ConnectOrStartAsync completed (mock) elapsedMs={stopwatch.ElapsedMilliseconds} manifestPath={paths.EndpointManifestPath}");
            return manifest;
        }
    }
}
