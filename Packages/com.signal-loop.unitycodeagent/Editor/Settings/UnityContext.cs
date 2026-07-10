using System;
using System.Collections.Generic;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Logging;

namespace SignalLoop.UnityCodeAgent.Settings
{
    public sealed class UnityContext
    {
        public UnityContext(
            UnityCodeAgentPaths paths,
            ProviderConfigDto provider,
            string providerValidationMessage,
            bool mockAgentService,
            bool showEventsSourceInChat,
            bool showAllEventsInChat,
            bool useDynamicServicePort,
            int servicePort,
            int serviceOrphanTimeoutSeconds,
            UnityCodeAgentLogger.LogLevel minLogLevel,
            bool logToFile,
            UnityCodeAgentTelemetryMode telemetryMode,
            string otlpEndpoint,
            string telemetryFilePath,
            bool telemetryCaptureContent,
            IReadOnlyList<string> skillDirectories,
            IReadOnlyList<string> disabledSkills,
            IReadOnlyList<string> toolAssemblyNames,
            IReadOnlyList<string> additionalToolAssemblyNames,
            string inputActionsAssetPath,
            string packageServiceRootPath = null,
            string packageSkillsSourcePath = null)
        {
            Paths = paths ?? throw new ArgumentNullException(nameof(paths));
            Provider = provider ?? ProviderConfigDto.Empty;
            ProviderValidationMessage = providerValidationMessage ?? string.Empty;
            MockAgentService = mockAgentService;
            ShowEventsSourceInChat = showEventsSourceInChat;
            ShowAllEventsInChat = showAllEventsInChat;
            UseDynamicServicePort = useDynamicServicePort;
            ServicePort = servicePort;
            ServiceOrphanTimeoutSeconds = serviceOrphanTimeoutSeconds;
            MinLogLevel = minLogLevel;
            LogToFile = logToFile;
            TelemetryMode = telemetryMode;
            OtlpEndpoint = otlpEndpoint ?? string.Empty;
            TelemetryFilePath = telemetryFilePath ?? string.Empty;
            TelemetryCaptureContent = telemetryCaptureContent;
            SkillDirectories = Copy(skillDirectories);
            DisabledSkills = Copy(disabledSkills);
            ToolAssemblyNames = Copy(toolAssemblyNames);
            AdditionalToolAssemblyNames = Copy(additionalToolAssemblyNames);
            InputActionsAssetPath = inputActionsAssetPath ?? string.Empty;
            PackageServiceRootPath = packageServiceRootPath ?? string.Empty;
            PackageSkillsSourcePath = packageSkillsSourcePath ?? string.Empty;
        }

        public UnityCodeAgentPaths Paths { get; }
        public ProviderConfigDto Provider { get; }
        public string ProviderValidationMessage { get; }
        public bool IsProviderValid => string.IsNullOrWhiteSpace(ProviderValidationMessage);
        public bool MockAgentService { get; }
        public bool ShowEventsSourceInChat { get; }
        public bool ShowAllEventsInChat { get; }
        public bool UseDynamicServicePort { get; }
        public int ServicePort { get; }
        public int ServiceOrphanTimeoutSeconds { get; }
        public UnityCodeAgentLogger.LogLevel MinLogLevel { get; }
        public bool LogToFile { get; }
        public UnityCodeAgentTelemetryMode TelemetryMode { get; }
        public string OtlpEndpoint { get; }
        public string TelemetryFilePath { get; }
        public bool TelemetryCaptureContent { get; }
        public IReadOnlyList<string> SkillDirectories { get; }
        public IReadOnlyList<string> DisabledSkills { get; }
        public IReadOnlyList<string> ToolAssemblyNames { get; }
        public IReadOnlyList<string> AdditionalToolAssemblyNames { get; }
        public string InputActionsAssetPath { get; }
        public string PackageServiceRootPath { get; }
        public string PackageSkillsSourcePath { get; }

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> values)
            => values == null ? Array.Empty<string>() : new List<string>(values);
    }
}
