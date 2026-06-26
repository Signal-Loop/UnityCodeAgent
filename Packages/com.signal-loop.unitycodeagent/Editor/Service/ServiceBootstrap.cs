using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Settings;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class ServiceBootstrap : IServiceBootstrap
    {
        private static readonly TimeSpan DefaultManifestTimeout = TimeSpan.FromSeconds(60);
        private static readonly SemaphoreSlim ConnectOrStartGate = new SemaphoreSlim(1, 1);

        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();

        public EndpointManifest ConnectOrStart(UnityContext context)
            => ConnectOrStartAsync(context).GetAwaiter().GetResult();

        public async Task<EndpointManifest> ConnectOrStartAsync(UnityContext context)
        {
            await ConnectOrStartGate.WaitAsync().ConfigureAwait(false);
            try
            {
                return await ConnectOrStartCoreAsync(context).ConfigureAwait(false);
            }
            finally
            {
                ConnectOrStartGate.Release();
            }
        }

        private async Task<EndpointManifest> ConnectOrStartCoreAsync(UnityContext context)
        {
            var paths = context.Paths;
            var stopwatch = Stopwatch.StartNew();
            var unityProcessId = Process.GetCurrentProcess().Id;
            _log.Debug(nameof(ServiceBootstrap), $"ConnectOrStart projectRoot={paths.ProjectRoot} unityProcessId={unityProcessId}.");
            var existingManifest = await TryLoadExistingManifestAsync(paths, unityProcessId).ConfigureAwait(false);
            if (existingManifest != null)
            {
                _log.Info(nameof(ServiceBootstrap), $"Reusing manifest port={existingManifest.Port} serviceProcessId={existingManifest.ServiceProcessId} elapsedMs={stopwatch.ElapsedMilliseconds}.");
                return existingManifest;
            }

            TryDeleteManifest(paths.EndpointManifestPath);

            var launchCommand = CreateLaunchCommand(context);
            _log.Info(nameof(ServiceBootstrap), $"Starting service '{launchCommand.executablePath} {launchCommand.arguments}' workingDirectory={launchCommand.workingDirectory}.");
            var startedProcess = StartProcess(launchCommand.executablePath, launchCommand.arguments, launchCommand.workingDirectory);
            _log.Info(nameof(ServiceBootstrap), $"Service start requested pid={startedProcess.ProcessId} elapsedMs={stopwatch.ElapsedMilliseconds} timeoutMs={(int)DefaultManifestTimeout.TotalMilliseconds}.");
            var manifest = await WaitForManifestAsync(
                paths.EndpointManifestPath,
                paths.ProjectRoot,
                DefaultManifestTimeout,
                (publishedManifest, root) => IsPublishedManifest(publishedManifest, root, unityProcessId),
                startedProcess).ConfigureAwait(false);
            _log.Info(nameof(ServiceBootstrap), $"Manifest ready port={manifest.Port} serviceProcessId={manifest.ServiceProcessId} elapsedMs={stopwatch.ElapsedMilliseconds}.");
            return manifest;
        }

        private async Task<EndpointManifest> TryLoadExistingManifestAsync(UnityCodeAgentPaths paths, int unityProcessId)
        {
            var manifest = TryReadManifest(paths.EndpointManifestPath);
            if (!await IsReusableManifestAsync(manifest, paths.ProjectRoot, unityProcessId).ConfigureAwait(false))
            {
                _log.Debug(nameof(ServiceBootstrap), $"Existing manifest not reusable.\nManifest={JsonConvert.SerializeObject(manifest)}\nprojectRoot={paths.ProjectRoot} unityProcessId={unityProcessId}.");
                return null;
            }

            return manifest;
        }

        private async Task<bool> IsReusableManifestAsync(EndpointManifest manifest, string projectRoot, int unityProcessId)
        {
            if (!IsManifestForProject(manifest, projectRoot))
            {
                _log.Debug(nameof(ServiceBootstrap), $"Manifest rejected: project mismatch or missing fields.\nManifest={JsonConvert.SerializeObject(manifest)}\nprojectRoot={projectRoot}");
                return false;
            }

            var reusableManifest = manifest;

            if (reusableManifest.UnityProcessId != unityProcessId)
            {
                _log.Debug(nameof(ServiceBootstrap), $"Manifest rejected: unityProcessId={reusableManifest.UnityProcessId} currentProcessId={unityProcessId}.");
                return false;
            }

            if (!IsProcessAlive(reusableManifest.ServiceProcessId))
            {
                _log.Info(nameof(ServiceBootstrap), $"Manifest rejected: serviceProcessId={reusableManifest.ServiceProcessId} is not alive.");
                return false;
            }

            var healthy = await IsEndpointHealthyAsync(reusableManifest).ConfigureAwait(false);
            if (!healthy)
            {
                _log.Info(nameof(ServiceBootstrap), $"Manifest rejected: endpoint health probe failed for port={reusableManifest.Port}.");
            }

            return healthy;
        }

        private static bool IsPublishedManifest(EndpointManifest manifest, string projectRoot, int unityProcessId)
        {
            if (!IsManifestForProject(manifest, projectRoot))
            {
                return false;
            }

            return manifest!.UnityProcessId == unityProcessId;
        }

        private static bool IsManifestForProject(EndpointManifest manifest, string projectRoot)
        {
            if (manifest == null || manifest.Port <= 0 || manifest.ServiceProcessId <= 0 || string.IsNullOrWhiteSpace(manifest.ProjectRoot))
            {
                return false;
            }

            return string.Equals(NormalizeProjectRoot(manifest.ProjectRoot), projectRoot, StringComparison.OrdinalIgnoreCase);
        }

        public static (string executablePath, string arguments, string workingDirectory) CreateLaunchCommand(UnityContext context)
        {
            var serviceRoot = ResolveServiceRoot(context);
            var projectPath = Path.Combine(serviceRoot, "UnityCodeCopilot.Service.csproj");
            var serviceArguments = BuildServiceArguments(context);
            var executablePath = TryGetPackagedServiceExecutablePath(serviceRoot);

            if (executablePath != null)
            {
                return (executablePath, serviceArguments, serviceRoot);
            }

            if (File.Exists(projectPath))
            {
                return ("dotnet", $"run --project \"{projectPath}\" --no-launch-profile -- {serviceArguments}", serviceRoot);
            }

            throw new FileNotFoundException(
                $"Could not find a packaged UnityCodeCopilot service executable or a development project file in: {serviceRoot}",
                Path.Combine(serviceRoot, "UnityCodeCopilot.Service"));
        }

        private static string ResolveServiceRoot(UnityContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.PackageServiceRootPath))
            {
                return context.PackageServiceRootPath;
            }

            return UnityCodeAgentPackagePaths.ResolveProjectFileSystemPath(UnityCodeAgentPackagePaths.ServiceRootRelativePath);
        }

        private static string TryGetPackagedServiceExecutablePath(string serviceRoot)
        {
            var candidates = new[]
            {
                Path.Combine(serviceRoot, "UnityCodeCopilot.Service.exe"),
                Path.Combine(serviceRoot, "UnityCodeCopilot.Service"),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string BuildServiceArguments(UnityContext context)
        {
            var paths = context.Paths;
            var arguments =
                $"--ProjectRoot=\"{paths.ProjectRoot}\" " +
                $"--UnityProcessId={Process.GetCurrentProcess().Id} " +
                $"--OrphanTimeoutSeconds={Math.Max(1, context.ServiceOrphanTimeoutSeconds)} " +
                $"--MinLogLevel={(int)context.MinLogLevel} " +
                $"--LogToFile={context.LogToFile}";

            arguments += context.UseDynamicServicePort
                ? " --urls http://127.0.0.1:0"
                : $" --urls http://127.0.0.1:{Math.Max(1, context.ServicePort)}";

            arguments += BuildTelemetryArguments(context);
            return arguments;
        }

        private static string BuildTelemetryArguments(UnityContext context)
        {
            switch (context.TelemetryMode)
            {
                case UnityCodeAgentTelemetryMode.None:
                    return " --EnableTelemetry=false";

                case UnityCodeAgentTelemetryMode.OtlpEndpoint:
                    if (string.IsNullOrWhiteSpace(context.OtlpEndpoint))
                    {
                        throw new InvalidOperationException("Telemetry Mode is set to OTLP Endpoint, but OtlpEndpoint is empty.");
                    }

                    return
                        " --EnableTelemetry=true" +
                        $" --OtlpEndpoint=\"{context.OtlpEndpoint.Trim()}\"" +
                        $" --TelemetryCaptureContent={context.TelemetryCaptureContent}";

                case UnityCodeAgentTelemetryMode.File:
                default:
                    var fileArguments =
                        " --EnableTelemetry=true" +
                        $" --TelemetryCaptureContent={context.TelemetryCaptureContent}";

                    if (!string.IsNullOrWhiteSpace(context.CliTelemetryFilePath))
                    {
                        fileArguments += $" --CliTelemetryFilePath=\"{context.CliTelemetryFilePath.Trim()}\"";
                    }

                    return fileArguments;
            }
        }

        private static StartedServiceProcess StartProcess(string executablePath, string arguments, string workingDirectory)
        {
            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workingDirectory,
                },
            };

            process.OutputDataReceived += (_, args) => AppendCapturedOutputLine(standardOutput, args.Data);
            process.ErrorDataReceived += (_, args) => AppendCapturedOutputLine(standardError, args.Data);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return new StartedServiceProcess(process, standardOutput, standardError);
        }

        private static async Task<EndpointManifest> WaitForManifestAsync(
            string manifestPath,
            string projectRoot,
            TimeSpan timeout,
            Func<EndpointManifest, string, bool> manifestValidator,
            StartedServiceProcess startedProcess)
        {
            var timeoutAt = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < timeoutAt)
            {
                var manifest = TryReadManifest(manifestPath);
                if (manifestValidator(manifest, projectRoot))
                {
                    return manifest!;
                }

                ThrowIfProcessExited(startedProcess);

                await Task.Delay(200).ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                $"Service endpoint manifest was not written within {(int)timeout.TotalSeconds} seconds.{FormatCapturedOutput(startedProcess)}");
        }

        private static void ThrowIfProcessExited(StartedServiceProcess startedProcess)
        {
            if (startedProcess == null || !startedProcess.HasExited)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Service process exited before publishing its endpoint manifest. exitCode={startedProcess.ExitCode}.{FormatCapturedOutput(startedProcess)}");
        }

        private static string FormatCapturedOutput(StartedServiceProcess startedProcess)
        {
            if (startedProcess == null)
            {
                return string.Empty;
            }

            var output = startedProcess.GetCapturedOutput();
            return string.IsNullOrWhiteSpace(output)
                ? string.Empty
                : $" Captured output:{Environment.NewLine}{output}";
        }

        private static void AppendCapturedOutputLine(StringBuilder builder, string line)
        {
            if (builder == null || string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            lock (builder)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(line.Trim());

                const int maxCapturedCharacters = 12000;
                if (builder.Length > maxCapturedCharacters)
                {
                    builder.Remove(0, builder.Length - maxCapturedCharacters);
                }
            }
        }

        private sealed class StartedServiceProcess
        {
            private readonly StringBuilder _standardOutput;
            private readonly StringBuilder _standardError;

            public StartedServiceProcess(Process process, StringBuilder standardOutput, StringBuilder standardError)
            {
                Process = process;
                _standardOutput = standardOutput ?? new StringBuilder();
                _standardError = standardError ?? new StringBuilder();
            }

            public Process Process { get; }

            public int ProcessId => Process == null ? 0 : Process.Id;

            public bool HasExited
            {
                get
                {
                    try
                    {
                        return Process == null || Process.HasExited;
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                }
            }

            public int ExitCode
            {
                get
                {
                    try
                    {
                        return Process == null ? 0 : Process.ExitCode;
                    }
                    catch (InvalidOperationException)
                    {
                        return 0;
                    }
                }
            }

            public string GetCapturedOutput()
            {
                var output = ReadBuilder(_standardOutput);
                var error = ReadBuilder(_standardError);
                if (string.IsNullOrWhiteSpace(output))
                {
                    return error;
                }

                if (string.IsNullOrWhiteSpace(error))
                {
                    return output;
                }

                return $"stdout:{Environment.NewLine}{output}{Environment.NewLine}stderr:{Environment.NewLine}{error}";
            }

            private static string ReadBuilder(StringBuilder builder)
            {
                lock (builder)
                {
                    return builder.ToString().Trim();
                }
            }
        }

        private static EndpointManifest TryReadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<EndpointManifest>(File.ReadAllText(manifestPath));
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void TryDeleteManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                return;
            }

            try
            {
                File.Delete(manifestPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static bool IsProcessAlive(int serviceProcessId)
        {
            if (serviceProcessId <= 0)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById(serviceProcessId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static async Task<bool> IsEndpointHealthyAsync(EndpointManifest manifest)
        {
            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                using var response = await httpClient.GetAsync($"http://127.0.0.1:{manifest.Port}/health").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static string NormalizeProjectRoot(string projectRoot)
            => Path.GetFullPath(projectRoot).Replace('\\', '/').TrimEnd('/');
    }
}
