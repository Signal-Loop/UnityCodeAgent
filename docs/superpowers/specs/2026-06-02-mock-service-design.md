# Mock Agent Service for E2E Testing — Design Spec

**Date:** 2026-06-02
**Status:** Draft — pending user review

## Problem

The Unity editor integration (`ChatEditorWindowClient`, menu commands, event stream) cannot be tested end-to-end without a running Copilot service. This makes it difficult to:
- Verify UI behavior across different session types
- Test service lifecycle (start, stop, abort, reconnect)
- Catch regressions in event handling and message display
- Develop UI features without a live Copilot subscription

## Solution

Add a `MockService` toggle in `UnityCodeAgentSettings`. When enabled, the `AgentService` wires in mock implementations of all three interfaces (`IServiceBootstrap`, `IAgentServiceApiClient`, `IAgentServiceEventStreamClient`), returning predefined responses for 5 test sessions.

**Key design principle:** The mock is input-agnostic — the selected session determines what responses are returned, regardless of prompt text. This makes behavior deterministic and testable.

## Architecture

### Settings

Add to `UnityCodeAgentSettings`:

```csharp
[Tooltip("When enabled, the agent service returns predefined mock responses instead of connecting to the real Copilot service.")]
public bool MockService;
```

### Wiring

The `AgentService` default constructor checks the setting and swaps factories:

```csharp
public AgentService()
    : this(
        UnityCodeAgentSettings.Instance.MockService
            ? (IServiceBootstrap)new MockServiceBootstrap()
            : new ServiceBootstrap(),
        manifest => UnityCodeAgentSettings.Instance.MockService
            ? (IAgentServiceApiClient)new MockAgentServiceApiClient(manifest)
            : new HttpAgentServiceApiClient(manifest),
        manifest => UnityCodeAgentSettings.Instance.MockService
            ? (IAgentServiceEventStreamClient)new MockAgentServiceEventStreamClient(manifest)
            : new SseAgentServiceEventStreamClient(manifest),
        CreateDefaultPaths,
        LoadManifest)
{
}
```

**Zero changes required** to menu commands or `ChatEditorWindowClient` — they create `new AgentService()` and get mocks automatically.

### File Structure

```
Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/
    MockServiceState.cs
    MockServiceBootstrap.cs
    MockAgentServiceApiClient.cs
    MockAgentServiceEventStreamClient.cs
    MockSessionData.cs
```

## Mock Implementations

### `MockServiceState`

Shared coordinator referenced by all mock classes:

- `ConcurrentQueue<AgentServiceEventEnvelope>` for pending events
- `CancellationTokenSource` for the active prompt's event delivery
- `long` sequence number tracking for SSE reconnection
- `bool` flag for whether a prompt is in flight
- Thread-safe event enqueue/dequeue with cancellation support

### `MockServiceBootstrap : IServiceBootstrap`

- `ConnectOrStartAsync`:
  - Simulates ~200ms startup delay
  - Writes a fake `EndpointManifest` to disk at the expected path
  - `Port = 0`, `ServiceProcessId = -1` (mock sentinel), `ProjectId = "mock"`
  - Returns the manifest

This exercises the real `WaitForManifestAsync` code path. Creates the `.unityCodeAgent/service/runtime/` directory if it doesn't exist. `AgentService.Stop()` will try PID -1 → returns false (expected no-op). `Status()` reports mock state correctly.

### `MockAgentServiceApiClient : IAgentServiceApiClient`

Maintains a dictionary of 5 predefined sessions.

| Method | Behavior |
|--------|----------|
| `GetSessionsAsync` | Returns all 5 `SessionSummaryDto` entries |
| `GetModelsAsync` | Returns 2 mock models: `gpt-4o`, `claude-sonnet-4` |
| `OpenSessionAsync` | Returns matching session's full message history |
| `CreateSessionAsync` | Creates new session from template, adds to dictionary, returns welcome message |
| `SendPromptAsync` | **Ignores prompt text.** Enqueues the next predefined response sequence for the active session onto `MockServiceState` |
| `AbortPromptAsync` | Cancels the active prompt's `CancellationTokenSource` in `MockServiceState` |

**Response cycling:** Each session stores N response sequences. First prompt → sequence 1, second → sequence 2. After exhaustion, replays from start.

### `MockAgentServiceEventStreamClient : IAgentServiceEventStreamClient`

- Pulls events from the shared `MockServiceState` queue
- Respects `lastEventId` for reconnection replay (replays events with `SequenceNumber > lastEventId`)
- Delivers events with simulated delays (50–100ms) to mimic SSE behavior
- Properly handles cancellation without hanging
- Loops waiting for new events (like a real SSE connection)

## Mock Sessions

**Event envelope details:**
- `SequenceNumber`: monotonically increasing per session (1, 2, 3, ...)
- `SessionId`: matches the session being opened/created
- `TimestampUtc`: `DateTimeOffset.UtcNow` at event creation time
- `SourceJson`: JSON-serialized object matching the tool call parameters (e.g., file path for writes, query for searches); empty string for non-tool events
- `IsSubAgentEvent`: `false` for all events (happy path)
- `StreamKey`: `null` for all events

### Session 1: "Simple Code Question" (`mock-session-simple`)

**Summary:** Basic Q&A, no tools.

| # | Type | Content |
|---|------|---------|
| 1 | `UserMessage` | "How do I get the player's position in Unity?" |
| 2 | `AssistantMessage` | Use `transform.position` to read the world-space position... |

**Response sequences:** 1 (single Q&A)

### Session 2: "Code Generation with Tool Call" (`mock-session-codegen`)

**Summary:** Code generation with reasoning and tool use.

| # | Type | Content |
|---|------|---------|
| 1 | `UserMessage` | "Create a rotating cube script" |
| 2 | `ReasoningDelta` | "The user wants a MonoBehaviour that rotates a cube..." |
| 3 | `Tool` | File write: `Assets/Scripts/RotatingCube.cs` |
| 4 | `AssistantDelta` | "I've created a RotatingCube script..." (streaming chunks) |
| 5 | `AssistantMessage` | Full response with usage instructions |

**Response sequences:** 1

### Session 3: "Scene Query via MCP" (`mock-session-mcp`)

**Summary:** MCP tool interaction with Unity editor.

| # | Type | Content |
|---|------|---------|
| 1 | `UserMessage` | "List all GameObjects in the scene" |
| 2 | `Tool` | MCP call: `unity-code-mc` list scene objects |
| 3 | `Mcp` | MCP response with scene hierarchy JSON |
| 4 | `AssistantMessage` | "Here are the GameObjects in your scene: ..." |

**Response sequences:** 1

### Session 4: "Multi-turn Debug" (`mock-session-debug`)

**Summary:** Multi-turn conversation with clarification.

**Sequence 1:**

| # | Type | Content |
|---|------|---------|
| 1 | `UserMessage` | "My character falls through the floor" |
| 2 | `AssistantMessage` | "Can you tell me more? Is the floor a static collider..." |

**Sequence 2:**

| # | Type | Content |
|---|------|---------|
| 1 | `UserMessage` | "The floor has a BoxCollider and the character has a Rigidbody" |
| 2 | `Tool` | MCP call: check physics settings |
| 3 | `AssistantMessage` | "The issue is likely the Rigidbody's collision detection mode..." |

**Response sequences:** 2 (clarification → fix)

### Session 5: "Asset Search" (`mock-session-search`)

**Summary:** Asset search with streaming response.

| # | Type | Content |
|---|------|---------|
| 1 | `UserMessage` | "Find all textures in the project" |
| 2 | `Tool` | Search: glob `**/*.png` |
| 3 | `AssistantDelta` | "I found 23 textures in your project..." (streaming chunks) |
| 4 | `AssistantMessage` | Full list with paths |

**Response sequences:** 1

## Lifecycle Behaviors Exercised

| Behavior | How It's Exercised |
|----------|-------------------|
| Service start | `MockServiceBootstrap.ConnectOrStartAsync` with delay + manifest write |
| Service stop | `AgentService.Stop()` tries PID -1 → returns false |
| Service status | Reports mock manifest state correctly |
| Prompt send → event streaming | `SendPromptAsync` queues events; event stream delivers them |
| Abort mid-stream | `AbortPromptAsync` cancels the event delivery CancellationTokenSource |
| Session creation | `CreateSessionAsync` adds session to mock dictionary |
| Session history hydration | `OpenSessionAsync` returns predefined messages |
| Event stream reconnection | `StreamEventsAsync` replays from `lastEventId` |
| Snapshot | `GetSessionsAsync` returns all 5 sessions |
| Menu commands | All work unchanged via `new AgentService()` |

## Conventions

- File names prefixed with `Mock` for clear separation
- Namespace: `SignalLoop.UnityCodeAgent.Service.Mock`
- public visibility (matches existing service classes)
- Robust logging via `UnityCodeAgentLogger` at Debug/Trace level
- No inline styles, no external dependencies

## Out of Scope (Happy Path Only)

- Error injection (network failures, malformed responses)
- Latency simulation beyond basic SSE delays
- Edge cases (empty sessions, expired tokens)
- Performance/load testing
