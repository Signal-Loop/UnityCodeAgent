using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Service;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Settings
{
    [CustomEditor(typeof(UnityCodeAgentSettings))]
    public sealed class UnityCodeAgentSettingsEditor : UnityEditor.Editor
    {
        private readonly AdvancedDropdownState _modelDropdownState = new AdvancedDropdownState();

        private SerializedProperty _useDynamicServicePort;
        private SerializedProperty _servicePort;
        private SerializedProperty _serviceOrphanTimeoutSeconds;
        private SerializedProperty _minLogLevel;
        private SerializedProperty _logToFile;
        private SerializedProperty _telemetryMode;
        private SerializedProperty _otlpEndpoint;
        private SerializedProperty _telemetryFilePath;
        private SerializedProperty _telemetryCaptureContent;
        private SerializedProperty _providerType;
        private SerializedProperty _byokBaseUrl;
        private SerializedProperty _showEventsSourceInChat;
        private SerializedProperty _showAllEventsInChat;
        private SerializedProperty _mockService;
        private string _modelRefreshMessage = string.Empty;
        private MessageType _modelRefreshMessageType = MessageType.Info;
        private ModelRefreshCoordinator _modelRefreshCoordinator;
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();

        private void OnEnable()
        {
            _useDynamicServicePort = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.UseDynamicServicePort));
            _servicePort = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.ServicePort));
            _serviceOrphanTimeoutSeconds = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.ServiceOrphanTimeoutSeconds));
            _minLogLevel = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.MinLogLevel));
            _logToFile = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.LogToFile));
            _telemetryMode = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.TelemetryMode));
            _otlpEndpoint = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.OtlpEndpoint));
            _telemetryFilePath = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.TelemetryFilePath));
            _telemetryCaptureContent = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.TelemetryCaptureContent));
            _providerType = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.ProviderType));
            _byokBaseUrl = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.ByokBaseUrl));
            _showEventsSourceInChat = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.ShowEventsSourceInChat));
            _showAllEventsInChat = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.ShowAllEventsInChat));
            _mockService = serializedObject.FindProperty(nameof(UnityCodeAgentSettings.MockAgentService));
        }

        private void OnDisable()
            => _modelRefreshCoordinator?.Dispose();

        public override void OnInspectorGUI()
        {
            var settings = (UnityCodeAgentSettings)target;

            serializedObject.Update();
            var previousBaseUrlKey = settings.GetCurrentBaseUrlKey();

            EditorGUILayout.LabelField("Provider", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_providerType);
            if ((UnityCodeAgentProviderType)_providerType.enumValueIndex == UnityCodeAgentProviderType.Byok)
            {
                EditorGUILayout.PropertyField(_byokBaseUrl, new GUIContent("BaseUrl"));
                if (!UnityCodeAgentSettings.IsValidHttpsUrl(_byokBaseUrl.stringValue))
                {
                    EditorGUILayout.HelpBox("BaseUrl must be a full HTTPS URL.", MessageType.Error);
                }

                var nextApiKey = EditorGUILayout.PasswordField(new GUIContent("ApiKey"), settings.ByokApiKey ?? string.Empty);
                if (!string.Equals(nextApiKey, settings.ByokApiKey, StringComparison.Ordinal))
                {
                    settings.ByokApiKey = nextApiKey;
                }

                EditorGUILayout.HelpBox("ApiKey is saved in EditorPrefs and is not written to the settings asset.", MessageType.Info);
            }
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Model", EditorStyles.boldLabel);
            DrawModelField(settings);
            var modelRefreshMessage = GetModelRefreshMessage(out var modelRefreshMessageType);
            if (!string.IsNullOrWhiteSpace(modelRefreshMessage))
            {
                EditorGUILayout.HelpBox(modelRefreshMessage, modelRefreshMessageType);
            }
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            UnityCodeAgentSkillsSettingsEditorDrawer.Draw(settings);
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            UnityCodeAgentSkillsInstallTargetDrawer.Draw(settings);
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);
            UnityCodeAgentInputActionsEditorDrawer.Draw(settings, serializedObject);
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            UnityCodeAgentToolAssembliesEditorDrawer.Draw(settings, serializedObject, Repaint);
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            UnityCodeAgentSkillsSettingsEditorDrawer.DrawMcp();
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Logging", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_minLogLevel);
            EditorGUILayout.PropertyField(_logToFile);
            EditorGUILayout.HelpBox("Unity logging changes apply immediately. The Agent Service uses these logging values the next time it starts.", MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_showEventsSourceInChat);
            EditorGUILayout.PropertyField(_showAllEventsInChat);
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox("Following settings affect the Agent Service process and apply after the service is restarted.", MessageType.Info);

            EditorGUILayout.LabelField("Agent Service", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_useDynamicServicePort);
            EditorGUILayout.PropertyField(_servicePort);
            EditorGUILayout.PropertyField(_serviceOrphanTimeoutSeconds);
            EditorGUILayout.PropertyField(_mockService);
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Telemetry", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_telemetryMode);
            EditorGUILayout.PropertyField(_otlpEndpoint);
            EditorGUILayout.PropertyField(_telemetryFilePath);
            EditorGUILayout.PropertyField(_telemetryCaptureContent);

            if (serializedObject.ApplyModifiedProperties())
            {
                var currentBaseUrlKey = settings.GetCurrentBaseUrlKey();
                if (!string.Equals(previousBaseUrlKey, currentBaseUrlKey, StringComparison.Ordinal))
                {
                    settings.ClearAvailableModelsAndSelection();
                    _modelRefreshMessage = "Provider changed. Refresh models, then select a model.";
                    _modelRefreshMessageType = MessageType.Info;
                }

                EditorUtility.SetDirty(settings);
            }
        }

        private void DrawModelField(UnityCodeAgentSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string modelLabel = settings.HasValidSelectedModel() ? settings.Model?.ToString() : string.Empty;
                if (string.IsNullOrWhiteSpace(modelLabel))
                {
                    modelLabel = "Select a model";
                }
                var popupRect = GUILayoutUtility.GetRect(new GUIContent(modelLabel), EditorStyles.popup, GUILayout.ExpandWidth(true));
                if (GUI.Button(popupRect, modelLabel, EditorStyles.popup))
                {
                    var dropdown = ShowModelDropdown(settings);
                    dropdown.Show(popupRect);
                }

                var isRefreshing = _modelRefreshCoordinator?.IsRefreshInProgress == true;
                using (new EditorGUI.DisabledScope(isRefreshing))
                {
                    if (GUILayout.Button(isRefreshing ? "Loading" : "Refresh", GUILayout.Width(72f)))
                    {
                        StartRefreshModels(settings);
                        _ = ShowModelDropdown(settings);
                        Repaint();
                    }
                }
            }
        }

        private ModelAdvancedDropdown ShowModelDropdown(UnityCodeAgentSettings settings)
        {
            var models = string.Equals(settings.AvailableModelsBaseUrl ?? string.Empty, settings.GetCurrentBaseUrlKey(), StringComparison.Ordinal)
                ? settings.AvailableModels
                : null;
            var dropdown = new ModelAdvancedDropdown(_modelDropdownState, models, model => SelectModel(settings, model));
            return dropdown;
        }

        private void SelectModel(UnityCodeAgentSettings settings, ModelInfoDto model)
        {
            if (!settings.SelectModel(model))
            {
                _modelRefreshMessage = "Refresh models, then select a model.";
                _modelRefreshMessageType = MessageType.Warning;
                Repaint();
                return;
            }

            _modelRefreshMessage = string.Empty;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            serializedObject.UpdateIfRequiredOrScript();
            Repaint();
        }

        private void StartRefreshModels(UnityCodeAgentSettings settings)
        {
            serializedObject.ApplyModifiedProperties();
            _log.Debug(nameof(UnityCodeAgentSettingsEditor), "StartRefreshModels begin");

            if (!settings.TryCreateProviderConfig(out var provider, out var validationMessage))
            {
                _modelRefreshMessage = validationMessage;
                _modelRefreshMessageType = MessageType.Error;
                _log.Warning(nameof(UnityCodeAgentSettingsEditor), $"StartRefreshModels validation failed message={validationMessage}");
                return;
            }

            if (_modelRefreshCoordinator?.IsRefreshInProgress == true)
            {
                _log.Debug(nameof(UnityCodeAgentSettingsEditor), "StartRefreshModels skipped because a refresh is already running");
                return;
            }

            _modelRefreshMessage = string.Empty;
            _modelRefreshCoordinator?.Dispose();

            var context = UnityCodeAgentSettings.GetUnityContext();
            _modelRefreshCoordinator = new ModelRefreshCoordinator(
                (progress, cancellationToken) =>
                {
                    var service = new AgentService(progress);
                    return service.GetModelsAsync(context, new ListAgentModelsRequestDto(provider), cancellationToken);
                },
                models =>
                {
                    settings.SetAvailableModels(models);
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                    serializedObject.UpdateIfRequiredOrScript();
                    _log.Debug(nameof(UnityCodeAgentSettingsEditor), $"StartRefreshModels completed count={models?.Count ?? 0}");
                },
                RunOnEditorThreadAsync,
                Repaint,
                exception => _log.Error(nameof(UnityCodeAgentSettingsEditor), "StartRefreshModels failed", exception));

            _modelRefreshCoordinator.StartRefresh();
        }

        private string GetModelRefreshMessage(out MessageType messageType)
        {
            if (_modelRefreshCoordinator != null && !string.IsNullOrWhiteSpace(_modelRefreshCoordinator.Message))
            {
                messageType = _modelRefreshCoordinator.MessageType;
                return _modelRefreshCoordinator.Message;
            }

            messageType = _modelRefreshMessageType;
            return _modelRefreshMessage;
        }

        private static async Task RunOnEditorThreadAsync(Action action, CancellationToken cancellationToken)
        {
            await UnityEditorThread.RunAsync(
                () =>
                {
                    action();
                    return true;
                },
                cancellationToken);
        }

        private sealed class ModelAdvancedDropdown : AdvancedDropdown
        {
            private readonly List<ModelInfoDto> _models;
            private readonly Action<ModelInfoDto> _onSelected;

            public ModelAdvancedDropdown(AdvancedDropdownState state, List<ModelInfoDto> models, Action<ModelInfoDto> onSelected)
                : base(state)
            {
                _models = models ?? new List<ModelInfoDto>();
                _onSelected = onSelected;
                minimumSize = new Vector2(360f, 320f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Models");

                if (_models.Count == 0)
                {
                    root.AddChild(new AdvancedDropdownItem("No models available"));
                    return root;
                }

                for (var index = 0; index < _models.Count; index++)
                {
                    var model = _models[index];
                    var label = model.ToString();
                    root.AddChild(new ModelAdvancedDropdownItem(label, model));
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is ModelAdvancedDropdownItem modelItem)
                {
                    _onSelected(modelItem.Model);
                }
            }
        }

        private sealed class ModelAdvancedDropdownItem : AdvancedDropdownItem
        {
            public ModelAdvancedDropdownItem(string name, ModelInfoDto model)
                : base(name)
            {
                Model = model;
            }

            public ModelInfoDto Model { get; }
        }

    }
}
