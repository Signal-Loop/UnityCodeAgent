using System;
using System.Collections.Generic;
using System.IO;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Logging;
using UnityEditor;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Settings
{
    public enum UnityCodeAgentTelemetryMode
    {
        None = 0,
        OtlpEndpoint = 1,
        File = 2,
    }

    public enum UnityCodeAgentSkillInstallTarget
    {
        GitHub = 0,
        Claude = 1,
        Agents = 2,
        Custom = 3,
    }

    public enum UnityCodeAgentProviderType
    {
        Copilot = 0,
        Byok = 1,
    }

    public sealed class UnityCodeAgentSettings : ScriptableObject
    {
        private const string ByokApiKeyEditorPrefsKey = "SignalLoop.UnityCodeAgent.Settings.ByokApiKey";

        private static UnityCodeAgentSettings _instance;

        [Tooltip("When enabled, the service binds to an ephemeral loopback port and publishes the chosen port through the endpoint manifest.")]
        public bool UseDynamicServicePort = true;

        [Tooltip("Fixed loopback port to use when dynamic service port selection is disabled.")]
        public int ServicePort = 5007;

        [Tooltip("Timeout in seconds before the service shuts down after the Unity parent process disappears.")]
        public int ServiceOrphanTimeoutSeconds = 90;

        [Tooltip("Minimum log level. Messages below this level are suppressed.")]
        public UnityCodeAgentLogger.LogLevel MinLogLevel = UnityCodeAgentLogger.LogLevel.Info;

        [Tooltip("Enable logging to a file under .unityCodeAgent/client/logs/unity.log.")]
        public bool LogToFile;

        [Tooltip("Show event json source in chat bubble.")]
        public bool ShowEventsSourceInChat;

        [Tooltip("Show all events in chat.")]
        public bool ShowAllEventsInChat;

        [Tooltip("Select how telemetry is exported by the Unity-started service.")]
        public UnityCodeAgentTelemetryMode TelemetryMode = UnityCodeAgentTelemetryMode.File;

        [Tooltip("OTLP endpoint used when Telemetry Mode is set to OTLP Endpoint, for example http://127.0.0.1:4318.")]
        public string OtlpEndpoint = string.Empty;

        [Tooltip("SDK/CLI telemetry file path used when Telemetry Mode is set to File. Leave empty to use .unityCodeAgent/service/logs/telemetry.jsonl.")]
        public string TelemetryFilePath = string.Empty;

        [Tooltip("When enabled, telemetry exporters may include prompt and response content. Ignored when Telemetry Mode is None.")]
        public bool TelemetryCaptureContent = true;

        [Tooltip("When enabled, the agent service returns predefined mock responses instead of connecting to the real Copilot service.")]
        public bool MockAgentService;

        public static readonly string[] DefaultToolAssemblyNames =
        {
            "System.Core",
            "UnityEngine.CoreModule",
            "UnityEditor.CoreModule",
            "Assembly-CSharp",
            "Assembly-CSharp-Editor"
        };

        [Tooltip("Additional assemblies to load for C# script execution beyond the default assemblies.")]
        public List<string> AdditionalToolAssemblyNames = new List<string>();

        [Tooltip("AssetDatabase path to the InputActionAsset used by play_unity_game. Leave empty to auto-detect.")]
        public string InputActionsAssetPath = string.Empty;

        [Tooltip("Provider used for model listing and chat sessions.")]
        public UnityCodeAgentProviderType ProviderType = UnityCodeAgentProviderType.Copilot;

        [Tooltip("Full HTTPS base URL for the BYOK provider.")]
        public string ByokBaseUrl = string.Empty;

        [Tooltip("Project-relative skill folders and disabled skill names.")]
        public UnityCodeAgentSkillsSettings Skills = new UnityCodeAgentSkillsSettings();

        [HideInInspector]
        public UnityCodeAgentSkillInstallTarget SkillsInstallTarget = UnityCodeAgentSkillInstallTarget.Agents;

        [Tooltip("Target directory for bundled skill file installation.")]
        public string SkillsTargetPath = UnityCodeAgentPaths.DefaultSkillsFolder;

        [SerializeField, HideInInspector]
        private bool _hasInitializedSkillsInstallTarget;

        public string ByokApiKey
        {
            get => EditorPrefs.GetString(ByokApiKeyEditorPrefsKey, string.Empty);
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    EditorPrefs.DeleteKey(ByokApiKeyEditorPrefsKey);
                    return;
                }

                EditorPrefs.SetString(ByokApiKeyEditorPrefsKey, value);
            }
        }

        [Tooltip("Model used when creating or opening sessions.")]
        public ModelInfoDto Model = null;

        [HideInInspector]
        public List<ModelInfoDto> AvailableModels = new List<ModelInfoDto>();

        [HideInInspector]
        public string AvailableModelsBaseUrl = string.Empty;

        public static UnityCodeAgentSettings Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                _instance = AssetDatabase.LoadAssetAtPath<UnityCodeAgentSettings>(UnityCodeAgentPackagePaths.SettingsAssetPath);
                if (_instance != null)
                {
                    _instance.EnsureDefaults();
                    return _instance;
                }

                _instance = CreateInstance<UnityCodeAgentSettings>();
                _instance.EnsureDefaults();
                SaveInstance(_instance);
                return _instance;
            }
        }

        private void OnEnable()
            => EnsureDefaults();

        public bool TryCreateProviderConfig(out ProviderConfigDto provider, out string validationMessage)
        {
            provider = null;
            validationMessage = string.Empty;

            if (ProviderType != UnityCodeAgentProviderType.Byok)
            {
                provider = ProviderConfigDto.Create(
                    HasValidSelectedModel() ? Model : null,
                    null,
                    null,
                    null,
                    null);
                return true;
            }

            var trimmedBaseUrl = (ByokBaseUrl ?? string.Empty).Trim();
            if (!IsValidHttpsUrl(trimmedBaseUrl))
            {
                validationMessage = "BaseUrl must be a full HTTPS URL.";
                return false;
            }

            provider = ProviderConfigDto.Create(
                HasValidSelectedModel() ? Model : null,
                null,
                trimmedBaseUrl,
                ByokApiKey,
                null);
            return true;
        }

        public string GetCurrentBaseUrlKey()
            => ProviderType == UnityCodeAgentProviderType.Byok
                ? ProviderConfigDto.NormalizeBaseUrl(ByokBaseUrl) ?? string.Empty
                : string.Empty;

        public bool HasValidSelectedModel()
        {
            if (Model == null || string.IsNullOrWhiteSpace(Model.Id))
            {
                return false;
            }

            if (!string.Equals(AvailableModelsBaseUrl ?? string.Empty, GetCurrentBaseUrlKey(), StringComparison.Ordinal))
            {
                return false;
            }

            return FindAvailableModel(Model) != null;
        }

        public void SetAvailableModels(IReadOnlyList<ModelInfoDto> models)
        {
            AvailableModels ??= new List<ModelInfoDto>();
            AvailableModels.Clear();
            AvailableModelsBaseUrl = GetCurrentBaseUrlKey();
            Model = null;

            if (models != null)
            {
                for (var index = 0; index < models.Count; index++)
                {
                    if (models[index] != null)
                    {
                        AvailableModels.Add(models[index]);
                    }
                }
            }

            AvailableModels.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }

        public bool SelectModel(ModelInfoDto model)
        {
            var availableModel = FindAvailableModel(model);
            if (availableModel == null || !string.Equals(AvailableModelsBaseUrl ?? string.Empty, GetCurrentBaseUrlKey(), StringComparison.Ordinal))
            {
                return false;
            }

            Model = availableModel;
            return true;
        }

        public void ClearAvailableModelsAndSelection()
        {
            AvailableModels ??= new List<ModelInfoDto>();
            AvailableModels.Clear();
            Model = null;
            AvailableModelsBaseUrl = string.Empty;
        }

        public IReadOnlyList<string> GetEnabledSkillDirectories()
        {
            EnsureDefaults();
            return Skills.GetEnabledSkillDirectories();
        }

        public IReadOnlyList<string> GetDisabledSkillNames()
        {
            EnsureDefaults();
            return Skills.GetDisabledSkillNames();
        }

        public bool IsSkillEnabled(string skillName)
        {
            EnsureDefaults();
            return Skills.IsSkillEnabled(skillName);
        }

        public void SetSkillEnabled(string skillName, bool enabled)
        {
            EnsureDefaults();
            Skills.SetSkillEnabled(skillName, enabled);
        }

        public string[] GetToolAssemblyNames()
        {
            if (AdditionalToolAssemblyNames == null || AdditionalToolAssemblyNames.Count == 0)
            {
                return DefaultToolAssemblyNames;
            }

            var allAssemblies = new List<string>(DefaultToolAssemblyNames);
            foreach (var assemblyName in AdditionalToolAssemblyNames)
            {
                if (!string.IsNullOrWhiteSpace(assemblyName) && !allAssemblies.Contains(assemblyName))
                {
                    allAssemblies.Add(assemblyName);
                }
            }

            return allAssemblies.ToArray();
        }

        public bool AddToolAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                return false;
            }

            assemblyName = assemblyName.Trim();
            if (Array.IndexOf(DefaultToolAssemblyNames, assemblyName) >= 0)
            {
                return false;
            }

            AdditionalToolAssemblyNames ??= new List<string>();
            if (AdditionalToolAssemblyNames.Contains(assemblyName))
            {
                return false;
            }

            AdditionalToolAssemblyNames.Add(assemblyName);
            EditorUtility.SetDirty(this);
            return true;
        }

        public bool RemoveToolAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName) || AdditionalToolAssemblyNames == null)
            {
                return false;
            }

            var removed = AdditionalToolAssemblyNames.Remove(assemblyName);
            if (removed)
            {
                EditorUtility.SetDirty(this);
            }

            return removed;
        }

        public void InitializeSkillsTarget()
        {
            Skills ??= new UnityCodeAgentSkillsSettings();
            Skills.EnsureDefaults();

            if (_hasInitializedSkillsInstallTarget)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(SkillsTargetPath))
            {
                SkillsTargetPath = NormalizePath(SkillsTargetPath);

                if (Path.IsPathRooted(SkillsTargetPath))
                {
                    SkillsInstallTarget = UnityCodeAgentSkillInstallTarget.Custom;
                }
                else if (SkillsInstallTarget != UnityCodeAgentSkillInstallTarget.Custom)
                {
                    SkillsTargetPath = GetStoredSkillsTargetPath(SkillsInstallTarget);
                }
            }
            else if (SkillsInstallTarget == UnityCodeAgentSkillInstallTarget.Custom)
            {
                SkillsTargetPath = GetDefaultCustomSkillsTargetPath();
            }
            else
            {
                SkillsTargetPath = GetStoredSkillsTargetPath(SkillsInstallTarget);
            }

            _hasInitializedSkillsInstallTarget = true;
            AddEffectiveSkillsTargetToSkillFolders();
            EditorUtility.SetDirty(this);
        }

        public void SetSkillsInstallTarget(UnityCodeAgentSkillInstallTarget target)
        {
            InitializeSkillsTarget();

            var currentCustomPath = SkillsInstallTarget == UnityCodeAgentSkillInstallTarget.Custom
                ? NormalizePath(SkillsTargetPath)
                : string.Empty;
            var currentResolvedPath = GetEffectiveSkillsTargetPath();

            SkillsInstallTarget = target;
            SkillsTargetPath = target == UnityCodeAgentSkillInstallTarget.Custom
                ? string.IsNullOrWhiteSpace(currentCustomPath) ? NormalizePath(currentResolvedPath) : currentCustomPath
                : GetStoredSkillsTargetPath(target);

            _hasInitializedSkillsInstallTarget = true;
            AddEffectiveSkillsTargetToSkillFolders();
            EditorUtility.SetDirty(this);
        }

        public void SetCustomSkillsTargetPath(string path)
        {
            SkillsInstallTarget = UnityCodeAgentSkillInstallTarget.Custom;
            SkillsTargetPath = string.IsNullOrWhiteSpace(path)
                ? GetDefaultCustomSkillsTargetPath()
                : NormalizePath(path);
            _hasInitializedSkillsInstallTarget = true;
            AddEffectiveSkillsTargetToSkillFolders();
            EditorUtility.SetDirty(this);
        }

        public string GetEffectiveSkillsTargetPath()
        {
            InitializeSkillsTarget();

            return SkillsInstallTarget == UnityCodeAgentSkillInstallTarget.Custom
                ? ResolveCustomSkillsTargetPath(SkillsTargetPath)
                : ResolveSkillsTargetPath(SkillsInstallTarget);
        }

        public static string ResolveSkillsTargetPath(UnityCodeAgentSkillInstallTarget target)
        {
            switch (target)
            {
                case UnityCodeAgentSkillInstallTarget.GitHub:
                    return NormalizeDirectoryPath(Path.GetFullPath(".github/skills"));
                case UnityCodeAgentSkillInstallTarget.Claude:
                    return NormalizeDirectoryPath(Path.GetFullPath(".claude/skills"));
                case UnityCodeAgentSkillInstallTarget.Agents:
                    return NormalizeDirectoryPath(Path.GetFullPath(UnityCodeAgentPaths.DefaultSkillsFolder));
                case UnityCodeAgentSkillInstallTarget.Custom:
                default:
                    return GetDefaultCustomSkillsTargetPath();
            }
        }

        public string GetEffectiveSkillsTargetProjectRelativePath()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return UnityCodeAgentPaths.ToProjectRelativePath(projectRoot, GetEffectiveSkillsTargetPath());
        }

        public void SetInputActionsAssetPath(string path)
        {
            InputActionsAssetPath = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace("\\", "/");
            EditorUtility.SetDirty(this);
        }

        public static bool IsValidHttpsUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
                && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        public static void SaveInstance(UnityCodeAgentSettings instance)
        {
            var settingsAssetPath = UnityCodeAgentPackagePaths.SettingsAssetPath;
            if (instance == null || string.IsNullOrWhiteSpace(settingsAssetPath) || AssetDatabase.AssetPathExists(settingsAssetPath))
            {
                return;
            }

            var directoryPath = Path.GetDirectoryName(settingsAssetPath);
            if (!string.IsNullOrWhiteSpace(directoryPath) && !AssetDatabase.IsValidFolder(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
                AssetDatabase.Refresh();
            }

            AssetDatabase.CreateAsset(instance, settingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(settingsAssetPath, ImportAssetOptions.ForceUpdate);
        }

        public void EnsureDefaults()
        {
            Skills ??= new UnityCodeAgentSkillsSettings();
            Skills.EnsureDefaults();
            InitializeSkillsTarget();
        }

        public static UnityContext GetUnityContext()
        {
            return CreateUnityContext();
        }

        public static UnityContext CreateUnityContext()
        {
            var settings = Instance;
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.EnsureDefaults();
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var paths = new UnityCodeAgentPaths(projectRoot);
            if (!settings.TryCreateProviderConfig(out var provider, out string validationMessage))
            {
                provider = ProviderConfigDto.Empty;
            }
            else if (!settings.HasValidSelectedModel())
            {
                provider = ProviderConfigDto.Empty;
                validationMessage = "Select a model in Unity Code Agent settings then retry.";
            }

            return new UnityContext(
                paths,
                provider,
                validationMessage,
                settings.MockAgentService,
                settings.ShowEventsSourceInChat,
                settings.ShowAllEventsInChat,
                settings.UseDynamicServicePort,
                settings.ServicePort,
                settings.ServiceOrphanTimeoutSeconds,
                settings.MinLogLevel,
                settings.LogToFile,
                settings.TelemetryMode,
                settings.OtlpEndpoint,
                settings.TelemetryFilePath,
                settings.TelemetryCaptureContent,
                settings.GetEnabledSkillDirectories(),
                settings.GetDisabledSkillNames(),
                settings.GetToolAssemblyNames(),
                settings.AdditionalToolAssemblyNames,
                settings.InputActionsAssetPath,
                UnityCodeAgentPackagePaths.ResolveServiceRootFileSystemPath(),
                UnityCodeAgentPackagePaths.ResolveSkillsSourceFileSystemPath());
        }

        private void AddEffectiveSkillsTargetToSkillFolders()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var projectRelativePath = UnityCodeAgentPaths.ToProjectRelativePath(projectRoot, GetEffectiveSkillsTargetPath());
            if (!string.IsNullOrWhiteSpace(projectRelativePath))
            {
                Skills.AddFolder(projectRelativePath);
            }
        }

        private static string ResolveCustomSkillsTargetPath(string path)
        {
            var normalized = NormalizePath(path ?? string.Empty);
            return Path.IsPathRooted(normalized)
                ? NormalizeDirectoryPath(Path.GetFullPath(normalized))
                : NormalizeDirectoryPath(Path.GetFullPath(normalized));
        }

        private static string GetStoredSkillsTargetPath(UnityCodeAgentSkillInstallTarget target)
        {
            switch (target)
            {
                case UnityCodeAgentSkillInstallTarget.GitHub:
                    return ".github/skills";
                case UnityCodeAgentSkillInstallTarget.Claude:
                    return ".claude/skills";
                case UnityCodeAgentSkillInstallTarget.Agents:
                case UnityCodeAgentSkillInstallTarget.Custom:
                default:
                    return UnityCodeAgentPaths.DefaultSkillsFolder;
            }
        }

        private static string GetDefaultCustomSkillsTargetPath()
            => NormalizePath(Path.GetFullPath(UnityCodeAgentPaths.DefaultSkillsFolder));

        private static string NormalizeDirectoryPath(string path)
        {
            var normalized = NormalizePath(path);
            return normalized.EndsWith("/") ? normalized : normalized + "/";
        }

        private static string NormalizePath(string path)
            => (path ?? string.Empty).Replace("\\", "/");

        private ModelInfoDto FindAvailableModel(ModelInfoDto model)
        {
            if (model == null || AvailableModels == null)
            {
                return null;
            }

            for (var index = 0; index < AvailableModels.Count; index++)
            {
                var availableModel = AvailableModels[index];
                if (availableModel != null
                    && string.Equals(availableModel.Id, model.Id, StringComparison.Ordinal)
                    && string.Equals(availableModel.Name, model.Name, StringComparison.Ordinal))
                {
                    return availableModel;
                }
            }

            return null;
        }
    }
}
