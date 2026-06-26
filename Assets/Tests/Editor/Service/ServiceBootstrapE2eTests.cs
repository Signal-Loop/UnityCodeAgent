using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Settings;
using UnityEngine.TestTools;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class ServiceBootstrapE2eTests
    {
        [UnityTest]
        [Description("Goal: verify real service bootstrap can resume from a background thread after Unity context creation. Scope: process bootstrap and endpoint manifest publication. Boundaries: excludes chat session creation.")]
        public IEnumerator ConnectOrStartAsync_FromBackgroundThread_PublishesManifest()
        {
            var testRoot = Path.GetFullPath(Path.Combine(
                "Temp",
                "UnityCodeAgent",
                nameof(ConnectOrStartAsync_FromBackgroundThread_PublishesManifest),
                Guid.NewGuid().ToString("N")));
            var projectRoot = Path.Combine(testRoot, "Project");
            var packageRoot = Path.Combine(testRoot, "Package");
            var serviceRoot = Path.Combine(packageRoot, "CopilotService~");
            var contractsRoot = Path.Combine(packageRoot, "Contracts");

            Directory.CreateDirectory(projectRoot);
            CopyDirectory(
                Path.GetFullPath(Path.Combine("Packages", "com.signal-loop.unitycodeagent", "Editor", "CopilotService~")),
                serviceRoot);
            CopyDirectory(
                Path.GetFullPath(Path.Combine("Packages", "com.signal-loop.unitycodeagent", "Editor", "Contracts")),
                contractsRoot);

            var context = CreateContext(projectRoot, serviceRoot);
            var service = new AgentService();
            EndpointManifest manifest = null;

            try
            {
                var task = Task.Run(() => service.StartAsync(context));
                var deadline = DateTimeOffset.UtcNow.AddSeconds(60);

                while (!task.IsCompleted && DateTimeOffset.UtcNow < deadline)
                {
                    yield return null;
                }

                Assert.That(task.IsCompleted, Is.True, "Service bootstrap did not complete before timeout.");
                if (task.IsFaulted)
                {
                    throw task.Exception.InnerException ?? task.Exception;
                }

                manifest = task.Result;
                Assert.That(manifest, Is.Not.Null);
                Assert.That(manifest.Port, Is.GreaterThan(0));
                Assert.That(manifest.ProjectRoot, Is.EqualTo(new UnityCodeAgentPaths(projectRoot).ProjectRoot));
                Assert.That(File.Exists(context.Paths.EndpointManifestPath), Is.True);
            }
            finally
            {
                if (manifest != null)
                {
                    service.Stop(context);
                }

                TryDeleteDirectory(testRoot);
            }
        }

        private static UnityContext CreateContext(string projectRoot, string serviceRoot)
            => new UnityContext(
                new UnityCodeAgentPaths(projectRoot),
                ProviderConfigDto.Empty,
                string.Empty,
                false,
                false,
                false,
                true,
                5007,
                5,
                UnityCodeAgentLogger.LogLevel.Info,
                false,
                UnityCodeAgentTelemetryMode.None,
                string.Empty,
                string.Empty,
                false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                UnityCodeAgentSettings.DefaultToolAssemblyNames,
                Array.Empty<string>(),
                string.Empty,
                NormalizePath(Path.GetFullPath(serviceRoot)),
                NormalizePath(Path.GetFullPath(Path.Combine("Packages", "com.signal-loop.unitycodeagent", "Editor", "Skills~"))));

        private static string NormalizePath(string path)
            => (path ?? string.Empty).Replace("\\", "/");

        private static void CopyDirectory(string sourcePath, string destinationPath)
        {
            Directory.CreateDirectory(destinationPath);

            foreach (var sourceFilePath in Directory.GetFiles(sourcePath))
            {
                var fileName = Path.GetFileName(sourceFilePath);
                File.Copy(sourceFilePath, Path.Combine(destinationPath, fileName), true);
            }

            foreach (var sourceDirectoryPath in Directory.GetDirectories(sourcePath))
            {
                var directoryName = Path.GetFileName(sourceDirectoryPath);
                if (string.Equals(directoryName, "bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(directoryName, "obj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CopyDirectory(sourceDirectoryPath, Path.Combine(destinationPath, directoryName));
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
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
    }
}
