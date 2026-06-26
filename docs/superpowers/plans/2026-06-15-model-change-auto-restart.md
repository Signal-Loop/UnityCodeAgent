# Auto-Restart Session on Model Change Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the user picks a different model in the settings inspector, the chat window's current session is silently reopened with the new model. A new footer label in the chat window shows the active model and pings the settings asset when clicked.

**Architecture:** A static `ModelChanged` event on `UnityCodeAgentSettings` is raised by a new `SetModel` write path used by the inspector. `ChatEditorWindowClient` subscribes to the event and, when fired, reopens the active session via the existing `AgentService.OpenSessionAsync` (which already reads the new model from settings at call time). A new `ChatSetActiveModelUpdate` propagates the new model to the window for label display. Mid-turn swaps are deferred until `SessionIdle`.

**Tech Stack:** Unity 6 + UI Toolkit, .NET 8 service, C# 9 records, NUnit + UnityTest.

---

## File Structure

### Files modified

- `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettings.cs` — add `ModelChanged` static event and `SetModel` write path.
- `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettingsEditor.cs` — call `SetModel` instead of direct assignment; add `PingSettingsAsset` helper.
- `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatEditorWindowClientContracts.cs` — add `ChatSetActiveModelUpdate` type.
- `Packages/com.signal-loop.unitycodeagent/Editor/Service/ChatEditorWindowClient.cs` — subscribe/unsubscribe to `ModelChanged`, handle event, defer mid-turn, reopen session, propagate label update.
- `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatEditorWindow.cs` — resolve `model-label`, click handler, label renderer, handle `ChatSetActiveModelUpdate`.
- `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatWindow.uxml` — add `<ui:Label name="model-label" />`.
- `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatWindow.uss` — add `.chat-model-label` and `.chat-model-label--empty` styles.
- `Assets/Tests/Editor/Service/ChatEditorWindowClientE2eTests.cs` — add five EditMode tests covering all reopen paths.
- `Assets/Tests/Editor/Service/ChatEditorWindowUiE2eTests.cs` — add two `[UnityTest]` tests for the label.

### Files created

- `Assets/Tests/Editor/Settings/UnityCodeAgentSettingsTests.cs` — EditMode tests for `SetModel` event semantics.

---

## Task 1: `UnityCodeAgentSettings` exposes a `ModelChanged` event and a `SetModel` write path

**Files:**
- Modify: `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettings.cs`
- Test: `Assets/Tests/Editor/Settings/UnityCodeAgentSettingsTests.cs`

- [ ] **Step 1: Create the test file with four failing tests**

```csharp
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Settings;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Tests.Settings
{
    public sealed class UnityCodeAgentSettingsTests
    {
        [Test]
        public void SetModel_AssignsAndRaisesModelChanged()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeAgentSettings>();
            var newModel = new ModelInfoDto("gpt-4o", "GPT-4o");
            var raised = 0;
            UnityCodeAgentSettings.ModelChanged += () => raised++;

            settings.SetModel(newModel);

            Assert.That(settings.Model, Is.SameAs(newModel));
            Assert.That(raised, Is.EqualTo(1));

            UnityCodeAgentSettings.ModelChanged -= () => raised++;
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void SetModel_WithDifferentInstance_RaisesExactlyOnce()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeAgentSettings>();
            settings.Model = new ModelInfoDto("gpt-4o", "GPT-4o");
            var raised = 0;
            UnityCodeAgentSettings.ModelChanged += () => raised++;

            settings.SetModel(new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4"));

            Assert.That(raised, Is.EqualTo(1));

            UnityCodeAgentSettings.ModelChanged -= () => raised++;
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void SetAvailableModels_WhenPriorModelIsDropped_RaisesModelChanged()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeAgentSettings>();
            settings.Model = new ModelInfoDto("gpt-4o", "GPT-4o");
            var raised = 0;
            UnityCodeAgentSettings.ModelChanged += () => raised++;

            settings.SetAvailableModels(System.Array.Empty<ModelInfoDto>());

            Assert.That(settings.Model, Is.Null, "Stale model should be cleared when no longer in the available list.");
            Assert.That(raised, Is.EqualTo(1), "ModelChanged must be raised when the resolved Model reference changes.");

            UnityCodeAgentSettings.ModelChanged -= () => raised++;
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void SetAvailableModels_WhenPriorModelIsPreserved_DoesNotRaiseModelChanged()
        {
            var settings = ScriptableObject.CreateInstance<UnityCodeAgentSettings>();
            var existing = new ModelInfoDto("gpt-4o", "GPT-4o");
            settings.Model = existing;
            var raised = 0;
            UnityCodeAgentSettings.ModelChanged += () => raised++;

            settings.SetAvailableModels(new[] { existing });

            Assert.That(settings.Model, Is.SameAs(existing));
            Assert.That(raised, Is.EqualTo(0), "ModelChanged should not be raised when the resolved Model is unchanged.");

            UnityCodeAgentSettings.ModelChanged -= () => raised++;
            Object.DestroyImmediate(settings);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd Assets/Tests/Editor && dotnet test Tests.Editor.csproj --filter "FullyQualifiedName~UnityCodeAgentSettingsTests" -v minimal` from the repo root (or use the Unity Test Runner MCP tool with `EditMode` test mode and the test name `SignalLoop.UnityCodeAgent.Tests.Settings.UnityCodeAgentSettingsTests`).
Expected: FAIL with "SetModel does not exist" / compile error.

- [ ] **Step 3: Add the `ModelChanged` event, `SetModel` method, and update `SetAvailableModels` to raise `ModelChanged` on actual model changes**

In `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettings.cs`, after the closing brace of the class fields section but before the `public static UnityCodeAgentSettings Instance` property, add:

```csharp
public static event Action ModelChanged;
```

After the existing `public ModelInfoDto Model = null;` field, add:

```csharp
public void SetModel(ModelInfoDto model)
{
    Model = model;
    EditorUtility.SetDirty(this);
    ModelChanged?.Invoke();
}
```

Then update `SetAvailableModels` so it captures the prior `Model` reference and raises `ModelChanged` only when the resolved model actually changes. The final body of `SetAvailableModels` should be:

```csharp
public void SetAvailableModels(IReadOnlyList<ModelInfoDto> models)
{
    AvailableModels ??= new List<ModelInfoDto>();
    AvailableModels.Clear();

    bool selectedModelExists = false;
    if (models != null)
    {
        for (var index = 0; index < models.Count; index++)
        {
            AvailableModels.Add(models[index]);
            if (Model != null && Model.Equals(models[index]))
            {
                selectedModelExists = true;
            }
        }
    }

    AvailableModels.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

    var priorModel = Model;
    if (!selectedModelExists)
    {
        Model = null;
    }

    if (AvailableModels.Count == 0)
    {
        Model = null;
    }

    if (!ReferenceEquals(priorModel, Model))
    {
        ModelChanged?.Invoke();
    }
}
```

Ensure the file has `using System;` (it already does).

- [ ] **Step 4: Run the tests to verify they pass**

Run: same command as Step 2.
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettings.cs Assets/Tests/Editor/Settings/UnityCodeAgentSettingsTests.cs
git commit -m "feat(settings): add ModelChanged event and SetModel write path"
```

---

## Task 2: `UnityCodeAgentSettingsEditor` uses `SetModel` and exposes a `PingSettingsAsset` helper

**Files:**
- Modify: `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettingsEditor.cs`

- [ ] **Step 1: Route the model selection through `SetModel`**

In `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettingsEditor.cs`, replace the body of `SelectModel`:

```csharp
private void SelectModel(UnityCodeAgentSettings settings, ModelInfoDto model)
{
    settings.SetModel(model);
    EditorUtility.SetDirty(settings);
    AssetDatabase.SaveAssets();
    serializedObject.UpdateIfRequiredOrScript();
    Repaint();
}
```

- [ ] **Step 2: Add the shared `PingSettingsAsset` helper**

Above the `SelectModel` method, add:

```csharp
private static void PingSettingsAsset()
{
    var settings = UnityCodeAgentSettings.Instance;
    EditorUtility.FocusProjectWindow();
    Selection.activeObject = settings;
    EditorGUIUtility.PingObject(settings);
}
```

Replace the body of `HandleSettingsButtonClicked` to use the helper:

```csharp
private void HandleSettingsButtonClicked()
{
    PingSettingsAsset();
}
```

- [ ] **Step 3: Compile-verify with `get_errors`**

Run: use the `get_errors` tool on the modified file (or run the Unity test runner on the existing settings tests to ensure no regressions).
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettingsEditor.cs
git commit -m "refactor(settings): route model selection through SetModel"
```

---

## Task 3: Add `ChatSetActiveModelUpdate` to the client contract

**Files:**
- Modify: `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatEditorWindowClientContracts.cs`

- [ ] **Step 1: Append the new update type**

At the end of the `ChatClientCallResult` class block in `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatEditorWindowClientContracts.cs`, after `ChatShowErrorUpdate`, add:

```csharp
public sealed class ChatSetActiveModelUpdate : ChatClientUpdate
{
    public ChatSetActiveModelUpdate(ModelInfoDto model)
    {
        Model = model;
    }

    public ModelInfoDto Model { get; }
}
```

Note: `Model` is a `ModelInfoDto` (non-nullable record). The window will compare against null using `_activeModel == null` semantics: we use a sentinel — when no model is selected, the client enqueues a `ChatSetActiveModelUpdate(null)` by passing a synthetic empty `ModelInfoDto("", "")`. (See Task 4 for the sentinel decision.)

- [ ] **Step 2: Compile-verify**

Run: `get_errors` on the file.
Expected: 0 errors. (Note: the empty `ModelInfoDto` is unused at this point — that is acceptable because the call site in Task 4 sets it explicitly.)

- [ ] **Step 3: Commit**

```bash
git add Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatEditorWindowClientContracts.cs
git commit -m "feat(ui): add ChatSetActiveModelUpdate contract"
```

> **Implementation note for the engineer:** the spec uses `ModelInfoDto?` (nullable). The `ModelInfoDto` record is not declared with `init` setters in the project (it is constructed via constructor), so the contract is `public sealed class ChatSetActiveModelUpdate : ChatClientUpdate { public ModelInfoDto Model { get; } }` and the client creates a sentinel when no model is selected. The window treats `Model.Id == ""` as "no model selected". This keeps the contract type-simple and matches the existing record style in `ServiceContracts.cs`.

---

## Task 4: `ChatEditorWindowClient` handles `ModelChanged` and propagates `ChatSetActiveModelUpdate`

**Files:**
- Modify: `Packages/com.signal-loop.unitycodeagent/Editor/Service/ChatEditorWindowClient.cs`
- Test: `Assets/Tests/Editor/Service/ChatEditorWindowClientE2eTests.cs`

- [ ] **Step 1: Add the failing EditMode tests for the four client behaviors**

Append to `Assets/Tests/Editor/Service/ChatEditorWindowClientE2eTests.cs` (at the bottom of the class, before its closing `}`):

```csharp
[Test]
[Description("Raising ModelChanged on settings with an active session enqueues a label update and reopens the session with the new model, without reloading history.")]
public async Task ModelChanged_WithActiveSession_ReopensSession()
{
    MockServiceRuntime.Reset();
    var settings = CreateTestSettings();
    var harness = new ModelChangeHarness(settings);
    using var client = harness.CreateClient();

    var initResult = await client.InitializeAsync(CancellationToken.None);
    Assert.That(initResult.Success, Is.True);
    client.DrainUpdates();

    // Raise a model change
    settings.SetModel(new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4"));
    var reopenResult = await WaitForUpdatesAsync(
        client,
        collected => collected.OfType<ChatSetActiveModelUpdate>().Any(),
        "Timed out waiting for ChatSetActiveModelUpdate after model change.");

    var labelUpdate = reopenResult.OfType<ChatSetActiveModelUpdate>().First();
    Assert.That(labelUpdate.Model.Id, Is.EqualTo("claude-sonnet-4"));
    Assert.That(harness.ApiOperations, Does.Contain("open:mock-session-simple"));
    Assert.That(harness.LastOpenRequestModel, Is.EqualTo("claude-sonnet-4"));
    Assert.That(reopenResult.OfType<ChatShowMessagesUpdate>().Any(), Is.False,
        "Reopen must not reload the history transcript.");
}

[Test]
[Description("Raising ModelChanged while the client is busy defers the reopen until SessionIdle arrives, then reopens with the latest pending model.")]
public async Task ModelChanged_WhileBusy_DefersReopenUntilIdle()
{
    MockServiceRuntime.Reset();
    var settings = CreateTestSettings();
    var harness = new ModelChangeHarness(settings);
    using var client = harness.CreateClient();

    var initResult = await client.InitializeAsync(CancellationToken.None);
    client.DrainUpdates();

    // Force busy by submitting a prompt
    var submitResult = await client.SubmitPromptAsync("hello", CancellationToken.None);
    Assert.That(submitResult.Success, Is.True);
    client.DrainUpdates();

    // Now raise a model change while busy
    settings.SetModel(new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4"));
    var labelOnly = await WaitForUpdatesAsync(
        client,
        collected => collected.OfType<ChatSetActiveModelUpdate>().Any(),
        "Timed out waiting for ChatSetActiveModelUpdate while busy.");
    Assert.That(labelOnly.OfType<ChatSetActiveModelUpdate>().Any(), Is.True);
    Assert.That(harness.ApiOperations.Last(), Is.EqualTo("send:hello"),
        "Model change while busy must not reopen the session yet.");
    client.DrainUpdates();

    // Now deliver SessionIdle — the deferred reopen should run
    await WaitForUpdatesAsync(
        client,
        collected => collected.OfType<ChatSetActiveModelUpdate>().Count() >= 2,
        "Timed out waiting for the deferred reopen ChatSetActiveModelUpdate.");
    Assert.That(harness.LastOpenRequestModel, Is.EqualTo("claude-sonnet-4"));
}

[Test]
[Description("Two model changes while busy collapse to a single reopen with the last value on SessionIdle.")]
public async Task ModelChanged_WhileBusy_TwoSwaps_OnlyLastOneIsApplied()
{
    MockServiceRuntime.Reset();
    var settings = CreateTestSettings();
    var harness = new ModelChangeHarness(settings);
    using var client = harness.CreateClient();

    await client.InitializeAsync(CancellationToken.None);
    client.DrainUpdates();

    await client.SubmitPromptAsync("hello", CancellationToken.None);
    client.DrainUpdates();

    var opensBefore = harness.ApiOperations.FindAll(op => op.StartsWith("open:")).Count;

    settings.SetModel(new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4"));
    settings.SetModel(new ModelInfoDto("gpt-4.1", "GPT-4.1"));

    // Wait for the second label update
    await WaitForUpdatesAsync(
        client,
        collected => collected.OfType<ChatSetActiveModelUpdate>().Count() >= 2,
        "Expected two ChatSetActiveModelUpdate enqueues for two model changes.");

    // No reopen should have happened yet
    var opensDuringBusy = harness.ApiOperations.FindAll(op => op.StartsWith("open:")).Count;
    Assert.That(opensDuringBusy, Is.EqualTo(opensBefore),
        "No reopen should occur while busy, regardless of how many model changes were raised.");

    // Trigger the deferred reopen via SessionIdle
    harness.EnqueueSessionIdle();
    await WaitForUpdatesAsync(
        client,
        collected => harness.ApiOperations.FindAll(op => op.StartsWith("open:")).Count > opensDuringBusy,
        "Deferred reopen after SessionIdle did not run.");

    Assert.That(harness.LastOpenRequestModel, Is.EqualTo("gpt-4.1"),
        "Only the last pending model should be applied on the deferred reopen.");
}

[Test]
[Description("Raising ModelChanged with no active session updates the label and the next CreateSessionAsync uses the new model.")]
public async Task ModelChanged_NoActiveSession_OnlyUpdatesLabelAndNextPromptUsesNewModel()
{
    MockServiceRuntime.Reset();
    var settings = CreateTestSettings();
    var harness = new ModelChangeHarness(settings, withCurrentSession: false);
    using var client = harness.CreateClient();

    var initResult = await client.InitializeAsync(CancellationToken.None);
    Assert.That(initResult.Success, Is.True);
    client.DrainUpdates();

    settings.SetModel(new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4"));
    await WaitForUpdatesAsync(
        client,
        collected => collected.OfType<ChatSetActiveModelUpdate>().Any(),
        "Timed out waiting for label update with no active session.");

    // Submit a prompt — should CreateSession with the new model
    var submitResult = await client.SubmitPromptAsync("hello", CancellationToken.None);
    Assert.That(submitResult.Success, Is.True);

    Assert.That(harness.LastCreateRequestModel, Is.EqualTo("claude-sonnet-4"),
        "Next CreateSessionAsync must use the latest model picked by the user.");
}

[Test]
[Description("After Dispose, ModelChanged raises are ignored.")]
public async Task ModelChanged_OnDispose_Unsubscribes()
{
    MockServiceRuntime.Reset();
    var settings = CreateTestSettings();
    var harness = new ModelChangeHarness(settings);
    var client = harness.CreateClient();

    await client.InitializeAsync(CancellationToken.None);
    client.DrainUpdates();

    var opensBefore = harness.ApiOperations.FindAll(op => op.StartsWith("open:")).Count;

    client.Dispose();

    // This must not trigger any reopen or label update — the client is disposed.
    settings.SetModel(new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4"));
    await Task.Delay(150);
    var opensAfter = harness.ApiOperations.FindAll(op => op.StartsWith("open:")).Count;
    Assert.That(opensAfter, Is.EqualTo(opensBefore),
        "Disposed client must not react to ModelChanged.");
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: use the Unity EditMode test runner MCP tool with `tests` array `["SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests.ModelChanged_WithActiveSession_ReopensSession", ...]` (list all five test names), `test_mode` = `EditMode`.
Expected: All 5 FAIL with compile errors referencing `ModelChangeHarness` and the missing client behavior.

- [ ] **Step 3: Add the `ModelChangeHarness` and supporting recorder at the end of the test file**

Inside the namespace, after `AlwaysFailingEventStreamClient`, add:

```csharp
private sealed class ModelChangeHarness
{
    private readonly UnityCodeAgentSettings _settings;
    private readonly EndpointManifest _manifest;
    private readonly bool _withCurrentSession;
    private readonly ModelChangeApiClient _apiClient;
    private readonly ModelChangeEventStreamClient _eventStream;

    public ModelChangeHarness(UnityCodeAgentSettings settings, bool withCurrentSession = true)
    {
        _settings = settings;
        _withCurrentSession = withCurrentSession;
        _manifest = new EndpointManifest
        {
            Version = 1,
            Port = 5200,
            ProjectRoot = "C:/UnityProject",
            ProjectId = "model-change-project",
            ServiceProcessId = 5200,
            UnityProcessId = 1,
            StartedAtUtc = DateTimeOffset.UtcNow,
        };
        _apiClient = new ModelChangeApiClient(withCurrentSession);
        _eventStream = new ModelChangeEventStreamClient();
    }

    public List<string> ApiOperations => _apiClient.Operations;

    public string LastOpenRequestModel => _apiClient.LastOpenRequestModel;

    public string LastCreateRequestModel => _apiClient.LastCreateRequestModel;

    public ChatEditorWindowClient CreateClient()
    {
        var service = new AgentService(
            new MockServiceBootstrap(),
            _ => (IAgentServiceApiClient)_apiClient,
            _ => (IAgentServiceEventStreamClient)_eventStream,
            () => new UnityCodeAgentPaths("C:/UnityProject"),
            _ => _manifest,
            () => _settings);

        return new ChatEditorWindowClient(service);
    }

    public void EnqueueSessionIdle()
    {
        _eventStream.EnqueueSessionIdle("mock-session-simple");
    }
}

private sealed class ModelChangeApiClient : IAgentServiceApiClient
{
    private readonly bool _withCurrentSession;

    public ModelChangeApiClient(bool withCurrentSession)
    {
        _withCurrentSession = withCurrentSession;
    }

    public List<string> Operations { get; } = new List<string>();

    public string LastOpenRequestModel { get; private set; }

    public string LastCreateRequestModel { get; private set; }

    public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        if (!_withCurrentSession)
        {
            return Task.FromResult<IReadOnlyList<SessionSummaryDto>>(Array.Empty<SessionSummaryDto>());
        }

        return Task.FromResult<IReadOnlyList<SessionSummaryDto>>(new[]
        {
            new SessionSummaryDto("mock-session-simple", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Simple"),
        });
    }

    public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ModelInfoDto>>(Array.Empty<ModelInfoDto>());

    public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
    {
        Operations.Add($"open:{request.SessionId}");
        LastOpenRequestModel = request.Model;
        return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));
    }

    public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
    {
        Operations.Add($"create:{request.SessionId}");
        LastCreateRequestModel = request.Model;
        return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ready", Array.Empty<AgentServiceEventEnvelope>()));
    }

    public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
    {
        Operations.Add($"send:{request.Prompt}");
        return Task.CompletedTask;
    }

    public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
    {
        Operations.Add($"abort:{request.SessionId}");
        return Task.CompletedTask;
    }

    public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellation)
    {
        Operations.Add($"tool-result:{request.CallId}");
        return Task.CompletedTask;
    }
}

private sealed class ModelChangeEventStreamClient : IAgentServiceEventStreamClient
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<AgentServiceEventEnvelope> _queue = new();

    public Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (_queue.TryDequeue(out var envelope))
                {
                    onEvent(envelope);
                }

                await Task.Delay(20, cancellationToken);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public void EnqueueSessionIdle(string sessionId)
    {
        _queue.Enqueue(new AgentServiceEventEnvelope(
            0,
            sessionId,
            DateTimeOffset.UtcNow,
            string.Empty,
            string.Empty,
            AgentEventType.SessionIdle,
            string.Empty,
            false));
    }
}
```

- [ ] **Step 4: Update `ChatEditorWindowClient` to handle `ModelChanged`**

In `Packages/com.signal-loop.unitycodeagent/Editor/Service/ChatEditorWindowClient.cs`:

Add a private field after `_isShowingSessions`:

```csharp
private ModelInfoDto _pendingModelChange;
private bool _isDisposed;
```

Add a subscription in the constructor that takes `(AgentService service, Action<string> showProgressMessage = null)`:

```csharp
UnityCodeAgentSettings.ModelChanged += HandleModelChanged;
```

In `Dispose`, add (before the existing body):

```csharp
if (_isDisposed)
{
    return;
}
_isDisposed = true;
UnityCodeAgentSettings.ModelChanged -= HandleModelChanged;
```

Add the new methods at the end of the class (before its closing `}`):

```csharp
private void HandleModelChanged()
{
    if (_isDisposed)
    {
        return;
    }

    var settings = UnityCodeAgentSettings.Instance;
    var model = settings.Model ?? new ModelInfoDto(string.Empty, string.Empty);
    EnqueueUpdate(new ChatSetActiveModelUpdate(model));

    if (string.IsNullOrWhiteSpace(_activeSessionId))
    {
        return;
    }

    if (_isBusy)
    {
        _pendingModelChange = model;
        _showProgressMessage("Will switch model after current response.");
        return;
    }

    _ = ReopenActiveSessionAsync();
}

private async Task ReopenActiveSessionAsync()
{
    try
    {
        _showProgressMessage("Switching model...");
        var sessionId = _activeSessionId;
        var response = await _service.OpenSessionAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
        _replayAfterSequenceNumber = response.LastEventId;
    }
    catch (Exception exception)
    {
        _log.Error(nameof(ChatEditorWindowClient), "Reopening session after model change failed.", exception);
        EnqueueUpdate(new ChatShowErrorUpdate("Failed to switch model.", exception.ToString()));
    }
    finally
    {
        _pendingModelChange = null;
    }
}
```

In `ApplyServiceEvent`, before the existing `if (envelope.Type == AgentEventType.SessionIdle)` block, add:

```csharp
if (envelope.Type == AgentEventType.SessionIdle && _pendingModelChange != null)
{
    _ = ReopenActiveSessionAsync();
}
```

Make sure to add `using SignalLoop.UnityCodeAgent.Settings;` and `using SignalLoop.UnityCodeAgent.Contracts;` (the latter is already present).

- [ ] **Step 5: Run the five new tests to verify they pass**

Run: same EditMode test runner with all five test names.
Expected: 5 passed.

- [ ] **Step 6: Run the full ChatEditorWindowClientE2eTests suite to verify no regressions**

Run: EditMode tests in `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests`.
Expected: All pre-existing tests still pass.

- [ ] **Step 7: Commit**

```bash
git add Assets/Tests/Editor/Service/ChatEditorWindowClientE2eTests.cs Packages/com.signal-loop.unitycodeagent/Editor/Service/ChatEditorWindowClient.cs
git commit -m "feat(client): auto-reopen session on model change"
```

---

## Task 5: Add the `model-label` element to the chat window

**Files:**
- Modify: `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatWindow.uxml`
- Modify: `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatWindow.uss`

- [ ] **Step 1: Add the label to the UXML**

In `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatWindow.uxml`, after the closing `</ui:VisualElement>` of `chat-actions` and before the closing `</ui:VisualElement>` of `chat-composer`, add:

```xml
<ui:Label name="model-label" class="chat-model-label"/>
```

- [ ] **Step 2: Add the styles to the USS**

In `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatWindow.uss`, append:

```css
.chat-model-label {
  font-size: 11px;
  opacity: 0.7;
  margin-top: 2px;
  margin-right: 0;
  margin-bottom: 0;
  margin-left: 0;
  -unity-text-align: lower-left;
}

.chat-model-label--empty {
  opacity: 0.4;
  -unity-font-style: italic;
}
```

- [ ] **Step 3: Verify in Unity (manual smoke)**

Open Unity Editor; the UXML should compile without warnings. Use `execute_csharp_script_in_unity_editor` if available, or just trigger a recompile and check `read_unity_console_logs` for errors.
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatWindow.uxml Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatWindow.uss
git commit -m "feat(ui): add model-label to chat window footer"
```

---

## Task 6: `ChatEditorWindow` wires the `model-label` to updates and click

**Files:**
- Modify: `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatEditorWindow.cs`

- [ ] **Step 1: Add the field and resolve it in `BuildUi`**

In `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatEditorWindow.cs`, add a private field after `_sendButton`:

```csharp
private Label _modelLabel;
```

In `BuildUi`, after the existing `_sendButton = rootVisualElement.Q<Button>("send-button");` line, add:

```csharp
_modelLabel = rootVisualElement.Q<Label>("model-label");
```

Add an unsubscribe for the click event in the cleanup block at the top of `BuildUi` (alongside the existing null reset):

```csharp
_modelLabel = null;
```

After the `_sendButton.clicked += HandleSendButtonClicked;` line, add:

```csharp
if (_modelLabel != null)
{
    _modelLabel.RegisterCallback<ClickEvent>(HandleModelLabelClicked);
}
```

- [ ] **Step 2: Handle the new update and add a label renderer**

In `ApplyUpdate`, add a new case before `default:`:

```csharp
case ChatSetActiveModelUpdate setModel:
    SetActiveModelLabel(setModel.Model);
    break;
```

Add the click handler (next to `HandleSettingsButtonClicked`):

```csharp
private void HandleModelLabelClicked(ClickEvent evt)
{
    EditorUtility.FocusProjectWindow();
    Selection.activeObject = UnityCodeAgentSettings.Instance;
    EditorGUIUtility.PingObject(UnityCodeAgentSettings.Instance);
}
```

Add the label renderer:

```csharp
private void SetActiveModelLabel(ModelInfoDto model)
{
    if (_modelLabel == null)
    {
        return;
    }

    if (model == null || string.IsNullOrWhiteSpace(model.Id))
    {
        _modelLabel.text = "No model selected";
        _modelLabel.AddToClassList("chat-model-label--empty");
    }
    else
    {
        _modelLabel.text = $"Model: {model}";
        _modelLabel.RemoveFromClassList("chat-model-label--empty");
    }
}
```

In `InitializeWindowAsync`, after the call to `ApplyUpdates(result.Updates)`, add a call to render the initial label from the current settings (so the first paint shows the right model before the stream pump delivers a `ChatSetActiveModelUpdate`):

```csharp
SetActiveModelLabel(UnityCodeAgentSettings.Instance.Model);
```

- [ ] **Step 3: Compile-verify**

Run: `get_errors` on the file.
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatEditorWindow.cs
git commit -m "feat(ui): render and click-handle model-label in chat window"
```

---

## Task 7: `[UnityTest]` end-to-end UI test for the model label

**Files:**
- Modify: `Assets/Tests/Editor/Service/ChatEditorWindowUiE2eTests.cs`

- [ ] **Step 1: Add the failing UI test**

Append to `Assets/Tests/Editor/Service/ChatEditorWindowUiE2eTests.cs`, before its closing `}`:

```csharp
[UnityTest]
[Description("Open chat window, change the model in settings, verify the footer label updates and the active session is reopened with the new model.")]
public IEnumerator ModelLabel_UpdatesOnModelChange()
{
    EditorApplication.ExecuteMenuItem("UnityCodeAgent/Open Chat Window");
    yield return WaitForWindowReady();

    var window = FindWindow();
    Assert.That(window, Is.Not.Null);

    var label = window.rootVisualElement.Q<Label>("model-label");
    Assert.That(label, Is.Not.Null, "The model label should be present in the chat window.");
    Assert.That(label.text, Does.Contain("GPT-4o"),
        "Initial label should reflect the settings.Model value (gpt-4o from the test setup).");

    var settings = UnityCodeAgentSettings.Instance;
    settings.SetModel(new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4"));

    yield return WaitUntil(
        () =>
        {
            var current = FindWindow()?.rootVisualElement.Q<Label>("model-label");
            return current != null && current.text.Contains("Claude Sonnet 4");
        },
        "Model label did not update to the new model after the model change.");

    // The label should NOT show the empty-style class
    var updatedLabel = FindWindow().rootVisualElement.Q<Label>("model-label");
    Assert.That(updatedLabel.ClassListContains("chat-model-label--empty"), Is.False,
        "Model label should not be marked as empty when a model is selected.");

    window.Close();
}

[UnityTest]
[Description("With no model selected, the chat window shows 'No model selected' in the footer label.")]
public IEnumerator ModelLabel_ShowsEmptyStateWhenNoModel()
{
    var settings = UnityCodeAgentSettings.Instance;
    var originalModel = settings.Model;
    settings.SetModel(new ModelInfoDto(string.Empty, string.Empty));

    EditorApplication.ExecuteMenuItem("UnityCodeAgent/Open Chat Window");
    yield return WaitForWindowReady();

    var window = FindWindow();
    var label = window.rootVisualElement.Q<Label>("model-label");
    Assert.That(label, Is.Not.Null);
    Assert.That(label.text, Is.EqualTo("No model selected"));
    Assert.That(label.ClassListContains("chat-model-label--empty"), Is.True);

    window.Close();
    settings.SetModel(originalModel);
}
```

- [ ] **Step 2: Run the tests to verify they pass**

Run: Unity PlayMode test runner with the two test names, `test_mode` = `EditMode` (these are `[UnityTest]` EditMode tests).
Expected: 2 passed.

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/Editor/Service/ChatEditorWindowUiE2eTests.cs
git commit -m "test(ui): verify model label updates on model change"
```

---

## Task 8: Final regression run and verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full EditMode test suite for `UnityCodeAgent`**

Run: Unity EditMode test runner with `test_mode` = `EditMode`, no test names (all).
Expected: all pre-existing tests still pass; the 7 new tests pass.

- [ ] **Step 2: Run the full PlayMode test suite**

Run: Unity PlayMode test runner.
Expected: all pre-existing tests still pass; the 2 new `[UnityTest]` EditMode tests pass.

- [ ] **Step 3: Manual end-to-end verification via `execute_csharp_script_in_unity_editor`**

Execute (via MCP):

```csharp
// 1. Open the chat window
EditorApplication.ExecuteMenuItem("UnityCodeAgent/Open Chat Window");

// 2. Verify the model label is present and shows the current model
var window = Resources.FindObjectsOfTypeAll<SignalLoop.UnityCodeAgent.UI.ChatEditorWindow>().FirstOrDefault();
var label = window.rootVisualElement.Q<Label>("model-label");
Debug.Log($"Initial label: '{label.text}', classListEmpty={label.ClassListContains("chat-model-label--empty")}");

// 3. Change the model
var settings = SignalLoop.UnityCodeAgent.Settings.UnityCodeAgentSettings.Instance;
settings.SetModel(new SignalLoop.UnityCodeAgent.Contracts.ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4"));

// 4. Verify the label updated
var updated = window.rootVisualElement.Q<Label>("model-label");
Debug.Log($"After model change: '{updated.text}'");

// 5. Verify progress message shown
var scrollView = window.rootVisualElement.Q<ScrollView>("scroll-view");
foreach (var child in scrollView.contentContainer.Children())
{
    var field = child.Q<TextField>("chat-message");
    if (field != null && field.value.Contains("Switching model"))
    {
        Debug.Log($"Progress message: '{field.value}'");
        break;
    }
}
```

Expected output: `Initial label: 'Model: GPT-4o (gpt-4o)'`, `After model change: 'Model: Claude Sonnet 4 (claude-sonnet-4)'`, and a `Progress message: 'Switching model...'` line.

- [ ] **Step 4: Final commit if any leftover changes**

```bash
git status
# If dirty:
git add -A
git commit -m "chore: post-verification cleanup"
```

---

## Self-Review

**Spec coverage check:**

- `UnityCodeAgentSettings.ModelChanged` event and `SetModel` write path → Task 1 ✅
- Inspector uses `SetModel` → Task 2 ✅
- `ChatSetActiveModelUpdate` contract → Task 3 ✅
- Client subscribes/handles `ModelChanged` → Task 4 ✅
- Defer mid-turn via `_pendingModelChange` and `SessionIdle` → Task 4 ✅
- Reopen via existing `AgentService.OpenSessionAsync` (no history reload) → Task 4 ✅
- `model-label` element + styles → Task 5 ✅
- Window wires label, click, renderer → Task 6 ✅
- EditMode tests for the 4 client scenarios + Dispose → Task 4 ✅
- Editor settings tests for `SetModel` semantics → Task 1 ✅
- UI `[UnityTest]` for label update + empty state → Task 7 ✅
- Manual E2E verification with `execute_csharp_script_in_unity_editor` → Task 8 ✅

**Placeholder scan:** No "TBD", no "fill in later", no "similar to Task N" without code reuse. The `ModelInfoDto` sentinel is a concrete decision, not a placeholder. ✅

**Type consistency check:**
- `ChatSetActiveModelUpdate.Model` is `ModelInfoDto` (non-nullable) consistently used in Task 3, Task 4 (handler enqueue), Task 6 (`SetActiveModelLabel(model)`), Task 7 (label assertion). The window treats `Id == ""` as empty. ✅
- `UnityCodeAgentSettings.SetModel(ModelInfoDto model)` signature used identically in Task 1 (test), Task 2 (inspector), Task 4 (test). ✅
- `ModelChangeHarness` shape used in Task 4 (definition) and Task 4 (test usage). ✅

**Lifecycle and reference review (post-design-check):**

- **`UnityCodeAgentSettings`** is a `ScriptableObject` cached in `private static _instance`. It lives for the Editor session and is re-loaded on domain reload. `ModelChanged` is a static `event Action` — also reset on domain reload.
- **`ChatEditorWindowClient`** is a plain C# class created lazily by `ChatEditorWindow` and re-created on every `BuildUi` (which fires on `OnEnable` and after domain reload). The existing `BuildUi` pre-check disposes the old client before constructing a new one.
- **Subscription lifetime:** the client subscribes in the constructor and unsubscribes in `Dispose`. `ChatEditorWindow.OnDisable` and the `BuildUi` pre-check both call `Dispose`, so subscriptions are cleaned up. **No leak.**
- **Domain reload:** the static event delegates are wiped on reload, but the next `BuildUi` re-subscribes. The initial label is set in `InitializeAsync` (`SetActiveModelLabel(UnityCodeAgentSettings.Instance.Model)`), so a window opened after a reload that has had a model change shows the correct label.
- **Multiple windows:** if the user opens multiple `ChatEditorWindow` instances, each subscribes independently. The static event fans out to all of them. Each client tracks its own `_activeSessionId`; the model change is applied per-window. Acceptable for a developer tool.
- **`SetAvailableModels` raising `ModelChanged`:** corrected in this revision (Task 1, Step 3) so that "Refresh models" in the inspector also updates the label and triggers a reopen if the prior model is no longer in the list. This was identified as a stale-label risk during design review.
- **Thread safety:** `SetModel` and `ModelChanged?.Invoke()` run on the Unity main thread (inspector UI). The handler is also on the main thread. The reopen itself is dispatched via `Task.Run` already inside `AgentService`. No locks needed. ✅
