using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Service;
using SignalLoop.UnityCodeAgent.Settings;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SignalLoop.UnityCodeAgent.UI
{
    public sealed class ChatEditorWindow : EditorWindow
    {
        internal const string WindowTitle = "Unity Code Agent";
        internal const string ChatWindowAssetPath = "Editor/UI/ChatWindow.uxml";
        internal const string AssistantTemplateAssetPath = "Editor/UI/ChatMessageTemplateAssistant.uxml";
        internal const string DefaultTemplateAssetPath = "Editor/UI/ChatMessageTemplateDefault.uxml";
        internal const string ErrorTemplateAssetPath = "Editor/UI/ChatMessageTemplateError.uxml";
        internal const string PromptTemplateAssetPath = "Editor/UI/ChatMessageTemplatePrompt.uxml";
        internal const string ProgressTemplateAssetPath = "Editor/UI/ChatMessageTemplateProgress.uxml";
        internal const string ReasoningTemplateAssetPath = "Editor/UI/ChatMessageTemplateReasoning.uxml";
        internal const string SessionEntryTemplateAssetPath = "Editor/UI/SessionEntryTemplate.uxml";
        internal const string ToolTemplateAssetPath = "Editor/UI/ChatMessageTemplateTool.uxml";
        internal const string UnfinishedSessionEntryClassName = "session-entry--unfinished";

        private ScrollView _scrollView;
        private ScrollView _sessionsScrollView;
        private TextField _userInput;
        private Button _sessionsButton;
        private Button _settingsButton;
        private Button _sendButton;
        private Button _stopButton;
        private Label _modelLabel;
        private readonly Dictionary<string, TextField> _streamedMessageFields = new Dictionary<string, TextField>();
        private ChatEditorWindowClient _chatClient;
        private UnityCodeAgentLogger _log;
        private CancellationTokenSource _lifecycleCancellation;
        private bool _isBusy;
        private ChatTranscriptScroller _transcriptScroller;
        private ChatProgressMessages _progressMessages;

        public Action<string> ShowProgressMessageHandler => content => _progressMessages?.ShowProgressMessage(content);

        private ChatEditorWindowClient ChatClient => _chatClient ??= new ChatEditorWindowClient();

        private UnityCodeAgentLogger Log => _log ??= new UnityCodeAgentLogger();

        private bool _isHydratingHistory;

        public static ChatEditorWindow ShowWindow()
        {
            var window = GetWindow<ChatEditorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(420f, 320f);
            return window;
        }

        public void CreateGUI()
        {
            BuildUi(UnityCodeAgentPackagePaths.LoadAsset<VisualTreeAsset>(ChatWindowAssetPath));
        }

        private void BuildUi(VisualTreeAsset visualTree)
        {
            Log.Debug(nameof(ChatEditorWindow), "Building chat editor window UI.");

            if (_scrollView != null || _userInput != null || _sendButton != null)
            {
                UnsubscribeFromClientUpdates();
                CancelLifecycleWork();
                _chatClient?.Dispose();
            }

            rootVisualElement.Clear();
            _scrollView = null;
            _sessionsScrollView = null;
            _userInput = null;
            _sessionsButton = null;
            _settingsButton = null;
            _sendButton = null;
            _stopButton = null;
            _modelLabel = null;
            _isBusy = false;
            _transcriptScroller = null;
            _progressMessages = null;
            _isHydratingHistory = false;
            _streamedMessageFields.Clear();

            _lifecycleCancellation = new CancellationTokenSource();

            if (visualTree == null)
            {
                var resolvedPath = UnityCodeAgentPackagePaths.ResolveAssetPath(ChatWindowAssetPath);
                Log.Error(nameof(ChatEditorWindow), $"Chat window visual tree asset missing path={resolvedPath}");
                rootVisualElement.Add(new HelpBox($"Missing UI asset at '{resolvedPath}'.", HelpBoxMessageType.Error));
                return;
            }

            visualTree.CloneTree(rootVisualElement);

            _scrollView = rootVisualElement.Q<ScrollView>("scroll-view");
            _sessionsScrollView = rootVisualElement.Q<ScrollView>("sessions-scroll-view");
            _userInput = rootVisualElement.Q<TextField>("user-input");
            _sessionsButton = rootVisualElement.Q<Button>("sessions-button");
            _settingsButton = rootVisualElement.Q<Button>("settings-button");
            _sendButton = rootVisualElement.Q<Button>("send-button");
            _stopButton = rootVisualElement.Q<Button>("stop-button");
            _modelLabel = rootVisualElement.Q<Label>("model-label");

            if (_scrollView == null || _sessionsScrollView == null || _userInput == null || _sessionsButton == null || _settingsButton == null || _sendButton == null || _stopButton == null || _modelLabel == null)
            {
                Log.Error(nameof(ChatEditorWindow), "Chat window UI is missing required elements.");
                rootVisualElement.Clear();
                rootVisualElement.Add(new HelpBox("Chat window UI is missing one or more required elements.", HelpBoxMessageType.Error));
                return;
            }

            _userInput.RegisterCallback<KeyDownEvent>(HandleUserInputKeyDown, TrickleDown.TrickleDown);
            _userInput.RegisterValueChangedCallback(_ => UpdateComposerState());
            _sessionsButton.clicked += HandleSessionsButtonClicked;
            _settingsButton.clicked += HandleSettingsButtonClicked;
            _sendButton.clicked += HandleSendButtonClicked;
            _stopButton.clicked += HandleStopButtonClicked;
            _sendButton.text = "Send";
            _stopButton.text = "Stop";
            _transcriptScroller = new ChatTranscriptScroller(_scrollView);
            _progressMessages = new ChatProgressMessages(_scrollView, _transcriptScroller, Log, ProgressTemplateAssetPath);
            SetBusyState(false);
            SetLoadingState(true);
            _progressMessages.ShowProgressMessage("Opening chat window...");
            SubscribeToClientUpdates();
            _ = InitializeWindowAsync();
        }

        private void OnDisable()
        {
            Log.Debug(nameof(ChatEditorWindow), "Disabling chat editor window.");
            UnsubscribeFromClientUpdates();
            CancelLifecycleWork();
            _chatClient?.Dispose();
            _chatClient = null;
            _transcriptScroller?.Reset();
            _transcriptScroller = null;
            _progressMessages = null;
        }

        private async Task<bool> SubmitPromptAsync()
        {
            if (_userInput == null || _sendButton == null)
            {
                return false;
            }

            var result = await ChatClient.SubmitPromptAsync(UnityCodeAgentSettings.GetUnityContext(), _userInput.value ?? string.Empty, GetLifecycleToken());
            ApplyUpdates(result.Updates);
            return result.Success;
        }

        private async Task<bool> AbortPromptAsync()
        {
            if (_stopButton == null)
            {
                return false;
            }

            var result = await ChatClient.AbortPromptAsync(UnityCodeAgentSettings.GetUnityContext(), GetLifecycleToken());
            ApplyUpdates(result.Updates);
            return result.Success;
        }

        private async Task ShowSessionsAsync()
        {
            if (_sessionsButton == null)
            {
                return;
            }

            var result = await ChatClient.ShowSessionsAsync(UnityCodeAgentSettings.GetUnityContext(), GetLifecycleToken());
            ApplyUpdates(result.Updates);
        }

        private async Task<bool> OpenSessionAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            var result = await ChatClient.OpenSessionAsync(UnityCodeAgentSettings.GetUnityContext(), sessionId, GetLifecycleToken());
            ApplyUpdates(result.Updates);
            return result.Success;
        }

        private void SetLoadingState(bool isLoading)
        {
            _isHydratingHistory = isLoading;
            if (!isLoading && !_isBusy)
            {
                _progressMessages?.PrepareForVisibleMessage();
            }

            rootVisualElement.SetEnabled(true);
            rootVisualElement.style.opacity = 1f;
            Log.Debug(nameof(ChatEditorWindow), $"Set loading state isLoading={isLoading}");
            UpdateComposerState();
        }

        private void SetBusyState(bool isBusy)
        {
            var wasBusy = _isBusy;
            _isBusy = isBusy;
            _progressMessages?.HandleBusyStateChanged(isBusy, wasBusy);

            Log.Debug(nameof(ChatEditorWindow), $"Set busy state isBusy={isBusy}");
            UpdateComposerState();
        }

        private void ShowSessions(IReadOnlyList<SessionSummaryDto> sessions, IReadOnlyCollection<string> unfinishedSessionIds)
        {
            _sessionsScrollView.contentContainer.Clear();

            foreach (var session in sessions)
            {
                var entry = BuildSessionEntry(session, unfinishedSessionIds);
                if (entry != null)
                {
                    _sessionsScrollView.Add(entry);
                }
            }

            _sessionsScrollView.style.display = DisplayStyle.Flex;
            _scrollView.style.display = DisplayStyle.None;
            UpdateComposerState();
        }

        private void SetUserInput(string userInput)
        {
            if (_userInput != null)
            {
                _userInput.value = userInput ?? string.Empty;
            }
        }

        private void SetModelLabel(string modelLabel)
        {
            if (_modelLabel != null)
            {
                _modelLabel.text = string.IsNullOrWhiteSpace(modelLabel) ? "No model selected" : modelLabel;
            }
        }

        private void ShowMessagesView(IReadOnlyList<AgentServiceEventEnvelope> messages)
        {
            _sessionsScrollView.style.display = DisplayStyle.None;
            _scrollView.style.display = DisplayStyle.Flex;
            UpdateComposerState();

            _scrollView.contentContainer.Clear();
            _streamedMessageFields.Clear();

            if (messages == null || messages.Count == 0)
            {
                return;
            }
            foreach (var message in messages)
            {
                ShowServiceEvent(message);
            }
        }

        private void AppendMessage(AgentEventType type, string content, bool expandable = false)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                Log.Debug(nameof(ChatEditorWindow), $"Skipping empty transcript message type={type}");
                return;
            }

            string templatePath = GetTemplateAssetPath(type);
            var template = UnityCodeAgentPackagePaths.LoadAsset<VisualTreeAsset>(templatePath);
            if (template == null)
            {
                Log.Warning(nameof(ChatEditorWindow), $"No visual tree asset found for message type={type} path={UnityCodeAgentPackagePaths.ResolveAssetPath(templatePath)}");
                return;
            }

            var container = new VisualElement();
            template.CloneTree(container);
            container.SetEnabled(false);

            var messageField = container.Q<TextField>("chat-message");
            if (messageField == null)
            {
                return;
            }

            if (expandable && !UnityCodeAgentSettings.GetUnityContext().ShowEventsSourceInChat)
            {
                var newlineIndex = content.IndexOfAny(new[] { '\r', '\n' });
                if (newlineIndex >= 0)
                {
                    content = content.Substring(0, newlineIndex);
                }
            }

            PrepareForVisibleTranscriptMessage();
            messageField.value = content;
            _scrollView.Add(messageField);
            _transcriptScroller?.RequestScrollToBottom(messageField);
        }

        private void UpsertStreamedMessage(string key, AgentEventType type, string content)
        {
            key ??= string.Empty;

            if (string.IsNullOrWhiteSpace(content))
            {
                Log.Debug(nameof(ChatEditorWindow), $"Skipping empty streamed transcript message key={key} type={type}");
                return;
            }

            if (_streamedMessageFields.TryGetValue(key, out var existingField))
            {
                PrepareForVisibleTranscriptMessage();
                existingField.value = content;
                _transcriptScroller?.RequestScrollToBottom(existingField);
                return;
            }

            AddStreamedMessage(key, type, content);
        }

        private void AddStreamedMessage(string key, AgentEventType type, string content)
        {
            string templatePath = GetTemplateAssetPath(type);
            var template = UnityCodeAgentPackagePaths.LoadAsset<VisualTreeAsset>(templatePath);
            if (template == null)
            {
                Log.Warning(nameof(ChatEditorWindow), $"No visual tree asset found for streamed message key={key} type={type} path={UnityCodeAgentPackagePaths.ResolveAssetPath(templatePath)}");
                return;
            }

            var container = new VisualElement();
            template.CloneTree(container);

            var messageField = container.Q<TextField>("chat-message");
            if (messageField == null)
            {
                return;
            }

            PrepareForVisibleTranscriptMessage();
            messageField.value = content;
            _streamedMessageFields[key] = messageField;
            _scrollView.Add(messageField);
            _transcriptScroller?.RequestScrollToBottom(messageField);
        }

        private void AppendToStreamedMessage(string key, AgentEventType type, string delta)
        {
            key ??= string.Empty;

            if (string.IsNullOrEmpty(delta))
            {
                Log.Debug(nameof(ChatEditorWindow), $"Skipping empty streamed transcript delta key={key} type={type}");
                return;
            }

            if (_streamedMessageFields.TryGetValue(key, out var existingField))
            {
                PrepareForVisibleTranscriptMessage();
                existingField.value += delta;
                _transcriptScroller?.RequestScrollToBottom(existingField);
                return;
            }

            AddStreamedMessage(key, type, delta);
        }

        private async Task InitializeWindowAsync()
        {
            var cancellationToken = GetLifecycleToken();
            try
            {
                var result = await ChatClient.InitializeAsync(UnityCodeAgentSettings.GetUnityContext(), cancellationToken);
                ApplyUpdates(result.Updates);
            }
            catch (OperationCanceledException)
            {
                Log.Debug(nameof(ChatEditorWindow), "Chat window initialization cancelled.");
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    SetLoadingState(false);
                }
            }
        }

        private CancellationToken GetLifecycleToken()
            => _lifecycleCancellation == null ? CancellationToken.None : _lifecycleCancellation.Token;

        private void CancelLifecycleWork()
        {
            if (_lifecycleCancellation == null)
            {
                return;
            }

            try
            {
                _lifecycleCancellation.Cancel();
            }
            catch
            {
            }

            _lifecycleCancellation.Dispose();
            _lifecycleCancellation = null;
        }

        private void SubscribeToClientUpdates()
        {
            EditorApplication.update -= HandleEditorUpdate;
            EditorApplication.update += HandleEditorUpdate;
        }

        private void UnsubscribeFromClientUpdates()
        {
            EditorApplication.update -= HandleEditorUpdate;
        }

        private void HandleEditorUpdate()
        {
            if (_chatClient == null)
            {
                return;
            }

            ApplyUpdates(_chatClient.DrainUpdates(UnityCodeAgentSettings.GetUnityContext()));
            _progressMessages?.DrainPending();
            _progressMessages?.ReportIfDue(_isBusy && !_isHydratingHistory);
        }

        private void ApplyUpdates(IReadOnlyList<ChatClientUpdate> updates)
        {
            if (updates == null)
            {
                return;
            }

            foreach (var update in updates)
            {
                ApplyUpdate(update);
            }
        }

        private void ApplyUpdate(ChatClientUpdate update)
        {
            switch (update)
            {
                case ChatSetBusyStateUpdate busy:
                    SetBusyState(busy.IsBusy);
                    break;
                case ChatShowSessionsUpdate showSessions:
                    ShowSessions(showSessions.Sessions, showSessions.UnfinishedSessionIds);
                    break;
                case ChatSetUserInput userInput:
                    SetUserInput(userInput.UserInput);
                    break;
                case ChatSetModelLabelUpdate modelLabel:
                    SetModelLabel(modelLabel.ModelLabel);
                    break;
                case ChatShowProgressMessageUpdate progress:
                    _progressMessages?.ShowProgressMessage(progress.Message);
                    break;
                case ChatShowMessagesUpdate replaceMessages:
                    ShowMessagesView(replaceMessages.Messages);
                    break;
                case ChatShowAgentEventUpdate showAgentEvent:
                    ShowServiceEvent(showAgentEvent.AgentEvent);
                    break;
                case ChatShowErrorUpdate showError:
                    AppendMessage(AgentEventType.Error, BuildErrorContent(showError.Message));
                    break;
                default:
                    Log.Warning(nameof(ChatEditorWindow), $"Received unknown update type {update?.GetType().Name}");
                    AppendMessage(AgentEventType.Unknown, $"Received unknown update type {update?.GetType().Name}");
                    break;
            }
        }

        private void UpdateComposerState()
        {
            if (_sendButton == null || _stopButton == null)
            {
                return;
            }

            var actionButtonsEnabled = !_isHydratingHistory;
            var isShowingSessions = _chatClient?.IsShowingSessions ?? false;
            var isActiveBusyResponse = _isBusy && !isShowingSessions;
            var canSend = actionButtonsEnabled
                && !isActiveBusyResponse
                && !string.IsNullOrWhiteSpace(_userInput?.value);
            var canStop = actionButtonsEnabled && isActiveBusyResponse;
            _scrollView?.SetEnabled(true);
            _sessionsScrollView?.SetEnabled(true);
            _userInput?.SetEnabled(true);
            _sessionsButton?.SetEnabled(actionButtonsEnabled && !isShowingSessions);
            _settingsButton?.SetEnabled(true);
            _sendButton.SetEnabled(canSend);
            _sendButton.text = "Send";
            _stopButton.SetEnabled(canStop);
        }

        private VisualElement BuildSessionEntry(SessionSummaryDto session, IReadOnlyCollection<string> unfinishedSessionIds)
        {
            var template = UnityCodeAgentPackagePaths.LoadAsset<VisualTreeAsset>(SessionEntryTemplateAssetPath);
            if (template == null)
            {
                Log.Error(nameof(ChatEditorWindow), $"Session entry visual tree asset missing path={UnityCodeAgentPackagePaths.ResolveAssetPath(SessionEntryTemplateAssetPath)}");
                return null;
            }

            var container = new VisualElement();
            template.CloneTree(container);

            var entryRoot = container.Q<VisualElement>("session-entry");
            if (entryRoot == null)
            {
                return null;
            }

            entryRoot.name = $"session-entry:{session.SessionId}";
            entryRoot.RegisterCallback<ClickEvent>(evt => _ = OpenSessionAsync(session.SessionId));
            if (IsUnfinishedSession(unfinishedSessionIds, session.SessionId))
            {
                entryRoot.AddToClassList(UnfinishedSessionEntryClassName);
            }
            var sessionName = entryRoot.Q<Label>("session-name");
            if (sessionName != null)
            {
                sessionName.text = BuildSessionName(session);
            }

            var sessionDate = entryRoot.Q<Label>("session-date");
            if (sessionDate != null)
            {
                sessionDate.text = BuildSessionSubtitle(session);
            }

            return entryRoot;
        }

        private static bool IsUnfinishedSession(IReadOnlyCollection<string> unfinishedSessionIds, string sessionId)
        {
            if (unfinishedSessionIds == null || string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            foreach (var unfinishedSessionId in unfinishedSessionIds)
            {
                if (string.Equals(unfinishedSessionId, sessionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildSessionSubtitle(SessionSummaryDto session)
        {
            var timestamp = session.ModifiedTime != default
                ? session.ModifiedTime
                : session.StartTime;

            if (timestamp != default)
            {
                return timestamp.ToLocalTime().ToString("g");
            }

            return session.SessionId;
        }

        private static string BuildSessionName(SessionSummaryDto session)
        {
            var name = string.IsNullOrWhiteSpace(session.Summary)
                ? session.SessionId
                : session.Summary;

            if (string.IsNullOrEmpty(name) || name.Length <= 60)
            {
                return name;
            }

            return name.Substring(0, 60) + "...";
        }

        private void ShowServiceEvent(AgentServiceEventEnvelope agentEvent)
        {
            if (agentEvent == null)
            {
                return;
            }

            if (agentEvent.IsSubAgentEvent)
            {
                return;
            }

            if (agentEvent.Type == AgentEventType.Error)
            {
                SetBusyState(false);
                AppendMessage(agentEvent.Type, BuildChatContent(agentEvent, 0, "The request failed."));
                return;
            }

            if (agentEvent.Type == AgentEventType.SessionIdle)
            {
                SetBusyState(false);
                if (UnityCodeAgentSettings.GetUnityContext().ShowAllEventsInChat)
                {
                    AppendMessage(agentEvent.Type, BuildChatContent(agentEvent));
                }
                return;
            }

            if (agentEvent.Type == AgentEventType.SessionStatusChanged)
            {
                SetBusyState(AgentSessionStatus.IsBusy(agentEvent.Content));
                if (UnityCodeAgentSettings.GetUnityContext().ShowAllEventsInChat)
                {
                    AppendMessage(agentEvent.Type, BuildChatContent(agentEvent));
                }
                return;
            }

            if (agentEvent.Type == AgentEventType.AssistantDelta || agentEvent.Type == AgentEventType.ReasoningDelta)
            {
                AppendToStreamedMessage(agentEvent.StreamKey, agentEvent.Type, agentEvent.Content);
                return;
            }

            if (agentEvent.Type == AgentEventType.AssistantMessage || agentEvent.Type == AgentEventType.Reasoning)
            {
                UpsertStreamedMessage(agentEvent.StreamKey, agentEvent.Type, BuildChatContent(agentEvent));
                return;
            }

            if (agentEvent.Type == AgentEventType.UserMessage)
            {
                AppendMessage(agentEvent.Type, BuildChatContent(agentEvent));
                return;
            }

            if (agentEvent.Type == AgentEventType.Tool || agentEvent.Type == AgentEventType.Service)
            {
                AppendMessage(agentEvent.Type, BuildChatContent(agentEvent, 100), true);
                return;
            }

            if (UnityCodeAgentSettings.GetUnityContext().ShowAllEventsInChat)
            {
                AppendMessage(agentEvent.Type, BuildChatContent(agentEvent, 100, agentEvent.Type.ToString()));
                return;
            }
        }

        private void HandleSendButtonClicked()
        {
            _ = SubmitPromptAsync();
        }

        private void HandleStopButtonClicked()
        {
            _ = AbortPromptAsync();
        }

        private void HandleSessionsButtonClicked()
        {
            _ = ShowSessionsAsync();
        }

        private void HandleSettingsButtonClicked()
        {
            var settings = UnityCodeAgentSettings.Instance;
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        private void HandleUserInputKeyDown(KeyDownEvent evt)
        {
            if (!evt.ctrlKey || (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter))
            {
                return;
            }

            evt.StopPropagation();
            _ = SubmitPromptAsync();
        }

        private string BuildChatContent(AgentServiceEventEnvelope agentEvent, int trimLength = 0, string fallbackContent = null)
        {
            if (agentEvent == null)
            {
                return fallbackContent ?? string.Empty;
            }

            var content = string.IsNullOrWhiteSpace(agentEvent.Content)
                ? fallbackContent ?? string.Empty
                : agentEvent.Content;

            if (trimLength > 0 && content.Length > trimLength)
            {
                content = content.Substring(0, trimLength) + "...";
            }

            if (!UnityCodeAgentSettings.GetUnityContext().ShowEventsSourceInChat)
            {
                return content;
            }

            var baseContent = $"{content}\n\n{agentEvent.Type}\n{agentEvent.StreamKey}";

            if (string.IsNullOrWhiteSpace(agentEvent.SourceJson))
            {
                return baseContent;
            }

            try
            {
                var parsedJson = JToken.Parse(agentEvent.SourceJson).ToString(Formatting.Indented);
                var unescapedJson = Regex.Unescape(parsedJson);
                return $"{baseContent}\n{unescapedJson}";
            }
            catch (JsonException exception)
            {
                Log.Warning(nameof(ChatEditorWindow), $"Failed to format event source JSON type={agentEvent.Type} error={exception.Message}");
                return baseContent;
            }
        }

        private string BuildErrorContent(string message)
        {
            var errorMessage = string.IsNullOrWhiteSpace(message) ? "The request failed." : message;

            return errorMessage;
        }

        private void PrepareForVisibleTranscriptMessage()
        {
            _progressMessages?.PrepareForVisibleMessage();
        }

        private static string GetTemplateAssetPath(AgentEventType eventType)
        {
            if (eventType == AgentEventType.UserMessage)
            {
                return PromptTemplateAssetPath;
            }

            if (eventType == AgentEventType.AssistantMessage || eventType == AgentEventType.AssistantDelta)
            {
                return AssistantTemplateAssetPath;
            }

            if (eventType == AgentEventType.Reasoning || eventType == AgentEventType.ReasoningDelta)
            {
                return ReasoningTemplateAssetPath;
            }

            if (eventType == AgentEventType.Tool)
            {
                return ToolTemplateAssetPath;
            }

            if (eventType == AgentEventType.Error)
            {
                return ErrorTemplateAssetPath;
            }

            return DefaultTemplateAssetPath;
        }
    }
}
