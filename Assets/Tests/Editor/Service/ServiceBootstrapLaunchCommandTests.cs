using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Settings;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class ServiceBootstrapLaunchCommandTests
    {
        private string _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "UnityCodeAgentLaunchCommandTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
        }

        [TearDown]
        public void TearDown()
        {
            TryDeleteDirectory(_testRoot);
            _testRoot = null;
        }

        [Test]
        public void CreateLaunchCommand_UsesPackagedExecutableWhenPresent()
        {
            var serviceRoot = CreateServiceRoot();
            var executablePath = Path.Combine(serviceRoot, "UnityCodeCopilot.Service.exe");
            File.WriteAllText(executablePath, string.Empty);
            var context = CreateContext(
                projectRoot: CreateProjectRoot("Packaged Project"),
                serviceRoot: serviceRoot,
                telemetryMode: UnityCodeAgentTelemetryMode.None,
                logToFile: true,
                orphanTimeoutSeconds: 7,
                minLogLevel: UnityCodeAgentLogger.LogLevel.Debug);

            var command = ServiceBootstrap.CreateLaunchCommand(context);

            Assert.That(command.executablePath, Is.EqualTo(executablePath));
            Assert.That(command.workingDirectory, Is.EqualTo(serviceRoot));
            Assert.That(command.arguments, Does.Contain($"--ProjectRoot=\"{context.Paths.ProjectRoot}\""));
            Assert.That(command.arguments, Does.Contain($"--UnityProcessId={Process.GetCurrentProcess().Id}"));
            Assert.That(command.arguments, Does.Contain("--OrphanTimeoutSeconds=7"));
            Assert.That(command.arguments, Does.Contain("--MinLogLevel=1"));
            Assert.That(command.arguments, Does.Contain("--LogToFile=True"));
            Assert.That(command.arguments, Does.Contain("--EnableTelemetry=false"));
            Assert.That(command.arguments, Does.Contain("--urls http://127.0.0.1:0"));
        }

        [Test]
        public void CreateLaunchCommand_FallsBackToDotnetRunProjectAndFixedPort()
        {
            var serviceRoot = CreateServiceRoot();
            var projectPath = Path.Combine(serviceRoot, "UnityCodeCopilot.Service.csproj");
            File.WriteAllText(projectPath, "<Project />");
            var context = CreateContext(
                projectRoot: CreateProjectRoot("Dotnet Project"),
                serviceRoot: serviceRoot,
                useDynamicServicePort: false,
                servicePort: 6123,
                telemetryMode: UnityCodeAgentTelemetryMode.None);

            var command = ServiceBootstrap.CreateLaunchCommand(context);

            Assert.That(command.executablePath, Is.EqualTo("dotnet"));
            Assert.That(command.workingDirectory, Is.EqualTo(serviceRoot));
            Assert.That(command.arguments, Does.StartWith($"run --project \"{projectPath}\" --no-launch-profile -- "));
            Assert.That(command.arguments, Does.Contain("--urls http://127.0.0.1:6123"));
            Assert.That(command.arguments, Does.Contain("--EnableTelemetry=false"));
        }

        [Test]
        public void CreateLaunchCommand_FileTelemetryIncludesContentAndCliPath()
        {
            var serviceRoot = CreateServiceRootWithProject();
            var telemetryPath = NormalizePath(Path.Combine(CreateProjectRoot("Telemetry Project"), ".unityCodeAgent", "service", "logs", "telemetry.jsonl"));
            var context = CreateContext(
                projectRoot: CreateProjectRoot("Telemetry Project"),
                serviceRoot: serviceRoot,
                telemetryMode: UnityCodeAgentTelemetryMode.File,
                cliTelemetryFilePath: telemetryPath,
                telemetryCaptureContent: false);

            var command = ServiceBootstrap.CreateLaunchCommand(context);

            Assert.That(command.arguments, Does.Contain("--EnableTelemetry=true"));
            Assert.That(command.arguments, Does.Contain("--TelemetryCaptureContent=False"));
            Assert.That(command.arguments, Does.Contain($"--CliTelemetryFilePath=\"{telemetryPath}\""));
            Assert.That(command.arguments, Does.Not.Contain("--OtlpEndpoint"));
        }

        [Test]
        public void CreateLaunchCommand_OtlpTelemetryIncludesEndpoint()
        {
            var serviceRoot = CreateServiceRootWithProject();
            var context = CreateContext(
                projectRoot: CreateProjectRoot("Otlp Project"),
                serviceRoot: serviceRoot,
                telemetryMode: UnityCodeAgentTelemetryMode.OtlpEndpoint,
                otlpEndpoint: "http://127.0.0.1:4318",
                telemetryCaptureContent: true);

            var command = ServiceBootstrap.CreateLaunchCommand(context);

            Assert.That(command.arguments, Does.Contain("--EnableTelemetry=true"));
            Assert.That(command.arguments, Does.Contain("--OtlpEndpoint=\"http://127.0.0.1:4318\""));
            Assert.That(command.arguments, Does.Contain("--TelemetryCaptureContent=True"));
            Assert.That(command.arguments, Does.Not.Contain("--CliTelemetryFilePath"));
        }

        [Test]
        public void CreateLaunchCommand_OtlpTelemetryWithoutEndpointThrows()
        {
            var context = CreateContext(
                projectRoot: CreateProjectRoot("Invalid Otlp Project"),
                serviceRoot: CreateServiceRootWithProject(),
                telemetryMode: UnityCodeAgentTelemetryMode.OtlpEndpoint,
                otlpEndpoint: string.Empty);

            var exception = Assert.Throws<InvalidOperationException>(() => ServiceBootstrap.CreateLaunchCommand(context));
            Assert.That(exception.Message, Does.Contain("OtlpEndpoint is empty"));
        }

        [Test]
        public void CreateLaunchCommand_MissingExecutableAndProjectThrows()
        {
            var serviceRoot = CreateServiceRoot();
            var context = CreateContext(
                projectRoot: CreateProjectRoot("Missing Service Project"),
                serviceRoot: serviceRoot);

            var exception = Assert.Throws<FileNotFoundException>(() => ServiceBootstrap.CreateLaunchCommand(context));
            Assert.That(exception.Message, Does.Contain(serviceRoot));
        }

        private string CreateServiceRootWithProject()
        {
            var serviceRoot = CreateServiceRoot();
            File.WriteAllText(Path.Combine(serviceRoot, "UnityCodeCopilot.Service.csproj"), "<Project />");
            return serviceRoot;
        }

        private string CreateServiceRoot()
        {
            var root = Path.Combine(_testRoot, "service-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return NormalizePath(root);
        }

        private string CreateProjectRoot(string name)
        {
            var root = Path.Combine(_testRoot, "project-" + Guid.NewGuid().ToString("N"), name);
            Directory.CreateDirectory(root);
            return NormalizePath(root);
        }

        private static UnityContext CreateContext(
            string projectRoot,
            string serviceRoot,
            bool useDynamicServicePort = true,
            int servicePort = 5007,
            int orphanTimeoutSeconds = 90,
            UnityCodeAgentLogger.LogLevel minLogLevel = UnityCodeAgentLogger.LogLevel.Info,
            bool logToFile = false,
            UnityCodeAgentTelemetryMode telemetryMode = UnityCodeAgentTelemetryMode.None,
            string otlpEndpoint = "",
            string cliTelemetryFilePath = "",
            bool telemetryCaptureContent = false)
            => new UnityContext(
                new UnityCodeAgentPaths(projectRoot),
                ProviderConfigDto.Empty,
                string.Empty,
                false,
                false,
                false,
                useDynamicServicePort,
                servicePort,
                orphanTimeoutSeconds,
                minLogLevel,
                logToFile,
                telemetryMode,
                otlpEndpoint,
                cliTelemetryFilePath,
                telemetryCaptureContent,
                Array.Empty<string>(),
                Array.Empty<string>(),
                UnityCodeAgentSettings.DefaultToolAssemblyNames,
                Array.Empty<string>(),
                string.Empty,
                serviceRoot,
                string.Empty);

        private static string NormalizePath(string path)
            => (path ?? string.Empty).Replace("\\", "/");

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
