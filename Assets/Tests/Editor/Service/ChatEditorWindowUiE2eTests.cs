using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Menu;
using SignalLoop.UnityCodeAgent.Service;
using SignalLoop.UnityCodeAgent.Service.Mock;
using SignalLoop.UnityCodeAgent.Settings;
using SignalLoop.UnityCodeAgent.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

// Test file goal: end-to-end UI verification of the ChatEditorWindow with mock service.
// Simulates real user behavior: menu commands, button clicks, UI element inspection.
// Uses [UnityTest] + yield return null to pump EditorApplication.update between actions,
// so asynchronous SSE events are drained to the UI without reflection.
// Scope: session loading, prompt submission with response delivery, session switching.
// Boundaries: excludes the real Copilot service — uses MockService mode.

namespace SignalLoop.UnityCodeAgent.UI
{
    public sealed class ChatEditorWindowUiE2eTests
    {
        private const double WaitTimeoutSeconds = 5;

        private bool _originalMockAgentService;
        private bool _originalShowEventsSourceInChat;
        private bool _originalShowAllEventsInChat;

        [SetUp]
        public void SetUp()
        {
            var settings = UnityCodeAgentSettings.Instance;
            _originalMockAgentService = settings.MockAgentService;
            _originalShowEventsSourceInChat = settings.ShowEventsSourceInChat;
            _originalShowAllEventsInChat = settings.ShowAllEventsInChat;
            settings.MockAgentService = true;
            settings.ShowEventsSourceInChat = false;
            settings.ShowAllEventsInChat = false;
            MockServiceRuntime.Reset(UnityCodeAgentSettings.GetUnityContext().Paths);

            CloseWindowIfOpen();
        }

        [TearDown]
        public void TearDown()
        {
            CloseWindowIfOpen();
            MockServiceRuntime.Reset(UnityCodeAgentSettings.GetUnityContext().Paths);

            var settings = UnityCodeAgentSettings.Instance;
            settings.MockAgentService = _originalMockAgentService;
            settings.ShowEventsSourceInChat = _originalShowEventsSourceInChat;
            settings.ShowAllEventsInChat = _originalShowAllEventsInChat;
        }

        // ──────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────

        private static List<string> GetMessageContents(ChatEditorWindow window)
        {
            var scrollView = window.rootVisualElement.Q<ScrollView>("scroll-view");
            var messages = new List<string>();
            foreach (var child in scrollView.contentContainer.Children())
            {
                var field = child.Q<TextField>("chat-message");
                messages.Add(field?.value ?? "(missing)");
            }
            return messages;
        }

        private static ChatEditorWindow FindWindow()
            => Resources.FindObjectsOfTypeAll<ChatEditorWindow>().FirstOrDefault();

        private static string SimpleMockSessionId()
            => MockSessionData.SimpleSessionId(UnityCodeAgentSettings.GetUnityContext().Paths);

        private static string CodegenMockSessionId()
            => MockSessionData.CodegenSessionId(UnityCodeAgentSettings.GetUnityContext().Paths);

        private static void CloseWindowIfOpen()
        {
            var window = FindWindow();
            if (window != null)
            {
                window.Close();
            }
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, string failureMessage, double timeoutSeconds = WaitTimeoutSeconds)
        {
            var deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(failureMessage);
        }

        private static IEnumerator WaitForWindowReady()
        {
            yield return WaitUntil(
                () =>
                {
                    var window = FindWindow();
                    return window != null
                        && window.rootVisualElement.Q<ScrollView>("scroll-view") != null
                        && GetMessageContents(window).Count >= 2;
                },
                "Chat window did not open with initial history.");
        }

        private static IEnumerator WaitForMessageCount(ChatEditorWindow window, int minimumCount)
        {
            yield return WaitUntil(
                () => window != null && GetMessageContents(window).Count >= minimumCount,
                $"Transcript did not reach {minimumCount} messages.");
        }

        private static IEnumerator WaitForTranscriptIdle(ChatEditorWindow window, int minimumCount)
        {
            yield return WaitUntil(
                () =>
                {
                    if (window == null)
                    {
                        return false;
                    }

                    var sendButton = window.rootVisualElement.Q<Button>("send-button");
                    return sendButton != null
                        && string.Equals(sendButton.text, "Send", System.StringComparison.Ordinal)
                        && GetMessageContents(window).Count >= minimumCount;
                },
                "Transcript did not return to idle state.");
        }

        private static IEnumerator WaitForSessionsList(ChatEditorWindow window, int expectedCount)
        {
            yield return WaitUntil(
                () =>
                {
                    if (window == null)
                    {
                        return false;
                    }

                    var sessionsScrollView = window.rootVisualElement.Q<ScrollView>("sessions-scroll-view");
                    return sessionsScrollView != null
                        && sessionsScrollView.style.display.value == DisplayStyle.Flex
                        && sessionsScrollView.contentContainer.childCount == expectedCount;
                },
                "Sessions list did not become visible with the expected number of entries.");
        }

        private static IEnumerator WaitForMessageContaining(ChatEditorWindow window, int index, string expectedSubstring)
        {
            yield return WaitUntil(
                () =>
                {
                    if (window == null)
                    {
                        return false;
                    }

                    var messages = GetMessageContents(window);
                    return messages.Count > index && messages[index].Contains(expectedSubstring);
                },
                $"Transcript did not contain '{expectedSubstring}' at index {index}.");
        }

        private static void SubmitPrompt(ChatEditorWindow window, string text)
        {
            var userInput = window.rootVisualElement.Q<TextField>("user-input");
            var sendButton = window.rootVisualElement.Q<Button>("send-button");
            userInput.value = text;
            var navSubmit = NavigationSubmitEvent.GetPooled();
            navSubmit.target = sendButton;
            sendButton.SendEvent(navSubmit);
        }

        private static void OpenSessionsList(ChatEditorWindow window)
        {
            var sessionsButton = window.rootVisualElement.Q<Button>("sessions-button");
            var navSubmit = NavigationSubmitEvent.GetPooled();
            navSubmit.target = sessionsButton;
            sessionsButton.SendEvent(navSubmit);
        }

        private static void ClickSessionEntry(ChatEditorWindow window, string sessionId)
        {
            var sessionsScrollView = window.rootVisualElement.Q<ScrollView>("sessions-scroll-view");
            foreach (var child in sessionsScrollView.contentContainer.Children())
            {
                if (child.name == $"session-entry:{sessionId}")
                {
                    var clickEvent = ClickEvent.GetPooled();
                    clickEvent.target = child;
                    child.SendEvent(clickEvent);
                    return;
                }
            }
            throw new System.InvalidOperationException($"Session entry '{sessionId}' not found");
        }

        private static string GetSessionEntryName(ChatEditorWindow window, string sessionId)
        {
            return GetSessionEntry(window, sessionId).Q<Label>("session-name")?.text;
        }

        private static VisualElement GetSessionEntry(ChatEditorWindow window, string sessionId)
        {
            var sessionsScrollView = window.rootVisualElement.Q<ScrollView>("sessions-scroll-view");
            foreach (var child in sessionsScrollView.contentContainer.Children())
            {
                if (child.name == $"session-entry:{sessionId}")
                {
                    return child;
                }
            }

            throw new System.InvalidOperationException($"Session entry '{sessionId}' not found");
        }

        // ──────────────────────────────────────────────
        // Test 1
        // ──────────────────────────────────────────────

        [UnityTest]
        [Description("Open chat window and verify startup shows progress while keeping the transcript and input enabled.")]
        public IEnumerator OpenWindow_ShowsStartupProgressWithoutDisablingFullWindow()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);

            var root = window.rootVisualElement;
            var userInput = root.Q<TextField>("user-input");
            var scrollView = root.Q<ScrollView>("scroll-view");
            var sessionsButton = root.Q<Button>("sessions-button");
            var sendButton = root.Q<Button>("send-button");
            var settingsButton = root.Q<Button>("settings-button");

            Assert.That(root.enabledInHierarchy, Is.True, "Opening the chat window should not disable the full window.");
            Assert.That(scrollView.enabledInHierarchy, Is.True, "The transcript should remain enabled during startup.");
            Assert.That(userInput.enabledInHierarchy, Is.True, "The input should remain enabled during startup.");
            Assert.That(settingsButton.enabledInHierarchy, Is.True, "Settings should remain available during startup.");
            Assert.That(sessionsButton, Is.Not.Null, "The sessions button should remain present during startup.");
            Assert.That(sendButton, Is.Not.Null, "The send button should remain present during startup.");
            var messages = GetMessageContents(window);
            var startupProgressMessages = new[] { "Opening chat window...", "Loading current chat session...", "Starting agent service..." };
            Assert.That(
                messages.Any(message => startupProgressMessages.Contains(message)) || messages.Any(message => message.Contains("transform.position")),
                Is.True,
                "The window should show startup progress or complete startup before the first inspection.");

            yield return WaitForWindowReady();
            window.Close();
        }

        [UnityTest]
        [Description("Open chat window via menu command, verify history shows UserMessage then AssistantMessage in correct order, close window.")]
        public IEnumerator OpenWindowViaMenu_ShowsHistoryInCorrectOrder()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);

            var messages = GetMessageContents(window);
            Assert.That(messages.Count >= 2, Is.True,
                $"Expected at least 2 history messages, got {messages.Count}");
            Assert.That(messages[0], Does.Contain("How do I get the player"),
                "First message should be the user question");
            Assert.That(messages[1], Does.Contain("transform.position"),
                "Second message should be the assistant answer");

            var root = window.rootVisualElement;
            Assert.That(root.Q<ScrollView>("scroll-view"), Is.Not.Null);
            Assert.That(root.Q<TextField>("user-input"), Is.Not.Null);
            Assert.That(root.Q<Button>("send-button"), Is.Not.Null);
            Assert.That(root.Q<Button>("sessions-button"), Is.Not.Null);
            Assert.That(root.Q<Button>("settings-button"), Is.Not.Null);

            window.Close();
        }

        // ──────────────────────────────────────────────
        // Test 2
        // ──────────────────────────────────────────────

        [UnityTest]
        [Description("Open window, submit a prompt via the send button, verify user echo then assistant response appear in correct order.")]
        public IEnumerator SubmitPromptViaButton_ShowsResponseInCorrectOrder()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);

            var initialMessages = GetMessageContents(window);
            Assert.That(initialMessages.Count >= 2, Is.True,
                "History should have at least 2 messages");

            SubmitPrompt(window, "What is transform.position?");
            yield return WaitForTranscriptIdle(window, 4);

            var messages = GetMessageContents(window);
            Assert.That(messages.Count >= 4, Is.True,
                $"Expected at least 4 messages after prompt, got {messages.Count}");

            Assert.That(messages[0], Does.Contain("How do I get the player"),
                "[0] should be history user message");
            Assert.That(messages[1], Does.Contain("transform.position"),
                "[1] should be history assistant message");
            Assert.That(messages[2], Does.Contain("What is transform.position?"),
                "[2] should be the submitted prompt");
            Assert.That(messages[3], Does.Contain("To get the player's position"),
                "[3] should be the mock assistant response");

            window.Close();
        }

        [UnityTest]
        [Description("Open window with a persisted event-stream cursor, replay already accepted stream events, verify transcript messages are not duplicated.")]
        public IEnumerator ReplayedStreamEvents_DoNotDuplicateTranscriptMessages()
        {
            new EventStreamCursorStore().Save(
                UnityCodeAgentSettings.GetUnityContext().Paths,
                MockServiceRuntime.StreamGenerationId,
                2);

            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);

            var initialMessages = GetMessageContents(window);
            Assert.That(initialMessages.Count, Is.EqualTo(2),
                $"Simple mock history should render exactly 2 visible messages, got {initialMessages.Count}");

            EnqueueMockEvent(SimpleMockSessionId(), 1, AgentEventType.UserMessage, "How do I get the player's position in Unity?", string.Empty);
            EnqueueMockEvent(SimpleMockSessionId(), 2, AgentEventType.AssistantMessage, "You can get the player's world-space position using `transform.position` on the player's `Transform` component.\n\n```csharp\nVector3 playerPos = playerTransform.position;\n```\n\nIf you need the position in local space (relative to a parent), use `transform.localPosition` instead.", string.Empty);

            var deadline = EditorApplication.timeSinceStartup + 0.75d;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                yield return null;
            }

            var afterReplay = GetMessageContents(window);
            Assert.That(afterReplay, Is.EqualTo(initialMessages),
                "Replayed historical events should be ignored instead of appended to the transcript.");

            window.Close();
        }

        // ──────────────────────────────────────────────
        // Test 3
        // ──────────────────────────────────────────────

        [UnityTest]
        [Description("Open window, open sessions list, select codegen session, verify its UserMessage + ReasoningDelta + Tool history, then submit a prompt.")]
        public IEnumerator SwitchSessionViaUi_LoadsCodegenHistory()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);

            var initialMessages = GetMessageContents(window);
            Assert.That(initialMessages.Count >= 2, Is.True,
                "Default session should have history");

            OpenSessionsList(window);
            yield return WaitForSessionsList(window, 5);

            var sessionsScrollView = window.rootVisualElement.Q<ScrollView>("sessions-scroll-view");
            Assert.That(sessionsScrollView, Is.Not.Null);
            Assert.That(sessionsScrollView.style.display.value, Is.EqualTo(DisplayStyle.Flex),
                "Sessions scroll view should be visible");
            Assert.That(sessionsScrollView.contentContainer.childCount, Is.EqualTo(5),
                "Should show 5 mock sessions");

            ClickSessionEntry(window, CodegenMockSessionId());
            yield return WaitForMessageContaining(window, 0, "Create a rotating cube script");
            yield return WaitForMessageCount(window, 3);

            var codegenMessages = GetMessageContents(window);
            Assert.That(codegenMessages.Count >= 3, Is.True,
                $"Codegen history should have at least 3 messages, got {codegenMessages.Count}");
            Assert.That(codegenMessages[0], Does.Contain("Create a rotating cube script"),
                "[0] should be codegen user request");
            Assert.That(codegenMessages[1], Does.Contain("rotates"),
                "[1] should be codegen reasoning/response");

            bool hasToolEvent = codegenMessages.Any(m => m.Contains("file_write"));
            Assert.That(hasToolEvent, Is.True,
                "Codegen history should contain a file_write tool event");

            bool hasSimpleContent = codegenMessages.Any(m => m.Contains("How do I get the player"));
            Assert.That(hasSimpleContent, Is.False,
                "Codegen session should NOT contain simple-session content");

            SubmitPrompt(window, "Make it rotate faster");
            yield return WaitForTranscriptIdle(window, codegenMessages.Count + 2);

            var afterPrompt = GetMessageContents(window);
            Assert.That(afterPrompt.Count >= codegenMessages.Count + 2, Is.True,
                $"Expected at least {codegenMessages.Count + 2} messages after prompt, got {afterPrompt.Count}");

            bool hasNewPrompt = afterPrompt.Any(m => m.Contains("Make it rotate faster"));
            Assert.That(hasNewPrompt, Is.True,
                "New user prompt should appear in the transcript");

            bool hasNewRotator = afterPrompt.Any(m => m.Contains("NewRotator"));
            Assert.That(hasNewRotator, Is.True,
                "Codegen response should mention NewRotator.cs");

            window.Close();
        }

        [UnityTest]
        [Description("Open window, open sessions list, verify long session names are truncated to 60 chars with ellipsis.")]
        public IEnumerator SessionsList_TruncatesLongSessionNames()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);

            OpenSessionsList(window);
            yield return WaitForSessionsList(window, 5);

            var sessionsButton = window.rootVisualElement.Q<Button>("sessions-button");
            Assert.That(sessionsButton, Is.Not.Null);
            Assert.That(sessionsButton.style.display.value, Is.EqualTo(DisplayStyle.Flex),
                "Sessions button should stay visible while the sessions list is open");
            Assert.That(sessionsButton.enabledInHierarchy, Is.False,
                "Sessions button should be disabled while the sessions list is open");

            var sessionName = GetSessionEntryName(window, SimpleMockSessionId());
            Assert.That(sessionName, Is.Not.Null);
            Assert.That(sessionName.Length, Is.EqualTo(63),
                "Truncated session name should keep the first 60 chars and add '...'");
            Assert.That(sessionName, Does.EndWith("..."));
            Assert.That(sessionName, Is.EqualTo("Simple code question — how to get player position and verify whether the session label truncation works in the UI".Substring(0, 60) + "..."));

            window.Close();
        }

        [UnityTest]
        [Description("Open window, mark the active session busy, open sessions list, verify the active session is marked unfinished and Send remains the visible action.")]
        public IEnumerator SessionsList_MarksBusySessionAsUnfinished()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);

            EnqueueMockEvent(SimpleMockSessionId(), 700, AgentEventType.SessionStatusChanged, "streaming", "busy-marker");
            yield return WaitUntil(
                () => window.rootVisualElement.Q<Button>("send-button")?.text == "Stop",
                "Chat window did not enter a busy state.");

            OpenSessionsList(window);
            yield return WaitForSessionsList(window, 5);

            var sendButton = window.rootVisualElement.Q<Button>("send-button");
            Assert.That(sendButton, Is.Not.Null);
            Assert.That(sendButton.text, Is.EqualTo("Send"),
                "The composer should remain a send action while the sessions list is open.");

            var entry = GetSessionEntry(window, SimpleMockSessionId());
            Assert.That(entry.ClassListContains("session-entry--unfinished"), Is.True);

            ClickSessionEntry(window, SimpleMockSessionId());
            yield return WaitUntil(
                () => window.rootVisualElement.Q<ScrollView>("scroll-view")?.style.display.value == DisplayStyle.Flex
                    && window.rootVisualElement.Q<Button>("send-button")?.text == "Send",
                "Ready session did not reopen as an idle transcript.");

            OpenSessionsList(window);
            yield return WaitForSessionsList(window, 5);

            var reopenedEntry = GetSessionEntry(window, SimpleMockSessionId());
            Assert.That(reopenedEntry.ClassListContains("session-entry--unfinished"), Is.False);

            window.Close();
        }

        [UnityTest]
        [Description("Open window, report progress through the window delegate, verify progress is replaced and removed when an upserted assistant message arrives.")]
        public IEnumerator ProgressDelegate_ReplacesProgressAndRemovesBeforeUpsertedMessage()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);
            var initialCount = GetMessageContents(window).Count;

            window.ShowProgressMessageHandler("Thinking...");
            yield return WaitUntil(
                () =>
                {
                    var messages = GetMessageContents(window);
                    return messages.Count == initialCount + 1 && messages.Last() == "Thinking...";
                },
                "Progress message was not shown.");

            window.ShowProgressMessageHandler("Analyzing...");
            yield return WaitUntil(
                () =>
                {
                    var messages = GetMessageContents(window);
                    return messages.Count == initialCount + 1 && messages.Last() == "Analyzing...";
                },
                "Progress message was not replaced in place.");

            EnqueueMockEvent(SimpleMockSessionId(), 503, AgentEventType.AssistantMessage, "Progress resolved.", "progress-response");
            yield return WaitUntil(
                () =>
                {
                    var messages = GetMessageContents(window);
                    return messages.Count == initialCount + 1
                        && messages.Last() == "Progress resolved."
                        && !messages.Any(message => message == "Thinking..." || message == "Analyzing...");
                },
                "Progress message was not removed before the regular assistant message.");

            window.Close();
        }

        [UnityTest]
        [Description("Open window, report progress through the window delegate, verify progress is removed before an appended tool message arrives.")]
        public IEnumerator ProgressDelegate_IsRemovedBeforeAppendedMessage()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);
            var initialCount = GetMessageContents(window).Count;

            window.ShowProgressMessageHandler("Preparing tool output...");
            yield return WaitUntil(
                () => GetMessageContents(window).Last() == "Preparing tool output...",
                "Progress message was not shown before the tool event.");

            EnqueueMockEvent(SimpleMockSessionId(), 504, AgentEventType.Tool, "Tool result ready.", "progress-tool-response");
            yield return WaitUntil(
                () =>
                {
                    var messages = GetMessageContents(window);
                    return messages.Count == initialCount + 1
                        && messages.Last() == "Tool result ready."
                        && !messages.Any(message => message == "Preparing tool output...");
                },
                "Progress message was not removed before the appended tool message.");

            window.Close();
        }

        [UnityTest]
        [Description("Open window, report progress through the window delegate, verify progress is removed before a streamed delta arrives.")]
        public IEnumerator ProgressDelegate_IsRemovedBeforeStreamedDelta()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);
            var initialCount = GetMessageContents(window).Count;

            window.ShowProgressMessageHandler("Waiting for stream...");
            yield return WaitUntil(
                () => GetMessageContents(window).Last() == "Waiting for stream...",
                "Progress message was not shown before the streamed delta.");

            EnqueueMockEvent(SimpleMockSessionId(), 505, AgentEventType.AssistantDelta, "Stream started.", "progress-delta-response");
            yield return WaitUntil(
                () =>
                {
                    var messages = GetMessageContents(window);
                    return messages.Count == initialCount + 1
                        && messages.Last() == "Stream started."
                        && !messages.Any(message => message == "Waiting for stream...");
                },
                "Progress message was not removed before the streamed delta.");

            window.Close();
        }

        [UnityTest]
        [Description("Open window, report progress through the window delegate, then deliver SessionIdle and verify idle removes trailing progress.")]
        public IEnumerator ProgressDelegate_IsRemovedWhenChatBecomesNotBusy()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);
            var initialCount = GetMessageContents(window).Count;

            window.ShowProgressMessageHandler("Waiting for response...");
            yield return WaitUntil(
                () => GetMessageContents(window).Last() == "Waiting for response...",
                "Progress message was not shown before idle.");

            EnqueueMockEvent(SimpleMockSessionId(), 602, AgentEventType.SessionIdle, string.Empty, "progress-idle-test");
            yield return WaitUntil(
                () =>
                {
                    var messages = GetMessageContents(window);
                    return messages.Count == initialCount
                        && !messages.Any(message => message == "Waiting for response...");
                },
                "Progress message was not removed when the chat became idle.");

            window.Close();
        }

        [UnityTest]
        [Description("Open window, deliver a long tool event, verify the visible transcript trims the tool message to 100 characters plus ellipsis.")]
        public IEnumerator LongToolEvent_IsTrimmedInTranscript()
        {
            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);
            var initialCount = GetMessageContents(window).Count;
            var longContent = new string('a', 100) + "tail that should not render";

            EnqueueMockEvent(SimpleMockSessionId(), 801, AgentEventType.Tool, longContent, "long-tool-output");
            yield return WaitUntil(
                () => GetMessageContents(window).Count == initialCount + 1,
                "Long tool event was not appended.");

            var messages = GetMessageContents(window);
            Assert.That(messages.Last(), Is.EqualTo(new string('a', 100) + "..."));
            Assert.That(messages.Last(), Does.Not.Contain("tail that should not render"));

            window.Close();
        }

        [UnityTest]
        [Description("Open window with event source display enabled, deliver a long tool event, verify only the user-facing content is trimmed while source metadata still renders.")]
        public IEnumerator LongToolEvent_WithSourceVisible_TrimsContentAndKeepsSourceMetadata()
        {
            var settings = UnityCodeAgentSettings.Instance;
            settings.ShowEventsSourceInChat = true;

            EditorApplication.ExecuteMenuItem(UnityCodeAgentServiceMenu.MenuRoot + "Open Chat");
            yield return WaitForWindowReady();

            var window = FindWindow();
            Assert.That(window, Is.Not.Null);
            var initialCount = GetMessageContents(window).Count;
            var longContent = new string('b', 100) + "hidden tail";

            EnqueueMockEvent(
                SimpleMockSessionId(),
                802,
                AgentEventType.Tool,
                longContent,
                "source-tool-output",
                "{\"tool\":\"file_write\",\"result\":\"ok\"}");
            yield return WaitUntil(
                () => GetMessageContents(window).Count == initialCount + 1,
                "Long source-visible tool event was not appended.");

            var rendered = GetMessageContents(window).Last();
            Assert.That(rendered, Does.StartWith(new string('b', 100) + "..."));
            Assert.That(rendered, Does.Not.Contain("hidden tail"));
            Assert.That(rendered, Does.Contain("Tool"));
            Assert.That(rendered, Does.Contain("source-tool-output"));
            Assert.That(rendered, Does.Contain("file_write"));

            window.Close();
        }

        private static void EnqueueMockEvent(string sessionId, long sequenceNumber, AgentEventType type, string content, string streamKey, string sourceJson = "")
        {
            MockServiceRuntime.SharedState.EnqueueEvent(new AgentServiceEventEnvelope(
                sequenceNumber,
                sessionId,
                System.DateTimeOffset.UtcNow,
                content,
                streamKey,
                type,
                sourceJson,
                false));
        }
    }
}
