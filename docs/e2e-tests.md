# Mock Agent Service — E2E Test Scenario

End-to-end testing of the Unity editor integration using mocked agent service responses.
When `MockService` is enabled in `UnityCodeAgentSettings`, the `AgentService` swaps in mock
implementations of `IServiceBootstrap`, `IAgentServiceApiClient`, and `IAgentServiceEventStreamClient`.
No real Copilot service is required.

## Architecture

### Mock File Structure

```
Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/
    MockServiceState.cs              — Shared coordinator (event queue, cancellation, sequence tracking)
    MockSessionData.cs               — 5 predefined sessions with summaries, message histories, response sequences
    MockServiceBootstrap.cs          — Fake manifest write with startup delay
    MockAgentServiceApiClient.cs     — Session CRUD, prompt → event queue
    MockAgentServiceEventStreamClient.cs — SSE-like event delivery from queue
```

### Wiring

`AgentService` default constructor checks `UnityCodeAgentSettings.Instance.MockService` and
conditionally injects mock factories. All downstream consumers (menu commands,
`ChatEditorWindowClient`, `ChatEditorWindow`) get mocks automatically with zero code changes.

### Design Principles

- **Input-agnostic:** The selected session determines what responses are returned, regardless
  of prompt text. Behavior is deterministic and testable.
- **No user input mocking in responses:** Response sequences never include `UserMessage` events.
  User messages come only from actual user input. History messages may include `UserMessage` events
  to show a realistic conversation transcript when a session is opened. Prompts trigger only
  assistant/tool/mcp responses.
- **Prompt-driven:** The mock waits for each user prompt before delivering the next response
  sequence. No responses are delivered without a preceding user action.
- **Happy path only:** No error injection, latency simulation, or failure modes.
- **Event-driven:** Mock API client enqueues events onto `MockServiceState.PendingEvents`;
  mock event stream client dequeues and delivers them with a 50ms delay per event.

---

## The 5 Mock Sessions

| # | Session ID | Summary | History Messages | Response Sequences | Response Event Types |
|---|-----------|---------|-----------------|-------------------|----------------------|
| 1 | `mock-session-simple` | Simple code question — how to get player position | UserMessage + AssistantMessage + SessionIdle | 1 | AssistantMessage |
| 2 | `mock-session-codegen` | Code generation — rotating cube script with tool call | UserMessage + ReasoningDelta + Tool + AssistantMessage + SessionIdle | 1 | AssistantDelta, Tool, AssistantMessage |
| 3 | `mock-session-mcp` | MCP scene query — list GameObjects via unity-code-mc | UserMessage + Tool + Mcp + AssistantMessage + SessionIdle | 1 | Tool, Mcp, AssistantMessage |
| 4 | `mock-session-debug` | Multi-turn debug — character falls through floor | UserMessage + AssistantMessage | 2 | AssistantDelta, AssistantMessage (seq 1); Tool, Mcp, AssistantMessage (seq 2) |
| 5 | `mock-session-search` | Asset search — find all textures in project | UserMessage + Tool + AssistantMessage + SessionIdle | 1 | Tool, AssistantDelta, AssistantMessage |

---

## Pre-requisites

1. Unity Editor is open with the UnityCodeCopilot project loaded.
2. The UnityCodeMCP server is connected (for MCP tool event types to render, though not strictly required).
3. The `execute_csharp_script_in_unity_editor` tool is available.

---

## E2E Test Procedure

All steps use `execute_csharp_script_in_unity_editor`. Run each step sequentially.

---

### Step 0: Enable Mock Mode

```csharp
using UnityEditor;
using SignalLoop.UnityCodeAgent.Settings;

var settings = UnityCodeAgentSettings.Instance;
settings.MockService = true;
EditorUtility.SetDirty(settings);
AssetDatabase.SaveAssets();
Debug.Log($"MockService enabled: {settings.MockService}");
```

**Expected:** Log prints `MockService enabled: True`.

---

### Step 1: Clean State

Close any open chat window and clear the console.

```csharp
using UnityEditor;
using SignalLoop.UnityCodeAgent.UI;

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
if (window != null) window.Close();

var logEntries = System.Type.GetType("UnityEditor.LogEntries, UnityEditor");
var clearMethod = logEntries?.GetMethod("Clear", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
clearMethod?.Invoke(null, null);

Debug.Log("Clean state ready.");
```

**Expected:** Log prints `Clean state ready.`. No chat window open.

---

### Step 2: Open Chat Window via Menu Command

```csharp
using UnityEditor;
using SignalLoop.UnityCodeAgent.UI;

EditorApplication.ExecuteMenuItem("UnityCodeAgent/Open Chat Window");

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
Debug.Log(window != null ? "Chat window opened." : "FAIL: Chat window not found.");
```

**Expected:** Log prints `Chat window opened.`. The window title is "Agent Chat".

---

### Step 3: Verify UI Elements

```csharp
using UnityEditor;
using UnityEngine.UIElements;
using SignalLoop.UnityCodeAgent.UI;

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
var root = window.rootVisualElement;

var scrollView = root.Q<ScrollView>("scroll-view");
var userInput = root.Q<TextField>("user-input");
var sendButton = root.Q<Button>("send-button");
var sessionsButton = root.Q<Button>("sessions-button");

Debug.Log($"scroll-view: {(scrollView != null ? "FOUND" : "MISSING")}");
Debug.Log($"user-input: {(userInput != null ? "FOUND" : "MISSING")}");
Debug.Log($"send-button: {(sendButton != null ? "FOUND" : "MISSING")}");
Debug.Log($"sessions-button: {(sessionsButton != null ? "FOUND" : "MISSING")}");
```

**Expected:** All four elements report `FOUND`.

---

### Step 4: Verify History Loads with Correct Order

After the window opens, the mock service bootstraps and loads the **mock-session-simple**
history. The history should include a user message ("How do I get the player's position in Unity?")
followed by the assistant response. Wait for async initialization, then verify exact content order.

```csharp
using UnityEditor;
using UnityEngine.UIElements;
using System.Threading;
using SignalLoop.UnityCodeAgent.UI;

Thread.Sleep(500);

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
var scrollView = window.rootVisualElement.Q<ScrollView>("scroll-view");

int childCount = scrollView?.contentContainer.childCount ?? -1;
Debug.Log($"[STEP 4] History childCount: {childCount}");

// Collect all message contents in order
var messages = new System.Collections.Generic.List<string>();
foreach (var child in scrollView.contentContainer.Children())
{
    var field = child.Q<TextField>("chat-message");
    messages.Add(field?.value ?? "(missing)");
}

// Dump to verify
for (int i = 0; i < messages.Count; i++)
{
    var preview = messages[i].Length > 80 ? messages[i].Substring(0, 80) + "..." : messages[i];
    Debug.Log($"  [{i}] {preview}");
}

// Verify exact order: [0] user question, [1] assistant answer
bool pass = messages.Count == 2
    && messages[0].Contains("How do I get the player")
    && messages[1].Contains("transform.position");

Debug.Log(pass
    ? "PASS: History order correct — UserMessage → AssistantMessage"
    : $"FAIL: Unexpected history. Expected [UserMsg, AsstMsg], got {messages.Count} messages");
```

**Expected:**
```
[STEP 4] History childCount: 2
  [0] How do I get the player's position in Unity?
  [1] You can get the player's world-space position using `transform.position`...
PASS: History order correct — UserMessage → AssistantMessage
```

---

### Step 5: Submit First Prompt via Send Button

Type a message into the input field and click the send button using a UI Toolkit
`NavigationSubmitEvent` (simulates pressing Enter or clicking the button).

```csharp
using UnityEditor;
using UnityEngine.UIElements;
using SignalLoop.UnityCodeAgent.UI;

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
var userInput = window.rootVisualElement.Q<TextField>("user-input");
var sendButton = window.rootVisualElement.Q<Button>("send-button");

userInput.value = "What is transform.position?";

var navSubmit = NavigationSubmitEvent.GetPooled();
navSubmit.target = sendButton;
sendButton.SendEvent(navSubmit);

Debug.Log("Prompt 1 submitted via button: 'What is transform.position?'");
```

**Expected:** No errors. Console logs show:
- `ChatEditorWindowClient: Submitting prompt sessionId=... promptLength=27`
- `MockAgentServiceApiClient: SendPromptAsync sessionId=... (prompt ignored in mock mode)`
- `MockServiceState: EnqueueEvent type=AssistantMessage`

---

### Step 6: Wait and Verify First Response Order

The mock event stream delivers events asynchronously. Wait ~1.5s for delivery,
then verify the complete message order.

```csharp
using UnityEditor;
using UnityEngine.UIElements;
using System.Threading;
using SignalLoop.UnityCodeAgent.UI;

Thread.Sleep(1500);

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
var scrollView = window.rootVisualElement.Q<ScrollView>("scroll-view");

int childCount = scrollView?.contentContainer.childCount ?? -1;
Debug.Log($"[STEP 6] childCount after prompt 1: {childCount}");

var messages = new System.Collections.Generic.List<string>();
foreach (var child in scrollView.contentContainer.Children())
{
    var field = child.Q<TextField>("chat-message");
    messages.Add(field?.value ?? "(missing)");
}

for (int i = 0; i < messages.Count; i++)
{
    var preview = messages[i].Length > 80 ? messages[i].Substring(0, 80) + "..." : messages[i];
    Debug.Log($"  [{i}] {preview}");
}

// Verify: [0]=history user, [1]=history assistant, [2]=new user, [3]=new assistant
bool pass = messages.Count == 4
    && messages[0].Contains("How do I get the player")
    && messages[1].Contains("transform.position")
    && messages[2].Contains("What is transform.position?")
    && messages[3].Contains("To get the player's position");  // mock response

Debug.Log(pass
    ? "PASS: Full order — HistoryUser, HistoryAsst, Prompt1User, Prompt1Asst"
    : $"FAIL: Expected [HistoryUser,HistoryAsst,Prompt1User,Prompt1Asst], got {messages.Count} messages");
```

**Expected:**
```
[STEP 6] childCount after prompt 1: 4
  [0] How do I get the player's position in Unity?
  [1] You can get the player's world-space position using `transform.position`...
  [2] What is transform.position?
  [3] To get the player's position, attach a reference to the player's Transform...
PASS: Full order — HistoryUser, HistoryAsst, Prompt1User, Prompt1Asst
```

---

### Step 7: Submit Second Prompt via Send Button

Submit a second prompt to verify response cycling (unique `StreamKey` per turn).

```csharp
using UnityEditor;
using UnityEngine.UIElements;
using SignalLoop.UnityCodeAgent.UI;

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
var userInput = window.rootVisualElement.Q<TextField>("user-input");
var sendButton = window.rootVisualElement.Q<Button>("send-button");

userInput.value = "And what about localPosition?";

var navSubmit = NavigationSubmitEvent.GetPooled();
navSubmit.target = sendButton;
sendButton.SendEvent(navSubmit);

Debug.Log("Prompt 2 submitted via button: 'And what about localPosition?'");
```

**Expected:** No errors. `Prompt 2 submitted` logged.

---

### Step 8: Wait and Verify Second Response Order

Wait for delivery, then verify the complete 6-message transcript order.

```csharp
using UnityEditor;
using UnityEngine.UIElements;
using System.Threading;
using SignalLoop.UnityCodeAgent.UI;

Thread.Sleep(1500);

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
var scrollView = window.rootVisualElement.Q<ScrollView>("scroll-view");

int childCount = scrollView?.contentContainer.childCount ?? -1;
Debug.Log($"[STEP 8] childCount after prompt 2: {childCount}");

var messages = new System.Collections.Generic.List<string>();
foreach (var child in scrollView.contentContainer.Children())
{
    var field = child.Q<TextField>("chat-message");
    messages.Add(field?.value ?? "(missing)");
}

for (int i = 0; i < messages.Count; i++)
{
    var preview = messages[i].Length > 80 ? messages[i].Substring(0, 80) + "..." : messages[i];
    Debug.Log($"  [{i}] {preview}");
}

// Verify 6 messages: full conversation with 2 turns
bool pass = messages.Count == 6
    && messages[0].Contains("How do I get the player")       // history user
    && messages[1].Contains("transform.position")               // history assistant
    && messages[2].Contains("What is transform.position?")      // prompt 1 user
    && messages[3].Contains("To get the player's position")     // prompt 1 response
    && messages[4].Contains("And what about localPosition")     // prompt 2 user
    && messages[5].Contains("To get the player's position");    // prompt 2 response (same content, different StreamKey)

Debug.Log(pass
    ? "PASS: Full 2-turn conversation — [HistUser,HistAsst,P1User,P1Asst,P2User,P2Asst]"
    : $"FAIL: Expected 6 messages in order [HUser,HAsst,P1User,P1Asst,P2User,P2Asst]");
```

**Expected:**
```
[STEP 8] childCount after prompt 2: 6
  [0] How do I get the player's position in Unity?
  [1] You can get the player's world-space position using `transform.position`...
  [2] What is transform.position?
  [3] To get the player's position, attach a reference to the player's Transform...
  [4] And what about localPosition?
  [5] To get the player's position, attach a reference to the player's Transform...
PASS: Full 2-turn conversation — [HistUser,HistAsst,P1User,P1Asst,P2User,P2Asst]
```

> **Note:** Messages [3] and [5] have identical content because the mock session has only
> 1 response sequence and cycles through it (modulo). This is expected — the important
> verification is that they are **separate** UI elements (not overwriting each other),
> confirmed by `childCount=6`.

---

### Step 9: Verify Console Logs Show Event Delivery

Use the `read_unity_console_logs` tool and confirm the event delivery pipeline:

```
read_unity_console_logs(max_entries: 50)
```

**Expected console log entries (in order):**
- `MockAgentServiceApiClient: GetSessionsAsync returning 5 mock sessions`
- `MockAgentServiceApiClient: OpenSessionAsync sessionId=mock-session-simple`
- `ChatEditorWindowClient: Loaded current session history sessionId=mock-session-simple messages=3`
- `MockAgentServiceApiClient: SendPromptAsync sessionId=mock-session-simple (prompt ignored in mock mode)`
- `MockServiceState: EnqueueEvent type=AssistantMessage`
- `MockAgentServiceEventStreamClient: Delivering event seq=... type=AssistantMessage`
- `ChatEditorWindowClient: Applying queued service event eventType=AssistantMessage`
- No errors or exceptions.

---

### Step 10: Switch Sessions

Open the sessions list and switch to a different mock session (e.g., codegen).

```csharp
using UnityEditor;
using UnityEngine.UIElements;
using System.Threading;
using SignalLoop.UnityCodeAgent.UI;

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);

// Click the sessions button to open the session list
var sessionsButton = window.rootVisualElement.Q<Button>("sessions-button");
var navSubmit = NavigationSubmitEvent.GetPooled();
navSubmit.target = sessionsButton;
sessionsButton.SendEvent(navSubmit);
Debug.Log("Sessions button clicked.");

Thread.Sleep(500);

// Verify sessions list is visible with 5 entries
var sessionsScrollView = window.rootVisualElement.Q<ScrollView>("sessions-scroll-view");
Debug.Log($"sessions-scroll-view visible: {(sessionsScrollView?.style.display.value == DisplayStyle.Flex ? "YES" : "NO")}");
Debug.Log($"sessions childCount: {sessionsScrollView?.contentContainer.childCount ?? -1}");

int idx = 0;
foreach (var child in sessionsScrollView.contentContainer.Children())
{
    var label = child.Q<Label>("session-name");
    Debug.Log($"  [{idx}] name={child.name} summary=\"{label?.text ?? "n/a"}\"");
    idx++;
}
```

**Expected:**
- `sessions-scroll-view visible: YES`
- `sessions childCount: 5`
- 5 entries listed with their summaries

---

### Step 10b: Open a Different Session

```csharp
using UnityEditor;
using UnityEngine.UIElements;
using System.Threading;
using SignalLoop.UnityCodeAgent.UI;

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
var sessionsScrollView = window.rootVisualElement.Q<ScrollView>("sessions-scroll-view");

// Find the codegen session entry
VisualElement codegenEntry = null;
foreach (var child in sessionsScrollView.contentContainer.Children())
{
    if (child.name == "session-entry:mock-session-codegen")
    {
        codegenEntry = child;
        break;
    }
}

if (codegenEntry == null)
{
    Debug.LogError("FAIL: codegen session entry not found");
    return;
}

// Click the entry via ClickEvent
var clickEvent = ClickEvent.GetPooled();
clickEvent.target = codegenEntry;
codegenEntry.SendEvent(clickEvent);
Debug.Log($"Codegen session opened.");

Thread.Sleep(500);

// Verify new session history loaded
var mainScrollView = window.rootVisualElement.Q<ScrollView>("scroll-view");
Debug.Log($"main scroll-view visible: {(mainScrollView?.style.display.value == DisplayStyle.Flex ? "YES" : "NO")}");
Debug.Log($"codegen history childCount: {mainScrollView?.contentContainer.childCount ?? -1}");

idx = 0;
foreach (var child in mainScrollView.contentContainer.Children())
{
    var field = child.Q<TextField>("chat-message");
    var preview = field?.value?.Length > 80 ? field.value.Substring(0, 80) + "..." : field?.value ?? "(no field)";
    Debug.Log($"  [{idx}] {preview}");
    idx++;
}
```

**Expected:**
- `main scroll-view visible: YES`
- codegen history shows 3 messages: "Create a rotating cube script", the assistant response,
  and a tool event

---

### Step 10c: Submit a Prompt in the Codegen Session

```csharp
using UnityEditor;
using UnityEngine.UIElements;
using System.Threading;
using SignalLoop.UnityCodeAgent.UI;

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
var userInput = window.rootVisualElement.Q<TextField>("user-input");
var sendButton = window.rootVisualElement.Q<Button>("send-button");

userInput.value = "Make it rotate faster";
var navSubmit = NavigationSubmitEvent.GetPooled();
navSubmit.target = sendButton;
sendButton.SendEvent(navSubmit);
Debug.Log("Prompt submitted in codegen session.");

Thread.Sleep(1500);

var scrollView = window.rootVisualElement.Q<ScrollView>("scroll-view");
Debug.Log($"childCount after prompt: {scrollView?.contentContainer.childCount ?? -1}");

idx = 0;
foreach (var child in scrollView.contentContainer.Children())
{
    var field = child.Q<TextField>("chat-message");
    var preview = field?.value?.Length > 80 ? field.value.Substring(0, 80) + "..." : field?.value ?? "(no field)";
    Debug.Log($"  [{idx}] {preview}");
    idx++;
}
```

**Expected:** `childCount=6` with messages in this order:
- `[0]` = codegen history user: "Create a rotating cube script"
- `[1]` = codegen history assistant response
- `[2]` = codegen history tool event
- `[3]` = new user input: "Make it rotate faster"
- `[4]` = codegen response assistant message (from response sequence)
- `[5]` = codegen response tool event (from response sequence)

---

### Step 11: Disable Mock Mode

```csharp
using UnityEditor;
using SignalLoop.UnityCodeAgent.Settings;

var settings = UnityCodeAgentSettings.Instance;
settings.MockService = false;
EditorUtility.SetDirty(settings);
AssetDatabase.SaveAssets();
Debug.Log($"MockService disabled: {settings.MockService}");
```

**Expected:** `MockService disabled: False`.

---

### Step 12: Clean Up

```csharp
using UnityEditor;
using SignalLoop.UnityCodeAgent.UI;

var window = EditorWindow.GetWindow<ChatEditorWindow>(false, "Agent Chat", false);
if (window != null) window.Close();
Debug.Log("Cleanup complete.");
```

**Expected:** Chat window closed. `Cleanup complete.` logged.

---

## Known Issues Found and Fixed

### 1. Null StreamKey in ChatEditorWindow (FIXED)

Mock events have `StreamKey = null`. The `UpsertStreamedMessage` and `AppendToStreamedMessage`
methods in `ChatEditorWindow` call `Dictionary.TryGetValue(key, ...)` which throws
`ArgumentNullException` when key is null.

**Fix:** Added `key ??= string.Empty;` at the top of both methods.

### 2. Response Sequences Not Pre-registered in OpenSessionAsync (FIXED)

`OpenSessionAsync` did not call `MockServiceState.RegisterResponseSequences()`, so the first
prompt after opening a session would fail to find response sequences.

**Fix:** Added sequence registration in `MockAgentServiceApiClient.OpenSessionAsync`.

### 3. Missing Using Directive in MockServiceBootstrap (FIXED)

`CS0246: 'UnityCodeAgentPaths' could not be found` — the file was missing
`using SignalLoop.UnityCodeAgent.Infrastructure;`.

**Fix:** Added the missing using directive.

### 4. User Messages Missing from History (FIXED, then REVERTED)

Initially user messages were in history, then they were removed, then restored. The final
design: `UserMessage` events appear in session history (the `GetMessages()` methods) so
opening a session shows a realistic transcript, but response sequences (`GetResponseSequences()`)
never contain `UserMessage` — those come only from actual user input in the chat window.

### 5. All Responses Overwriting Same UI Element (FIXED)

`AgentService` creates a new `MockAgentServiceApiClient` per method call via its factory. The
`_sequenceIndices` and `_responseSequences` dictionaries were instance-level, so the sequence
index reset to 0 on each call. This caused all responses to share the same `StreamKey`, and
`UpsertStreamedMessage` would update the existing element instead of creating a new one.

**Fix:** Moved `_responseSequences` and `_sequenceIndices` into `MockServiceState` (shared
via `Lazy<MockServiceState>`) so they persist across client instances. Added unique `StreamKey`
(`mock-turn-{sessionId}-{index}`) per prompt invocation in `SendPromptAsync`.

---

## Service-Level Direct Verification (supplementary)

In addition to the UI-driven e2e test above, you can verify the mock service layer directly:

```csharp
using SignalLoop.UnityCodeAgent.Service;
using SignalLoop.UnityCodeAgent.Contracts;

var svc = new AgentService();
await svc.BootstrapAsync();

var sessions = await svc.ApiClient.GetSessionsAsync(default);
Debug.Log($"Sessions: {sessions.Count}");

var first = sessions[0];
var history = await svc.ApiClient.OpenSessionAsync(first.SessionId, default);
Debug.Log($"Opened '{first.SessionId}': {history.Count} messages");

var newSession = await svc.ApiClient.CreateSessionAsync(default);
Debug.Log($"Created session: {newSession.SessionId}");

await svc.ApiClient.SendPromptAsync(first.SessionId, "test prompt", default);
Debug.Log("SendPromptAsync completed.");

await foreach (var evt in svc.EventStreamClient.SubscribeAsync(0, default))
{
    Debug.Log($"Event: seq={evt.SequenceNumber} type={evt.Type} session={evt.SessionId}");
    if (evt.Type == AgentEventType.SessionIdle) break;
}

Debug.Log("Direct verification complete.");
```

**Expected:** 5 sessions returned, history loaded, new session created, prompt enqueued,
events delivered including AssistantMessage and SessionIdle.