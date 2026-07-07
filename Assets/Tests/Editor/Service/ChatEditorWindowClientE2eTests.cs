using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Service.Mock;
using SignalLoop.UnityCodeAgent.Settings;
using SignalLoop.UnityCodeAgent.UI;
using UnityEngine;
using UnityEngine.TestTools;

// Test file goal: end-to-end verification of the ChatEditorWindowClient with mock service.
// Scope: session loading, prompt submission with response delivery, session switching.
// Boundaries: excludes the real Copilot service and HTTP transport — uses MockService mode.

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class ChatEditorWindowClientE2eTests
    {
        private const int EventDeliveryPollDelayMs = 50;
        private const int EventDeliveryMaxAttempts = 80;
        private static readonly DateTimeOffset TestSessionIdBaseTime = new DateTimeOffset(2026, 6, 30, 1, 0, 0, TimeSpan.Zero);

        private static UnityCodeAgentSettings CreateTestSettings()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeAgentSettings>();
            settings.Model = new ModelInfoDto("gpt-4o", "GPT-4o");
            return settings;
        }

        private static UnityContext CreateTestContext(
            UnityCodeAgentSettings settings,
            IReadOnlyList<string> skillDirectories = null,
            IReadOnlyList<string> disabledSkills = null,
            bool showAllEventsInChat = false)
            => new UnityContext(
                new UnityCodeAgentPaths("C:/UnityProject"),
                ProviderConfigDto.Create(settings.Model, null, null, null, null),
                string.Empty,
                true,
                false,
                showAllEventsInChat,
                true,
                5007,
                90,
                UnityCodeAgentLogger.LogLevel.Info,
                false,
                UnityCodeAgentTelemetryMode.None,
                string.Empty,
                string.Empty,
                false,
                skillDirectories ?? Array.Empty<string>(),
                disabledSkills ?? Array.Empty<string>(),
                UnityCodeAgentSettings.DefaultToolAssemblyNames,
                Array.Empty<string>(),
                string.Empty);

        private static string TestSessionId(int offsetMinutes)
            => UnityCodeAgentSessionIds.Create(new UnityCodeAgentPaths("C:/UnityProject"), TestSessionIdBaseTime.AddMinutes(offsetMinutes));

        private static string Session1Id => TestSessionId(0);

        private static string Session2Id => TestSessionId(1);

        private static string CreatedSession1Id => TestSessionId(10);

        private static string CreatedSession2Id => TestSessionId(11);

        private static string SimpleMockSessionId(UnityContext context)
            => MockSessionData.SimpleSessionId(context.Paths);

        private static string CodegenMockSessionId(UnityContext context)
            => MockSessionData.CodegenSessionId(context.Paths);

        private static UnityContext CreateNoModelContext(UnityCodeAgentSettings settings)
            => new UnityContext(
                new UnityCodeAgentPaths("C:/UnityProject"),
                ProviderConfigDto.Empty,
                "Select a model in Unity Code Agent settings then retry.",
                true,
                false,
                false,
                true,
                5007,
                90,
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
                string.Empty);

        private static UnityContext CreateContextFromSettings(UnityCodeAgentSettings settings)
        {
            if (!settings.TryCreateProviderConfig(out var provider, out var validationMessage))
            {
                provider = ProviderConfigDto.Empty;
            }
            else if (!settings.HasValidSelectedModel())
            {
                provider = ProviderConfigDto.Empty;
                validationMessage = "Select a model in Unity Code Agent settings then retry.";
            }

            return new UnityContext(
                new UnityCodeAgentPaths("C:/UnityProject"),
                provider,
                validationMessage,
                true,
                false,
                false,
                true,
                5007,
                90,
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
                string.Empty);
        }

        private static ChatEditorWindowClient CreateMockClient(UnityContext context)
        {
            var manifest = new EndpointManifest
            {
                Version = 1,
                Port = 0,
                ProjectRoot = "C:/UnityProject",
                ProjectId = "mock-project",
                ServiceProcessId = 0,
                UnityProcessId = 0,
                StartedAtUtc = DateTimeOffset.UtcNow,
                StreamGenerationId = Guid.NewGuid().ToString("N"),
            };

            var service = new AgentService(
                new MockServiceBootstrap(),
                new MockServiceBootstrap(),
                (_, currentManifest) => new MockAgentServiceApiClient(currentManifest, context.Paths, MockServiceRuntime.SharedState),
                (_, currentManifest) => new MockAgentServiceEventStreamClient(currentManifest, MockServiceRuntime.SharedState),
                _ => manifest);

            return new ChatEditorWindowClient(service);
        }

        private static async Task<IReadOnlyList<ChatClientUpdate>> WaitForUpdatesAsync(
            ChatEditorWindowClient client,
            UnityContext context,
            Func<IReadOnlyList<ChatClientUpdate>, bool> completion,
            string failureMessage)
        {
            var collectedUpdates = new List<ChatClientUpdate>();

            for (var attempt = 0; attempt < EventDeliveryMaxAttempts; attempt++)
            {
                await Task.Delay(EventDeliveryPollDelayMs);
                var updates = client.DrainUpdates(context);
                if (updates.Count > 0)
                {
                    collectedUpdates.AddRange(updates);
                }

                if (completion(collectedUpdates))
                {
                    return collectedUpdates;
                }
            }

            Assert.Fail(failureMessage);
            return collectedUpdates;
        }

        // ──────────────────────────────────────────────
        // Test 1: Open default session, verify history messages order,
        //         close / dispose.
        // ──────────────────────────────────────────────

        [Test]
        [Description("Open default mock session, verify history contains UserMessage + AssistantMessage in correct order, verify idle state.")]
        public async Task InitializeSession_LoadsHistoryInCorrectOrder()
        {
            MockServiceRuntime.Reset();
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            using var client = CreateMockClient(context);

            // Act: initialize (loads the default session "mock-session-simple")
            var result = await client.InitializeAsync(context, CancellationToken.None);

            // Assert: result.Updates contain ShowMessages with the history
            var showMessages = result.Updates.OfType<ChatShowMessagesUpdate>().FirstOrDefault();
            Assert.That(showMessages, Is.Not.Null, "Expected a ChatShowMessagesUpdate after initialization");
            Assert.That(showMessages.Messages, Is.Not.Null, "Messages list should not be null");
            Assert.That(showMessages.Messages.Count >= 2,
                "Expected at least 2 messages in simple-session history");

            // Verify exact message order: [0] = UserMessage, [1] = AssistantMessage
            var messages = showMessages.Messages;
            Assert.That(messages[0].Type, Is.EqualTo(AgentEventType.UserMessage),
                "First history message should be a UserMessage");
            Assert.That(messages[0].Content, Does.Contain("How do I get the player"),
                "First message should contain the user question");

            Assert.That(messages[1].Type, Is.EqualTo(AgentEventType.AssistantMessage),
                "Second history message should be an AssistantMessage");
            Assert.That(messages[1].Content, Does.Contain("transform.position"),
                "Second message should contain the assistant answer");

            // Verify idle / busy updates
            var busyUpdates = result.Updates.OfType<ChatSetBusyStateUpdate>();
            Assert.That(busyUpdates.Any(), Is.True);
        }

        // ──────────────────────────────────────────────
        // Test 2: Open session, send prompt, verify user
        //         echo + assistant response in order.
        // ──────────────────────────────────────────────

        [Test]
        [Description("Open default session, submit a prompt, verify the UI message order: HistoryUser, HistoryAsst, PromptUser, PromptAsst.")]
        public async Task SubmitPrompt_DeliversResponseInCorrectOrder()
        {
            MockServiceRuntime.Reset();
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            using var client = CreateMockClient(context);

            // Arrange: initialize to load mock-session-simple history
            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            client.DrainUpdates(context);

            // Act: submit a prompt
            var submitResult = await client.SubmitPromptAsync(context, "What is transform.position?", CancellationToken.None);
            Assert.That(submitResult.Success, Is.True);

            // Get the immediate echo updates (UserMessage + SetUserInput + SetBusyState)
            // Then wait for the background event stream to deliver the assistant response
            var updates = await WaitForUpdatesAsync(
                client,
                context,
                collected =>
                {
                    var events = collected.OfType<ChatShowAgentEventUpdate>().Select(u => u.AgentEvent).ToList();
                    return events.Any(e => e.Type == AgentEventType.AssistantMessage && e.Content.Contains("To get the player's position"))
                        && events.Any(e => e.Type == AgentEventType.SessionIdle);
                },
                "Timed out waiting for the mock prompt response in the simple session.");
            var agentEvents = updates.OfType<ChatShowAgentEventUpdate>()
                .Select(u => u.AgentEvent)
                .ToList();

            // We should have: [0] user echo, [1..n] assistant response events
            Assert.That(agentEvents.Count >= 2, Is.True);

            var userEcho = agentEvents[0];
            Assert.That(userEcho.Type, Is.EqualTo(AgentEventType.UserMessage));
            Assert.That(userEcho.Content, Does.Contain("What is transform.position?"));

            var assistantEvents = agentEvents.Where(e => e.Type == AgentEventType.AssistantMessage).ToList();
            Assert.That(assistantEvents.Count >= 1, Is.True);
            Assert.That(assistantEvents[0].Content, Does.Contain("To get the player's position"));

            var idleEvents = agentEvents.Where(e => e.Type == AgentEventType.SessionIdle).ToList();
            Assert.That(idleEvents.Count >= 1, Is.True);
        }

        // ──────────────────────────────────────────────
        // Test 3: Show sessions list, select a different
        //         session (codegen), verify its history.
        // ──────────────────────────────────────────────

        [Test]
        [Description("Show sessions, select codegen session, verify it loads with its own history UserMessage + AssistantMessage + Tool, not the simple-session history.")]
        public async Task SwitchSession_LoadsDifferentSessionHistory()
        {
            MockServiceRuntime.Reset();
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            using var client = CreateMockClient(context);

            // Arrange: initialize default session first
            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            client.DrainUpdates(context);

            // Act 1: show sessions list
            var sessionsResult = await client.ShowSessionsAsync(context, CancellationToken.None);
            Assert.That(sessionsResult.Success, Is.True);

            var showSessions = sessionsResult.Updates.OfType<ChatShowSessionsUpdate>().FirstOrDefault();
            Assert.That(showSessions, Is.Not.Null);
            Assert.That(showSessions.Sessions.Count, Is.EqualTo(5));

            var codegenSessionId = CodegenMockSessionId(context);

            // Find the codegen session
            var codegen = showSessions.Sessions
                .FirstOrDefault(s => s.SessionId == codegenSessionId);
            Assert.That(codegen, Is.Not.Null);

            // Act 2: open the codegen session
            var openResult = await client.OpenSessionAsync(context, codegenSessionId, CancellationToken.None);
            Assert.That(openResult.Success, Is.True);

            // OpenSessionAsync returns ChatShowMessagesUpdate in result.Updates
            var showMessages = openResult.Updates.OfType<ChatShowMessagesUpdate>().FirstOrDefault();
            Assert.That(showMessages, Is.Not.Null);
            Assert.That(showMessages.Messages, Is.Not.Null);
            Assert.That(showMessages.Messages.Count >= 3, Is.True);

            // Verify codegen messages (UserMessage + ReasoningDelta + Tool + ...)
            var messages = showMessages.Messages;
            Assert.That(messages[0].Type, Is.EqualTo(AgentEventType.UserMessage),
                "Codegen first message should be UserMessage");
            Assert.That(messages[0].Content, Does.Contain("Create a rotating cube script"),
                "Codegen first message should contain the user's request");

            Assert.That(messages[1].Type, Is.EqualTo(AgentEventType.ReasoningDelta),
                "Codegen second message should be ReasoningDelta");

            // There should be a Tool event
            var toolMessages = messages.Where(m => m.Type == AgentEventType.Tool).ToList();
            Assert.That(toolMessages.Count >= 1,
                "Expected at least one Tool event in codegen history");
            Assert.That(toolMessages[0].Content, Does.Contain("file_write"),
                "Tool event should reference file_write");

            // Verify this is NOT the simple-session history
            var hasSimpleSessionContent = messages.Any(m =>
                m.Content != null && m.Content.Contains("How do I get the player"));
            Assert.That(hasSimpleSessionContent, Is.False,
                "Codegen session should NOT contain simple-session message content");

            // Also verify we can submit a prompt in the codegen session
            var submitResult = await client.SubmitPromptAsync(context, "Make it rotate faster", CancellationToken.None);
            Assert.That(submitResult.Success, Is.True, "SubmitPromptAsync in codegen should succeed");

            var promptUpdates = await WaitForUpdatesAsync(
                client,
                context,
                collected =>
                {
                    var events = collected.OfType<ChatShowAgentEventUpdate>().Select(u => u.AgentEvent).ToList();
                    return events.Any(e => e.Type == AgentEventType.Tool)
                        && events.Any(e => e.Type == AgentEventType.AssistantMessage && e.Content != null && e.Content.Contains("NewRotator"))
                        && events.Any(e => e.Type == AgentEventType.SessionIdle);
                },
                "Timed out waiting for the codegen prompt response.");
            var promptEvents = promptUpdates.OfType<ChatShowAgentEventUpdate>()
                .Select(u => u.AgentEvent)
                .ToList();

            // Should have codegen response sequence (AssistantDelta + Tool + AssistantMessage + SessionIdle)
            var toolEvents = promptEvents.Where(e => e.Type == AgentEventType.Tool).ToList();
            Assert.That(toolEvents.Count >= 1,
                "Expected at least one Tool event in codegen response");

            var assistantInResponse = promptEvents.Any(e =>
                e.Type == AgentEventType.AssistantMessage &&
                e.Content != null && e.Content.Contains("NewRotator"));
            Assert.That(assistantInResponse, Is.True,
                "Codegen response should mention NewRotator.cs");
        }

        [Test]
        [Description("Goal: verify the chat client resets busy state when the background event stream fails so the next prompt remains a send action instead of becoming an abort action. Scope: ChatEditorWindowClient state recovery only. Boundaries: excludes real HTTP/SSE transport.")]
        public async Task StreamFailure_ResetsBusyStateAndAllowsNextPrompt()
        {
            var settings = CreateTestSettings();
            var harness = new FailingStreamHarness(settings);
            var context = CreateTestContext(settings);
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);
            await WaitForUpdatesAsync(
                client,
                context,
                HasStreamRecoveryEvent,
                "Timed out waiting for the initial failing stream recovery event.");

            var firstSubmit = await client.SubmitPromptAsync(context, "first prompt", CancellationToken.None);
            Assert.That(firstSubmit.Success, Is.True);

            var recoveryUpdates = await WaitForUpdatesAsync(
                client,
                context,
                HasStreamRecoveryEvent,
                "Timed out waiting for the failing stream after submit to reset busy state.");

            Assert.That(
                recoveryUpdates.OfType<ChatShowAgentEventUpdate>().Any(update => update.AgentEvent.Type == AgentEventType.Service),
                Is.False,
                "Connection recovery progress should be shown through the progress callback, not as transcript service events.");
            Assert.That(harness.ProgressMessages, Does.Contain("Agent service connection lost. Restarting service connection..."));
            Assert.That(harness.ProgressMessages, Does.Contain("Agent service connection restored."));

            var secondSubmit = await client.SubmitPromptAsync(context, "second prompt", CancellationToken.None);
            Assert.That(secondSubmit.Success, Is.True);
            Assert.That(harness.ApiOperations, Is.EqualTo(new[] { $"open:{Session1Id}", "send:first prompt", "send:second prompt" }));
        }

        [Test]
        [Description("Goal: verify live status events update composer busy state independently of session snapshot rendering. Scope: ChatEditorWindowClient live stream handling only. Boundaries: excludes real SSE transport.")]
        public async Task ReplayedIdleEvent_ResetsBusyStateAndAllowsNextPrompt()
        {
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            var harness = new ReplayedIdleHarness();
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            var firstSubmit = await client.SubmitPromptAsync(context, "first prompt", CancellationToken.None);
            Assert.That(firstSubmit.Success, Is.True);

            var updates = await WaitForUpdatesAsync(
                client,
                context,
                collected => collected.OfType<ChatSetBusyStateUpdate>().Any(update => !update.IsBusy),
                "Timed out waiting for replayed idle event to reset busy state.");

            Assert.That(
                updates.OfType<ChatShowAgentEventUpdate>().Any(update => update.AgentEvent.Type == AgentEventType.SessionIdle),
                Is.True,
                "Live stream idle events should be routed after the cursor accepts them.");

            var secondSubmit = await client.SubmitPromptAsync(context, "second prompt", CancellationToken.None);
            Assert.That(secondSubmit.Success, Is.True);
            Assert.That(harness.ApiOperations, Is.EqualTo(new[] { $"open:{Session1Id}", "send:first prompt", "send:second prompt" }));
        }

        [Test]
        [Description("Goal: verify model changes reconfigure the active session id with the selected model before the next prompt is sent. Scope: ChatEditorWindowClient model-change coordination only. Boundaries: excludes real HTTP/SSE transport.")]
        public async Task ModelChange_ReconfiguresActiveSessionBeforeNextPromptAndUpdatesLabel()
        {
            var settings = CreateTestSettings();
            var harness = new ModelChangeHarness(settings);
            var context = CreateTestContext(settings);
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);
            Assert.That(
                initResult.Updates.OfType<ChatSetModelLabelUpdate>().Last().ModelLabel,
                Is.EqualTo("GPT-4o (gpt-4o)"));

            settings.Model = new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4");

            context = CreateTestContext(settings);
            var submitResult = await client.SubmitPromptAsync(context, "use the new model", CancellationToken.None);
            Assert.That(submitResult.Success, Is.True);

            var updates = client.DrainUpdates(context);
            Assert.That(
                updates.OfType<ChatSetModelLabelUpdate>().Last().ModelLabel,
                Is.EqualTo("Claude Sonnet 4 (claude-sonnet-4)"));
            Assert.That(harness.ApiOperations.Count, Is.EqualTo(3));
            Assert.That(harness.ApiOperations[0], Is.EqualTo($"open:{Session1Id}:gpt-4o"));
            Assert.That(harness.ApiOperations[1], Is.EqualTo($"open:{Session1Id}:claude-sonnet-4"));
            Assert.That(harness.ApiOperations[2], Is.EqualTo($"send:{Session1Id}:use the new model"));
        }

        [Test]
        [Description("Goal: verify model changes do not reopen the active session while the sessions list is visible, but selecting a session applies the latest model. Scope: ChatEditorWindowClient sessions-view model-change coordination only. Boundaries: excludes UI Toolkit rendering and real HTTP/SSE transport.")]
        public async Task SessionsView_ModelChange_DoesNotReopenUntilSessionSelected()
        {
            var settings = CreateTestSettings();
            var harness = new ModelChangeHarness(settings);
            var context = CreateTestContext(settings);
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            var sessionsResult = await client.ShowSessionsAsync(context, CancellationToken.None);
            Assert.That(sessionsResult.Success, Is.True);

            settings.Model = new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4");
            context = CreateTestContext(settings);
            var updates = client.DrainUpdates(context);

            Assert.That(harness.ApiOperations, Is.EqualTo(new[]
            {
                $"open:{Session1Id}:gpt-4o",
            }));
            Assert.That(
                updates.OfType<ChatSetModelLabelUpdate>().Last().ModelLabel,
                Is.EqualTo("Claude Sonnet 4 (claude-sonnet-4)"));

            var openResult = await client.OpenSessionAsync(context, Session1Id, CancellationToken.None);
            Assert.That(openResult.Success, Is.True);

            Assert.That(harness.ApiOperations, Is.EqualTo(new[]
            {
                $"open:{Session1Id}:gpt-4o",
                $"open:{Session1Id}:claude-sonnet-4",
            }));
        }

        [Test]
        [Description("Goal: verify submitting a prompt from the sessions list after a model change creates a new session with the latest model instead of reopening the previous active session. Scope: ChatEditorWindowClient sessions-view submit behavior only. Boundaries: excludes UI Toolkit rendering and real HTTP/SSE transport.")]
        public async Task SessionsView_ModelChange_SubmitPromptCreatesNewSessionWithLatestModel()
        {
            var settings = CreateTestSettings();
            var harness = new ModelChangeHarness(settings);
            var context = CreateTestContext(settings);
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            var sessionsResult = await client.ShowSessionsAsync(context, CancellationToken.None);
            Assert.That(sessionsResult.Success, Is.True);

            settings.Model = new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4");
            context = CreateTestContext(settings);
            client.DrainUpdates(context);

            var submitResult = await client.SubmitPromptAsync(context, "start with latest model", CancellationToken.None);
            Assert.That(submitResult.Success, Is.True);

            Assert.That(harness.ApiOperations.Count, Is.EqualTo(3));
            Assert.That(harness.ApiOperations[0], Is.EqualTo($"open:{Session1Id}:gpt-4o"));
            Assert.That(harness.ApiOperations[1], Does.StartWith("create:"));
            Assert.That(harness.ApiOperations[1], Does.EndWith(":claude-sonnet-4"));
            Assert.That(harness.ApiOperations[2], Is.EqualTo($"send:{CreatedSession1Id}:start with latest model"));
        }

        [Test]
        [Description("Goal: verify session-bound skill changes reconfigure the active session before the next prompt is sent. Scope: ChatEditorWindowClient session request signature only. Boundaries: excludes real HTTP/SSE transport and settings inspector UI.")]
        public async Task SkillChange_ReconfiguresActiveSessionBeforeNextPrompt()
        {
            var settings = CreateTestSettings();
            var harness = new ModelChangeHarness(settings);
            var context = CreateTestContext(settings, new[] { "Assets/AgentSkills" });
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            context = CreateTestContext(settings, new[] { "Assets/AgentSkills" }, new[] { "expensive-skill" });
            var submitResult = await client.SubmitPromptAsync(context, "use updated skills", CancellationToken.None);
            Assert.That(submitResult.Success, Is.True);

            Assert.That(harness.ApiOperations.Count, Is.EqualTo(3));
            Assert.That(harness.ApiOperations[0], Is.EqualTo($"open:{Session1Id}:gpt-4o:skills=Assets/AgentSkills"));
            Assert.That(harness.ApiOperations[1], Is.EqualTo($"open:{Session1Id}:gpt-4o:skills=Assets/AgentSkills:disabled=expensive-skill"));
            Assert.That(harness.ApiOperations[2], Is.EqualTo($"send:{Session1Id}:use updated skills"));
        }

        [Test]
        [Description("Goal: verify live debug-only settings do not reconfigure the active session before the next prompt is sent. Scope: ChatEditorWindowClient session request signature only. Boundaries: excludes UI Toolkit rendering and real HTTP/SSE transport.")]
        public async Task LiveDebugChange_DoesNotReconfigureActiveSessionBeforeNextPrompt()
        {
            var settings = CreateTestSettings();
            var harness = new ModelChangeHarness(settings);
            var context = CreateTestContext(settings, showAllEventsInChat: false);
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            context = CreateTestContext(settings, showAllEventsInChat: true);
            var submitResult = await client.SubmitPromptAsync(context, "debug setting changed", CancellationToken.None);
            Assert.That(submitResult.Success, Is.True);

            Assert.That(harness.ApiOperations, Is.EqualTo(new[]
            {
                $"open:{Session1Id}:gpt-4o",
                $"send:{Session1Id}:debug setting changed",
            }));
        }

        [Test]
        [Description("Goal: verify opening chat without a selected model returns an immediate settings guidance message without calling the service. Scope: ChatEditorWindowClient provider validation only. Boundaries: excludes UI Toolkit rendering and settings inspector behavior.")]
        public async Task Initialize_NoSelectedModel_ReturnsSettingsMessageWithoutServiceCall()
        {
            var settings = CreateTestSettings();
            settings.Model = null;
            var harness = new ModelChangeHarness(settings);
            var context = CreateNoModelContext(settings);
            using var client = harness.CreateClient();

            var result = await client.InitializeAsync(context, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.Updates.OfType<ChatShowErrorUpdate>().Single().Message,
                Is.EqualTo("Select a model in Unity Code Agent settings then retry."));
            Assert.That(result.Updates.OfType<ChatSetBusyStateUpdate>().Single().IsBusy, Is.False);
            Assert.That(harness.ApiOperations, Is.Empty);
        }

        [Test]
        [Description("Goal: verify submitting a prompt without a selected model returns an immediate settings guidance message without creating or sending a session. Scope: ChatEditorWindowClient submit validation only. Boundaries: excludes UI Toolkit rendering and real service transport.")]
        public async Task SubmitPrompt_NoSelectedModel_ReturnsSettingsMessageWithoutServiceCall()
        {
            var settings = CreateTestSettings();
            settings.Model = null;
            var harness = new ModelChangeHarness(settings);
            var context = CreateNoModelContext(settings);
            using var client = harness.CreateClient();

            var result = await client.SubmitPromptAsync(context, "hello", CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.Updates.OfType<ChatShowErrorUpdate>().Single().Message,
                Is.EqualTo("Select a model in Unity Code Agent settings then retry."));
            Assert.That(result.Updates.OfType<ChatShowAgentEventUpdate>(), Is.Empty);
            Assert.That(result.Updates.OfType<ChatSetBusyStateUpdate>().Single().IsBusy, Is.False);
            Assert.That(harness.ApiOperations, Is.Empty);
        }

        [Test]
        [Description("Goal: verify a model selected for a previous provider BaseUrl cannot be used to submit prompts after BaseUrl changes. Scope: ChatEditorWindowClient provider/model validation only. Boundaries: excludes settings inspector clearing.")]
        public async Task SubmitPrompt_StaleModelAfterBaseUrlChange_ReturnsSettingsMessageWithoutServiceCall()
        {
            var settings = CreateTestSettings();
            settings.SetAvailableModels(new[] { new ModelInfoDto("gpt-4o", "GPT-4o") });
            Assert.That(settings.SelectModel(new ModelInfoDto("gpt-4o", "GPT-4o")), Is.True);
            settings.ProviderType = UnityCodeAgentProviderType.Byok;
            settings.ByokBaseUrl = "https://provider.example/v1";
            var harness = new ModelChangeHarness(settings);
            var context = CreateContextFromSettings(settings);
            using var client = harness.CreateClient();

            var result = await client.SubmitPromptAsync(context, "hello", CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Updates.OfType<ChatSetModelLabelUpdate>().Single().ModelLabel, Is.EqualTo("No model selected"));
            Assert.That(result.Updates.OfType<ChatShowErrorUpdate>().Single().Message, Is.EqualTo("Select a model in Unity Code Agent settings then retry."));
            Assert.That(harness.ApiOperations, Is.Empty);
        }

        [Test]
        [Description("Goal: verify a refreshed model list still requires explicit model selection before prompt submission. Scope: ChatEditorWindowClient model validation only. Boundaries: excludes live model refresh transport.")]
        public async Task SubmitPrompt_RefreshedButUnselectedModelList_ReturnsSettingsMessageWithoutServiceCall()
        {
            var settings = CreateTestSettings();
            settings.SetAvailableModels(new[] { new ModelInfoDto("gpt-4o", "GPT-4o") });
            var harness = new ModelChangeHarness(settings);
            var context = CreateContextFromSettings(settings);
            using var client = harness.CreateClient();

            var result = await client.SubmitPromptAsync(context, "hello", CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Updates.OfType<ChatSetModelLabelUpdate>().Single().ModelLabel, Is.EqualTo("No model selected"));
            Assert.That(result.Updates.OfType<ChatShowErrorUpdate>().Single().Message, Is.EqualTo("Select a model in Unity Code Agent settings then retry."));
            Assert.That(harness.ApiOperations, Is.Empty);
        }

        [Test]
        [Description("Goal: verify explicit model selection after refresh allows prompt routing with the current BYOK BaseUrl and selected model. Scope: ChatEditorWindowClient request construction only. Boundaries: excludes real service transport.")]
        public async Task SubmitPrompt_ExplicitSelectionAfterRefresh_RoutesWithCurrentBaseUrlAndModel()
        {
            var settings = CreateTestSettings();
            settings.ProviderType = UnityCodeAgentProviderType.Byok;
            settings.ByokBaseUrl = "https://provider.example/v1/";
            settings.SetAvailableModels(new[] { new ModelInfoDto("provider-model", "Provider Model") });
            Assert.That(settings.SelectModel(new ModelInfoDto("provider-model", "Provider Model")), Is.True);
            var harness = new ModelChangeHarness(settings);
            var context = CreateContextFromSettings(settings);
            using var client = harness.CreateClient();

            var result = await client.SubmitPromptAsync(context, "hello provider", CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(harness.ApiOperations.Count, Is.EqualTo(2));
            Assert.That(harness.ApiOperations[0], Does.StartWith("create:"));
            Assert.That(harness.ApiOperations[0], Does.EndWith(":provider-model:base=https://provider.example/v1"));
            Assert.That(harness.ApiOperations[1], Is.EqualTo($"send:{CreatedSession1Id}:hello provider"));
        }

        [Test]
        [Description("Goal: verify a provider BaseUrl change after an active session updates chat label to no model selected and does not reopen the session. Scope: ChatEditorWindowClient settings refresh loop only. Boundaries: excludes UI Toolkit rendering.")]
        public async Task DrainUpdates_BaseUrlChangedAfterActiveSession_LabelsNoModelSelectedWithoutReopen()
        {
            var settings = CreateTestSettings();
            settings.SetAvailableModels(new[] { new ModelInfoDto("gpt-4o", "GPT-4o") });
            Assert.That(settings.SelectModel(new ModelInfoDto("gpt-4o", "GPT-4o")), Is.True);
            var harness = new ModelChangeHarness(settings);
            var context = CreateContextFromSettings(settings);
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            settings.ProviderType = UnityCodeAgentProviderType.Byok;
            settings.ByokBaseUrl = "https://provider.example/v1";
            context = CreateContextFromSettings(settings);
            var updates = await WaitForUpdatesAsync(
                client,
                context,
                collected => collected.OfType<ChatSetModelLabelUpdate>().Any(update => update.ModelLabel == "No model selected"),
                "Timed out waiting for invalid provider settings to update the model label.");

            Assert.That(updates.OfType<ChatSetModelLabelUpdate>().Last().ModelLabel, Is.EqualTo("No model selected"));
            Assert.That(harness.ApiOperations, Is.EqualTo(new[] { $"open:{Session1Id}:gpt-4o" }));
        }

        [Test]
        [Description("Goal: verify repeated editor update drains with invalid model settings do not run the session reconfiguration path. Scope: ChatEditorWindowClient background settings refresh only. Boundaries: excludes console log capture.")]
        public async Task DrainUpdates_InvalidModelRepeatedly_DoesNotReconfigureSession()
        {
            var settings = CreateTestSettings();
            settings.SetAvailableModels(new[] { new ModelInfoDto("gpt-4o", "GPT-4o") });
            Assert.That(settings.SelectModel(new ModelInfoDto("gpt-4o", "GPT-4o")), Is.True);
            var harness = new ModelChangeHarness(settings);
            var context = CreateContextFromSettings(settings);
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            settings.ProviderType = UnityCodeAgentProviderType.Byok;
            settings.ByokBaseUrl = "https://provider.example/v1";
            context = CreateContextFromSettings(settings);
            for (var index = 0; index < 5; index++)
            {
                await Task.Delay(150);
                client.DrainUpdates(context);
            }

            Assert.That(harness.ApiOperations, Is.EqualTo(new[] { $"open:{Session1Id}:gpt-4o" }));
        }

        [Test]
        [Description("Goal: verify non-active session events received from SSE are ignored by the visible transcript client. Scope: ChatEditorWindowClient event filtering only. Boundaries: excludes UI Toolkit rendering and real SSE transport.")]
        public async Task NonActiveSessionEvent_IsIgnored()
        {
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            var harness = new NonActiveEventHarness();
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            await harness.EventDelivered.Task;
            var updates = client.DrainUpdates(context);

            Assert.That(
                updates.OfType<ChatShowAgentEventUpdate>().Any(update => update.AgentEvent.SessionId == Session2Id),
                Is.False);
        }

        [Test]
        [Description("Goal: verify background session Unity tool requests are processed while Sessions View is open without rendering background transcript events. Scope: ChatEditorWindowClient tool dispatch and sessions-view event handling only. Boundaries: excludes real SDK transport and successful tool implementation behavior.")]
        public async Task SessionsView_BackgroundToolInvocation_IsExecuted()
        {
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            var harness = new BackgroundToolInvocationHarness();
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            var sessionsResult = await client.ShowSessionsAsync(context, CancellationToken.None);
            Assert.That(sessionsResult.Success, Is.True);

            harness.Stream.Publish(CreateToolInvocationEnvelope(Session2Id, "background-call-1"));

            var transcriptUpdates = new List<ChatShowAgentEventUpdate>();
            for (var attempt = 0; attempt < EventDeliveryMaxAttempts && harness.ToolResults.Count == 0; attempt++)
            {
                await Task.Delay(EventDeliveryPollDelayMs);
                transcriptUpdates.AddRange(client.DrainUpdates(context).OfType<ChatShowAgentEventUpdate>());
            }

            Assert.That(harness.ToolResults.Count, Is.EqualTo(1), "Expected the background Unity tool request to complete.");
            Assert.That(harness.ToolResults[0].SessionId, Is.EqualTo(Session2Id));
            Assert.That(harness.ToolResults[0].CallId, Is.EqualTo("background-call-1"));
            Assert.That(harness.ToolResults[0].IsError, Is.True, "The unknown test tool should return an error result, proving the request was executed.");
            Assert.That(transcriptUpdates, Is.Empty, "Background tool events should not be rendered into the visible transcript.");
        }

        [Test]
        [Description("Goal: verify background session status updates received while Sessions View is open update the unfinished marker list without rendering transcript events. Scope: ChatEditorWindowClient sessions-view background event handling only. Boundaries: excludes UI Toolkit rendering.")]
        public async Task SessionsView_BackgroundSessionStatusMarksSessionUnfinished()
        {
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            var harness = new BackgroundToolInvocationHarness();
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            var sessionsResult = await client.ShowSessionsAsync(context, CancellationToken.None);
            Assert.That(sessionsResult.Success, Is.True);

            harness.Stream.Publish(new AgentServiceEventEnvelope(
                43,
                Session2Id,
                DateTimeOffset.UtcNow,
                "streaming",
                null,
                AgentEventType.SessionStatusChanged,
                "streaming",
                false));

            ChatShowSessionsUpdate refreshedSessions = null;
            for (var attempt = 0; attempt < EventDeliveryMaxAttempts; attempt++)
            {
                await Task.Delay(EventDeliveryPollDelayMs);
                client.DrainUpdates(context);

                var refreshResult = await client.ShowSessionsAsync(context, CancellationToken.None);
                refreshedSessions = refreshResult.Updates.OfType<ChatShowSessionsUpdate>().Single();
                if (refreshedSessions.UnfinishedSessionIds.Contains(Session2Id))
                {
                    break;
                }
            }

            Assert.That(refreshedSessions, Is.Not.Null);
            Assert.That(refreshedSessions.UnfinishedSessionIds, Does.Contain(Session2Id));
        }

        [Test]
        [Description("Goal: verify opening the sessions list while busy marks the active session unfinished and submitting from that view creates a new session instead of aborting the previous one. Scope: ChatEditorWindowClient sessions-view behavior only. Boundaries: excludes UI Toolkit class styling.")]
        public async Task SessionsView_SubmitPromptWhilePreviousSessionBusy_CreatesNewSession()
        {
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            var harness = new SessionsViewSubmitHarness();
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            var firstSubmit = await client.SubmitPromptAsync(context, "keep session one busy", CancellationToken.None);
            Assert.That(firstSubmit.Success, Is.True);

            var sessionsResult = await client.ShowSessionsAsync(context, CancellationToken.None);
            Assert.That(sessionsResult.Success, Is.True);
            var showSessions = sessionsResult.Updates.OfType<ChatShowSessionsUpdate>().Single();
            Assert.That(showSessions.UnfinishedSessionIds, Does.Contain(Session1Id));

            var secondSubmit = await client.SubmitPromptAsync(context, "start session two", CancellationToken.None);
            Assert.That(secondSubmit.Success, Is.True);

            Assert.That(harness.ApiOperations, Is.EqualTo(new[]
            {
                "list",
                $"open:{Session1Id}:ready",
                $"send:{Session1Id}:keep session one busy",
                "list",
                $"create:{CreatedSession2Id}",
                $"send:{CreatedSession2Id}:start session two",
            }));
        }

        [Test]
        [Description("Goal: verify submit and abort are separate client actions while the active session is busy. Scope: ChatEditorWindowClient command routing only. Boundaries: excludes UI Toolkit rendering and actual runtime cancellation.")]
        public async Task BusyActiveSession_SubmitFailsAndAbortCallsAbortEndpoint()
        {
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            var harness = new SessionsViewSubmitHarness();
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            var firstSubmit = await client.SubmitPromptAsync(context, "keep session one busy", CancellationToken.None);
            Assert.That(firstSubmit.Success, Is.True);

            var secondSubmit = await client.SubmitPromptAsync(context, "should not send", CancellationToken.None);
            Assert.That(secondSubmit.Success, Is.False);

            var abortResult = await client.AbortPromptAsync(context, CancellationToken.None);
            Assert.That(abortResult.Success, Is.True);

            Assert.That(harness.ApiOperations, Is.EqualTo(new[]
            {
                "list",
                $"open:{Session1Id}:ready",
                $"send:{Session1Id}:keep session one busy",
                $"abort:{Session1Id}",
            }));
        }

        [Test]
        [Description("Goal: verify submitting from Sessions View with no selected model returns to the transcript and shows the validation error. Scope: ChatEditorWindowClient sessions-view invalid-submit behavior only. Boundaries: excludes UI Toolkit rendering.")]
        public async Task SessionsView_SubmitPromptWithoutSelectedModel_OpensMessagesAndShowsError()
        {
            var settings = CreateTestSettings();
            settings.SetAvailableModels(new[] { new ModelInfoDto("gpt-4o", "GPT-4o") });
            Assert.That(settings.SelectModel(new ModelInfoDto("gpt-4o", "GPT-4o")), Is.True);
            var harness = new SessionsViewSubmitHarness();
            var context = CreateContextFromSettings(settings);
            using var client = harness.CreateClient();

            var sessionsResult = await client.ShowSessionsAsync(context, CancellationToken.None);
            Assert.That(sessionsResult.Success, Is.True);
            Assert.That(client.IsShowingSessions, Is.True);

            settings.ProviderType = UnityCodeAgentProviderType.Byok;
            settings.ByokBaseUrl = "https://provider.example/v1";
            context = CreateContextFromSettings(settings);
            var submitResult = await client.SubmitPromptAsync(context, "start new session", CancellationToken.None);

            Assert.That(submitResult.Success, Is.False);
            Assert.That(client.IsShowingSessions, Is.False);
            Assert.That(submitResult.Updates.OfType<ChatShowMessagesUpdate>().Single(), Is.Not.Null);
            Assert.That(
                submitResult.Updates.OfType<ChatShowErrorUpdate>().Single().Message,
                Is.EqualTo("Select a model in Unity Code Agent settings then retry."));
            Assert.That(harness.ApiOperations, Is.EqualTo(new[] { "list" }));
        }

        [Test]
        [Description("Goal: verify reopening an unfinished session as ready removes the local unfinished marker before returning to the sessions list. Scope: ChatEditorWindowClient marker lifecycle only. Boundaries: excludes UI Toolkit class styling and real SDK status.")]
        public async Task OpenSession_WhenPreviouslyUnfinishedSessionIsReady_RemovesUnfinishedMarker()
        {
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            var harness = new SessionsViewSubmitHarness();
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            var submitResult = await client.SubmitPromptAsync(context, "mark session one busy", CancellationToken.None);
            Assert.That(submitResult.Success, Is.True);

            var firstSessionsResult = await client.ShowSessionsAsync(context, CancellationToken.None);
            var firstShowSessions = firstSessionsResult.Updates.OfType<ChatShowSessionsUpdate>().Single();
            Assert.That(firstShowSessions.UnfinishedSessionIds, Does.Contain(Session1Id));

            var openResult = await client.OpenSessionAsync(context, Session1Id, CancellationToken.None);
            Assert.That(openResult.Success, Is.True);
            Assert.That(openResult.Updates.OfType<ChatSetBusyStateUpdate>().Last().IsBusy, Is.False);

            var secondSessionsResult = await client.ShowSessionsAsync(context, CancellationToken.None);
            var secondShowSessions = secondSessionsResult.Updates.OfType<ChatShowSessionsUpdate>().Single();
            Assert.That(secondShowSessions.UnfinishedSessionIds, Does.Not.Contain(Session1Id));
        }

        [Test]
        [Description("Goal: verify switching back to a session replaces the transcript from OpenSessionAsync history and sets busy state from the opened session response. Scope: ChatEditorWindowClient session switching only. Boundaries: excludes SDK persistence and UI Toolkit rendering.")]
        public async Task OpenSession_ReloadsHistoryAndUsesOpenedSessionBusyState()
        {
            var settings = CreateTestSettings();
            var context = CreateTestContext(settings);
            var harness = new SwitchBackHarness();
            using var client = harness.CreateClient();

            var initResult = await client.InitializeAsync(context, CancellationToken.None);
            Assert.That(initResult.Success, Is.True);

            var openSecond = await client.OpenSessionAsync(context, Session2Id, CancellationToken.None);
            Assert.That(openSecond.Success, Is.True);

            var reopenFirst = await client.OpenSessionAsync(context, Session1Id, CancellationToken.None);
            Assert.That(reopenFirst.Success, Is.True);

            var showMessages = reopenFirst.Updates.OfType<ChatShowMessagesUpdate>().Single();
            Assert.That(showMessages.Messages.Select(message => message.Content), Does.Contain("background response persisted"));
            Assert.That(reopenFirst.Updates.OfType<ChatSetBusyStateUpdate>().Last().IsBusy, Is.True);
        }

        private static bool HasStreamRecoveryEvent(IReadOnlyList<ChatClientUpdate> updates)
            => updates.OfType<ChatShowAgentEventUpdate>().Any(update =>
                update.AgentEvent.Type == AgentEventType.SessionStatusChanged
                && string.Equals(update.AgentEvent.Content, "ready", StringComparison.OrdinalIgnoreCase));

        private static AgentServiceEventEnvelope CreateToolInvocationEnvelope(string sessionId, string callId)
            => new AgentServiceEventEnvelope(
                42,
                sessionId,
                DateTimeOffset.UtcNow,
                "Unity tool invocation requested.",
                null,
                AgentEventType.ToolInvocationRequest,
                $"{{\"CallId\":\"{callId}\",\"SessionId\":\"{sessionId}\",\"ToolName\":\"unknown_test_tool\",\"ArgumentsJson\":\"{{}}\"}}",
                false);

        private sealed class NonActiveEventHarness
        {
            private readonly EndpointManifest _manifest = new EndpointManifest
            {
                Version = 1,
                Port = 5103,
                ProjectRoot = "C:/UnityProject",
                ProjectId = "non-active-event-project",
                ServiceProcessId = 5103,
                UnityProcessId = 1,
                StartedAtUtc = DateTimeOffset.UtcNow,
            };

            public TaskCompletionSource<bool> EventDelivered { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public ChatEditorWindowClient CreateClient()
            {
                var service = new AgentService(
                    new MockServiceBootstrap(),
                    new MockServiceBootstrap(),
                    (_, _) => new StaticOpenApiClient(Session1Id, "ready", Array.Empty<AgentServiceEventEnvelope>()),
                    (_, _) => new OneShotEventStreamClient(
                        new AgentServiceEventEnvelope(20, Session2Id, DateTimeOffset.UtcNow, "hidden background message", null, AgentEventType.AssistantMessage, string.Empty, false),
                        EventDelivered),
                    _ => _manifest);

                return new ChatEditorWindowClient(service);
            }
        }

        private sealed class SessionsViewSubmitHarness
        {
            private readonly EndpointManifest _manifest = new EndpointManifest
            {
                Version = 1,
                Port = 5104,
                ProjectRoot = "C:/UnityProject",
                ProjectId = "sessions-view-submit-project",
                ServiceProcessId = 5104,
                UnityProcessId = 1,
                StartedAtUtc = DateTimeOffset.UtcNow,
            };

            public List<string> ApiOperations { get; } = new List<string>();

            public ChatEditorWindowClient CreateClient()
            {
                var service = new AgentService(
                    new MockServiceBootstrap(),
                    new MockServiceBootstrap(),
                    (_, _) => new SessionsViewSubmitApiClient(ApiOperations),
                    (_, _) => new QuietEventStreamClient(),
                    _ => _manifest);

                return new ChatEditorWindowClient(service);
            }
        }

        private sealed class BackgroundToolInvocationHarness
        {
            private readonly EndpointManifest _manifest = new EndpointManifest
            {
                Version = 1,
                Port = 5106,
                ProjectRoot = "C:/UnityProject",
                ProjectId = "background-tool-project",
                ServiceProcessId = 5106,
                UnityProcessId = 1,
                StartedAtUtc = DateTimeOffset.UtcNow,
            };

            public ManualEventStreamClient Stream { get; } = new ManualEventStreamClient();

            public List<AgentToolInvocationResultDto> ToolResults { get; } = new List<AgentToolInvocationResultDto>();

            public ChatEditorWindowClient CreateClient()
            {
                var service = new AgentService(
                    new MockServiceBootstrap(),
                    new MockServiceBootstrap(),
                    (_, _) => new BackgroundToolApiClient(ToolResults),
                    (_, _) => Stream,
                    _ => _manifest);

                return new ChatEditorWindowClient(service);
            }
        }

        private sealed class BackgroundToolApiClient : IAgentServiceApiClient
        {
            private readonly List<AgentToolInvocationResultDto> _toolResults;

            public BackgroundToolApiClient(List<AgentToolInvocationResultDto> toolResults)
            {
                _toolResults = toolResults;
            }

            public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<SessionSummaryDto>>(new[]
                {
                    new SessionSummaryDto(Session1Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Session 1"),
                    new SessionSummaryDto(Session2Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Session 2"),
                });

            public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ModelInfoDto>>(Array.Empty<ModelInfoDto>());

            public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult(new AgentSessionResponseDto(Session1Id, "ready", Array.Empty<AgentServiceEventEnvelope>()));

            public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));

            public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
            {
                _toolResults.Add(request);
                return Task.CompletedTask;
            }
        }

        private sealed class ManualEventStreamClient : IAgentServiceEventStreamClient
        {
            private TaskCompletionSource<AgentServiceEventEnvelope> _nextEvent =
                new TaskCompletionSource<AgentServiceEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Publish(AgentServiceEventEnvelope envelope)
                => _nextEvent.TrySetResult(envelope);

            public async Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var current = _nextEvent;
                    var completed = await Task.WhenAny(current.Task, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
                    if (completed != current.Task)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    var envelope = await current.Task.ConfigureAwait(false);
                    if (Interlocked.CompareExchange(
                            ref _nextEvent,
                            new TaskCompletionSource<AgentServiceEventEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously),
                            current) == current)
                    {
                        onEvent(envelope);
                    }
                }
            }
        }

        private sealed class SwitchBackHarness
        {
            private readonly EndpointManifest _manifest = new EndpointManifest
            {
                Version = 1,
                Port = 5105,
                ProjectRoot = "C:/UnityProject",
                ProjectId = "switch-back-project",
                ServiceProcessId = 5105,
                UnityProcessId = 1,
                StartedAtUtc = DateTimeOffset.UtcNow,
            };

            public ChatEditorWindowClient CreateClient()
            {
                var apiClient = new SwitchBackApiClient();
                var service = new AgentService(
                    new MockServiceBootstrap(),
                    new MockServiceBootstrap(),
                    (_, _) => apiClient,
                    (_, _) => new QuietEventStreamClient(),
                    _ => _manifest);

                return new ChatEditorWindowClient(service);
            }
        }

        private sealed class StaticOpenApiClient : IAgentServiceApiClient
        {
            private readonly string _sessionId;
            private readonly string _status;
            private readonly IReadOnlyList<AgentServiceEventEnvelope> _messages;

            public StaticOpenApiClient(string sessionId, string status, IReadOnlyList<AgentServiceEventEnvelope> messages)
            {
                _sessionId = sessionId;
                _status = status;
                _messages = messages;
            }

            public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<SessionSummaryDto>>(new[] { new SessionSummaryDto(_sessionId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Session") });

            public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ModelInfoDto>>(Array.Empty<ModelInfoDto>());

            public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult(new AgentSessionResponseDto(_sessionId, _status, _messages));

            public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));

            public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
                => Task.CompletedTask;
        }

        private sealed class SessionsViewSubmitApiClient : IAgentServiceApiClient
        {
            private readonly List<string> _apiOperations;

            public SessionsViewSubmitApiClient(List<string> apiOperations)
            {
                _apiOperations = apiOperations;
            }

            public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
            {
                _apiOperations.Add("list");
                return Task.FromResult<IReadOnlyList<SessionSummaryDto>>(new[]
                {
                    new SessionSummaryDto(Session1Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Session 1"),
                    new SessionSummaryDto(Session2Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Session 2"),
                });
            }

            public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ModelInfoDto>>(Array.Empty<ModelInfoDto>());

            public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"open:{request.SessionId}:ready");
                return Task.FromResult(new AgentSessionResponseDto(Session1Id, "ready", Array.Empty<AgentServiceEventEnvelope>()));
            }

            public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"create:{CreatedSession2Id}");
                return Task.FromResult(new AgentSessionResponseDto(CreatedSession2Id, "ready", Array.Empty<AgentServiceEventEnvelope>()));
            }

            public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"send:{request.SessionId}:{request.Prompt}");
                return Task.CompletedTask;
            }

            public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"abort:{request.SessionId}");
                return Task.CompletedTask;
            }

            public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
                => Task.CompletedTask;
        }

        private sealed class SwitchBackApiClient : IAgentServiceApiClient
        {
            private int _session1OpenCount;

            public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<SessionSummaryDto>>(new[]
                {
                    new SessionSummaryDto(Session1Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Session 1"),
                    new SessionSummaryDto(Session2Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Session 2"),
                });

            public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ModelInfoDto>>(Array.Empty<ModelInfoDto>());

            public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
            {
                if (request.SessionId == Session1Id)
                {
                    _session1OpenCount++;
                    if (_session1OpenCount > 1)
                    {
                        return Task.FromResult(new AgentSessionResponseDto(
                            Session1Id,
                            "streaming",
                            new[]
                            {
                                new AgentServiceEventEnvelope(30, Session1Id, DateTimeOffset.UtcNow, "background response persisted", null, AgentEventType.AssistantMessage, string.Empty, false),
                            }));
                    }
                }

                return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));
            }

            public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));

            public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
                => Task.CompletedTask;

            public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
                => Task.CompletedTask;
        }

        private sealed class OneShotEventStreamClient : IAgentServiceEventStreamClient
        {
            private readonly AgentServiceEventEnvelope _envelope;
            private readonly TaskCompletionSource<bool> _eventDelivered;

            public OneShotEventStreamClient(AgentServiceEventEnvelope envelope, TaskCompletionSource<bool> eventDelivered)
            {
                _envelope = envelope;
                _eventDelivered = eventDelivered;
            }

            public async Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken)
            {
                onEvent(_envelope);
                _eventDelivered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
        }

        private sealed class FailingStreamHarness
        {
            private readonly UnityCodeAgentSettings _settings;
            private readonly EndpointManifest _manifest = new EndpointManifest
            {
                Version = 1,
                Port = 5100,
                ProjectRoot = "C:/UnityProject",
                ProjectId = "failing-stream-project",
                ServiceProcessId = 5100,
                UnityProcessId = 1,
                StartedAtUtc = DateTimeOffset.UtcNow,
            };

            public FailingStreamHarness(UnityCodeAgentSettings settings)
            {
                _settings = settings;
            }

            public List<string> ApiOperations { get; } = new List<string>();

            public List<string> ProgressMessages { get; } = new List<string>();

            public ChatEditorWindowClient CreateClient()
            {
                var service = new AgentService(
                    new MockServiceBootstrap(),
                    new MockServiceBootstrap(),
                    (_, _) => new RecordingApiClient(ApiOperations),
                    (_, _) => new AlwaysFailingEventStreamClient(),
                    _ => _manifest,
                    ProgressMessages.Add);

                return new ChatEditorWindowClient(service);
            }
        }

        private sealed class RecordingApiClient : IAgentServiceApiClient
        {
            private readonly List<string> _apiOperations;

            public RecordingApiClient(List<string> apiOperations)
            {
                _apiOperations = apiOperations;
            }

            public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<SessionSummaryDto>>(new[]
                {
                    new SessionSummaryDto(Session1Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Session 1"),
                });

            public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ModelInfoDto>>(Array.Empty<ModelInfoDto>());

            public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"open:{request.SessionId}");
                return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));
            }

            public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));

            public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"send:{request.Prompt}");
                return Task.CompletedTask;
            }

            public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"abort:{request.SessionId}");
                return Task.CompletedTask;
            }

            public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }

        private sealed class AlwaysFailingEventStreamClient : IAgentServiceEventStreamClient
        {
            public Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken)
                => throw new System.Net.Http.HttpRequestException("stream unavailable");
        }

        private sealed class ReplayedIdleHarness
        {
            private readonly EndpointManifest _manifest = new EndpointManifest
            {
                Version = 1,
                Port = 5102,
                ProjectRoot = "C:/UnityProject",
                ProjectId = "replayed-idle-project",
                ServiceProcessId = 5102,
                UnityProcessId = 1,
                StartedAtUtc = DateTimeOffset.UtcNow,
            };

            private readonly TaskCompletionSource<bool> _promptSent =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public List<string> ApiOperations { get; } = new List<string>();

            public ChatEditorWindowClient CreateClient()
            {
                var service = new AgentService(
                    new MockServiceBootstrap(),
                    new MockServiceBootstrap(),
                    (_, _) => new ReplayedIdleApiClient(ApiOperations, _promptSent),
                    (_, _) => new ReplayedIdleEventStreamClient(_promptSent),
                    _ => _manifest);

                return new ChatEditorWindowClient(service);
            }
        }

        private sealed class ReplayedIdleApiClient : IAgentServiceApiClient
        {
            private readonly List<string> _apiOperations;
            private readonly TaskCompletionSource<bool> _promptSent;

            public ReplayedIdleApiClient(List<string> apiOperations, TaskCompletionSource<bool> promptSent)
            {
                _apiOperations = apiOperations;
                _promptSent = promptSent;
            }

            public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<SessionSummaryDto>>(new[]
                {
                    new SessionSummaryDto(Session1Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Session 1"),
                });

            public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ModelInfoDto>>(Array.Empty<ModelInfoDto>());

            public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"open:{request.SessionId}");
                return Task.FromResult(new AgentSessionResponseDto(
                    request.SessionId,
                    "ready",
                    new[]
                    {
                        CreateIdleEvent(),
                    }));
            }

            public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));

            public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"send:{request.Prompt}");
                _promptSent.TrySetResult(true);
                return Task.CompletedTask;
            }

            public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"abort:{request.SessionId}");
                return Task.CompletedTask;
            }

            public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }

        private sealed class ReplayedIdleEventStreamClient : IAgentServiceEventStreamClient
        {
            private readonly TaskCompletionSource<bool> _promptSent;

            public ReplayedIdleEventStreamClient(TaskCompletionSource<bool> promptSent)
            {
                _promptSent = promptSent;
            }

            public async Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken)
            {
                await _promptSent.Task.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                onEvent(CreateIdleEvent());
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
        }

        private static AgentServiceEventEnvelope CreateIdleEvent()
            => new AgentServiceEventEnvelope(
                10,
                Session1Id,
                DateTimeOffset.UtcNow,
                "idle",
                null,
                AgentEventType.SessionIdle,
                string.Empty,
                false);

        private sealed class ModelChangeHarness
        {
            private readonly UnityCodeAgentSettings _settings;
            private readonly EndpointManifest _manifest = new EndpointManifest
            {
                Version = 1,
                Port = 5101,
                ProjectRoot = "C:/UnityProject",
                ProjectId = "model-change-project",
                ServiceProcessId = 5101,
                UnityProcessId = 1,
                StartedAtUtc = DateTimeOffset.UtcNow,
            };

            public ModelChangeHarness(UnityCodeAgentSettings settings)
            {
                _settings = settings;
            }

            public List<string> ApiOperations { get; } = new List<string>();

            public ChatEditorWindowClient CreateClient()
            {
                var service = new AgentService(
                    new MockServiceBootstrap(),
                    new MockServiceBootstrap(),
                    (_, _) => new ModelRecordingApiClient(ApiOperations),
                    (_, _) => new QuietEventStreamClient(),
                    _ => _manifest);

                return new ChatEditorWindowClient(service);
            }
        }

        private sealed class ModelRecordingApiClient : IAgentServiceApiClient
        {
            private readonly List<string> _apiOperations;

            public ModelRecordingApiClient(List<string> apiOperations)
            {
                _apiOperations = apiOperations;
            }

            public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<SessionSummaryDto>>(new[]
                {
                    new SessionSummaryDto(Session1Id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Session 1"),
                });

            public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<ModelInfoDto>>(Array.Empty<ModelInfoDto>());

            public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add(FormatOperation("open", request.SessionId, request.Provider, request.SkillDirectories, request.DisabledSkills));
                return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));
            }

            public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add(FormatOperation("create", request.SessionId, request.Provider, request.SkillDirectories, request.DisabledSkills));
                return Task.FromResult(new AgentSessionResponseDto(CreatedSession1Id, "ready", Array.Empty<AgentServiceEventEnvelope>()));
            }

            public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"send:{request.SessionId}:{request.Prompt}");
                return Task.CompletedTask;
            }

            public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
            {
                _apiOperations.Add($"abort:{request.SessionId}");
                return Task.CompletedTask;
            }

            public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            private static string FormatOperation(
                string operation,
                string sessionId,
                ProviderConfigDto provider,
                IReadOnlyList<string> skillDirectories,
                IReadOnlyList<string> disabledSkills)
            {
                var text = $"{operation}:{sessionId}:{provider?.Model ?? string.Empty}";
                if (skillDirectories != null && skillDirectories.Count > 0)
                {
                    text += $":skills={string.Join(",", skillDirectories)}";
                }

                if (disabledSkills != null && disabledSkills.Count > 0)
                {
                    text += $":disabled={string.Join(",", disabledSkills)}";
                }

                if (!string.IsNullOrWhiteSpace(provider?.BaseUrl))
                {
                    text += $":base={provider.BaseUrl}";
                }

                return text;
            }
        }

        private sealed class QuietEventStreamClient : IAgentServiceEventStreamClient
        {
            public Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken)
                => Task.Delay(Timeout.Infinite, cancellationToken);
        }

    }
}
