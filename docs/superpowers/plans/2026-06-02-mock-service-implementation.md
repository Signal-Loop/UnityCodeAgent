# Mock Agent Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `MockService` toggle to `UnityCodeAgentSettings` that swaps in predefined mock responses for all agent service interfaces, enabling end-to-end testing of the Unity editor integration without a running Copilot service.

**Architecture:** The `AgentService` default constructor checks the `MockService` setting and conditionally injects mock implementations of `IServiceBootstrap`, `IAgentServiceApiClient`, and `IAgentServiceEventStreamClient`. All downstream consumers (menu commands, `ChatEditorWindowClient`) get mocks automatically with zero changes.

**Tech Stack:** C# / .NET, Unity Editor (UI Toolkit), Newtonsoft.Json, `ConcurrentQueue` for thread-safe event delivery.

**Spec:** `docs/superpowers/specs/2026-06-02-mock-service-design.md`

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettings.cs` | Add `MockService` bool field |
| Create | `Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/MockServiceState.cs` | Shared coordinator: event queue, cancellation, sequence tracking |
| Create | `Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/MockSessionData.cs` | 5 predefined sessions with summaries and event envelopes |
| Create | `Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/MockServiceBootstrap.cs` | Fake manifest write, startup delay |
| Create | `Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/MockAgentServiceApiClient.cs` | Session CRUD, prompt → event queue |
| Create | `Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/MockAgentServiceEventStreamClient.cs` | SSE-like event delivery from queue |
| Modify | `Packages/com.signal-loop.unitycodeagent/Editor/Service/AgentService.cs` | Conditional factory injection in default constructor |

---

### Task 1: Add `MockService` Setting

**Files:**
- Modify: `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettings.cs`

- [ ] **Step 1: Add the `MockService` field**

Add after the existing `TelemetryCaptureContent` field (around line 46):

```csharp
[Tooltip("When enabled, the agent service returns predefined mock responses instead of connecting to the real Copilot service.")]
public bool MockService;
```

The field is a simple `bool` — `false` by default. It persists in the `ScriptableObject` asset via Unity's serialization. No custom editor changes needed; it appears automatically in the inspector.

---

### Task 2: Create `MockServiceState`

**Files:**
- Create: `Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/MockServiceState.cs`

- [ ] **Step 1: Create the shared coordinator class**

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Logging;

namespace SignalLoop.UnityCodeAgent.Service.Mock
{
    public sealed class MockServiceState
    {
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();
        private long _nextSequenceNumber = 1;

        public ConcurrentQueue<AgentServiceEventEnvelope> PendingEvents { get; } = new ConcurrentQueue<AgentServiceEventEnvelope>();

        public CancellationTokenSource ActivePromptCts { get; set; }

        public bool IsPromptInFlight { get; set; }

        public long GetNextSequenceNumber()
            => Interlocked.Increment(ref _nextSequenceNumber);

        public void ResetSequenceNumber()
            => Interlocked.Exchange(ref _nextSequenceNumber, 1);

        public void EnqueueEvent(AgentServiceEventEnvelope envelope)
        {
            _log.Debug(nameof(MockServiceState), $"EnqueueEvent type={envelope.Type} seq={envelope.SequenceNumber} sessionId={envelope.SessionId}");
            PendingEvents.Enqueue(envelope);
        }

        public bool TryDequeueEvent(out AgentServiceEventEnvelope envelope)
            => PendingEvents.TryDequeue(out envelope);

        public void CancelActivePrompt()
        {
            var cts = ActivePromptCts;
            if (cts != null && !cts.IsCancellationRequested)
            {
                _log.Debug(nameof(MockServiceState), "Cancelling active prompt.");
                cts.Cancel();
            }

            ActivePromptCts = null;
            IsPromptInFlight = false;
        }
    }
}
```

---

### Task 3: Create `MockSessionData`

**Files:**
- Create: `Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/MockSessionData.cs`

- [ ] **Step 1: Create the predefined sessions data class**

This is the largest file — it defines all 5 sessions with their summaries, message histories, and response sequences.

```csharp
using System;
using System.Collections.Generic;
using SignalLoop.UnityCodeAgent.Contracts;

namespace SignalLoop.UnityCodeAgent.Service.Mock
{
    public static class MockSessionData
    {
        public static readonly IReadOnlyList<SessionSummaryDto> SessionSummaries = new[]
        {
            new SessionSummaryDto(
                SessionId: "mock-session-simple",
                StartTime: DateTimeOffset.UtcNow.AddHours(-2),
                ModifiedTime: DateTimeOffset.UtcNow.AddHours(-1),
                Summary: "Simple code question — how to get player position",
                Cwd: "C:/UnityProject",
                Branch: "main",
                Repository: "test-repo"),
            new SessionSummaryDto(
                SessionId: "mock-session-codegen",
                StartTime: DateTimeOffset.UtcNow.AddHours(-3),
                ModifiedTime: DateTimeOffset.UtcNow.AddHours(-2),
                Summary: "Code generation — rotating cube script with tool call",
                Cwd: "C:/UnityProject",
                Branch: "main",
                Repository: "test-repo"),
            new SessionSummaryDto(
                SessionId: "mock-session-mcp",
                StartTime: DateTimeOffset.UtcNow.AddHours(-4),
                ModifiedTime: DateTimeOffset.UtcNow.AddHours(-3),
                Summary: "MCP scene query — list GameObjects via unity-code-mc",
                Cwd: "C:/UnityProject",
                Branch: "main",
                Repository: "test-repo"),
            new SessionSummaryDto(
                SessionId: "mock-session-debug",
                StartTime: DateTimeOffset.UtcNow.AddHours(-5),
                ModifiedTime: DateTimeOffset.UtcNow.AddHours(-1),
                Summary: "Multi-turn debug — character falls through floor",
                Cwd: "C:/UnityProject",
                Branch: "main",
                Repository: "test-repo"),
            new SessionSummaryDto(
                SessionId: "mock-session-search",
                StartTime: DateTimeOffset.UtcNow.AddHours(-6),
                ModifiedTime: DateTimeOffset.UtcNow.AddHours(-4),
                Summary: "Asset search — find all textures in project",
                Cwd: "C:/UnityProject",
                Branch: "main",
                Repository: "test-repo"),
        };

        public static IReadOnlyList<AgentServiceEventEnvelope> GetMessages(string sessionId)
        {
            return sessionId switch
            {
                "mock-session-simple" => CreateSimpleMessages(),
                "mock-session-codegen" => CreateCodegenMessages(),
                "mock-session-mcp" => CreateMcpMessages(),
                "mock-session-debug" => CreateDebugMessages(),
                "mock-session-search" => CreateSearchMessages(),
                _ => Array.Empty<AgentServiceEventEnvelope>(),
            };
        }

        public static IReadOnlyList<IReadOnlyList<AgentServiceEventEnvelope>> GetResponseSequences(string sessionId)
        {
            return sessionId switch
            {
                "mock-session-simple" => new[] { CreateSimpleResponseSequence() },
                "mock-session-codegen" => new[] { CreateCodegenResponseSequence() },
                "mock-session-mcp" => new[] { CreateMcpResponseSequence() },
                "mock-session-debug" => new[] { CreateDebugSequence1(), CreateDebugSequence2() },
                "mock-session-search" => new[] { CreateSearchResponseSequence() },
                _ => Array.Empty<IReadOnlyList<AgentServiceEventEnvelope>>(),
            };
        }

        public static IReadOnlyList<AgentServiceEventEnvelope> CreateWelcomeMessages(string sessionId)
        {
            return new[]
            {
                MakeEnvelope(1, sessionId, "Welcome! How can I help you today?", AgentEventType.AssistantMessage),
            };
        }

        // --- Session 1: Simple Code Question ---

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateSimpleMessages()
        {
            var sid = "mock-session-simple";
            return new[]
            {
                MakeEnvelope(1, sid, "How do I get the player's position in Unity?", AgentEventType.UserMessage),
                MakeEnvelope(2, sid, "You can get the player's world-space position using `transform.position` on the player's `Transform` component.\n\n```csharp\nVector3 playerPos = playerTransform.position;\n```\n\nIf you need the position in local space (relative to a parent), use `transform.localPosition` instead.", AgentEventType.AssistantMessage),
                MakeEnvelope(3, sid, string.Empty, AgentEventType.SessionIdle),
            };
        }

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateSimpleResponseSequence()
        {
            var sid = "mock-session-simple";
            return new[]
            {
                MakeEnvelope(100, sid, "To get the player's position, attach a reference to the player's Transform and read `transform.position`. This returns a `Vector3` with x, y, z world coordinates.", AgentEventType.AssistantMessage),
                MakeEnvelope(101, sid, string.Empty, AgentEventType.SessionIdle),
            };
        }

        // --- Session 2: Code Generation ---

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateCodegenMessages()
        {
            var sid = "mock-session-codegen";
            return new[]
            {
                MakeEnvelope(1, sid, "Create a rotating cube script", AgentEventType.UserMessage),
                MakeEnvelope(2, sid, "The user wants a MonoBehaviour that continuously rotates a cube around the Y axis.", AgentEventType.ReasoningDelta),
                MakeEnvelope(3, sid, @"{""tool"":""file_write"",""path"":""Assets/Scripts/RotatingCube.cs"",""content"":""using UnityEngine;\\n\\npublic class RotatingCube : MonoBehaviour\\n{\\n    [SerializeField] private float speed = 50f;\\n\\n    private void Update()\\n    {\\n        transform.Rotate(Vector3.up, speed * Time.deltaTime);\\n    }\\n}""}", AgentEventType.Tool, sourceJson: @"{""name"":""file_write"",""arguments"":{""path"":""Assets/Scripts/RotatingCube.cs""}}"),
                MakeEnvelope(4, sid, "I've created a `RotatingCube` script at `Assets/Scripts/RotatingCube.cs`. It rotates around the Y axis at a configurable speed. Attach it to any cube GameObject.", AgentEventType.AssistantMessage),
                MakeEnvelope(5, sid, string.Empty, AgentEventType.SessionIdle),
            };
        }

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateCodegenResponseSequence()
        {
            var sid = "mock-session-codegen";
            return new[]
            {
                MakeEnvelope(100, sid, "I'll create a new script for you that rotates the cube around the Y axis.", AgentEventType.AssistantDelta),
                MakeEnvelope(101, sid, @"{""tool"":""file_write"",""path"":""Assets/Scripts/NewRotator.cs""}", AgentEventType.Tool, sourceJson: @"{""name"":""file_write"",""arguments"":{""path"":""Assets/Scripts/NewRotator.cs""}}"),
                MakeEnvelope(102, sid, "Created `Assets/Scripts/NewRotator.cs`. Attach it to your cube and adjust the speed in the inspector.", AgentEventType.AssistantMessage),
                MakeEnvelope(103, sid, string.Empty, AgentEventType.SessionIdle),
            };
        }

        // --- Session 3: MCP Scene Query ---

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateMcpMessages()
        {
            var sid = "mock-session-mcp";
            return new[]
            {
                MakeEnvelope(1, sid, "List all GameObjects in the scene", AgentEventType.UserMessage),
                MakeEnvelope(2, sid, @"{""tool"":""unity-code-mc/list_scene_objects"",""arguments"":{}}", AgentEventType.Tool, sourceJson: @"{""name"":""unity-code-mc/list_scene_objects"",""arguments"":{}}"),
                MakeEnvelope(3, sid, @"{""objects"":[{""name"":""Main Camera"",""instanceId"":1001},{""name"":""Directional Light"",""instanceId"":1002},{""name"":""Player"",""instanceId"":1003},{""name"":""Floor"",""instanceId"":1004}]}", AgentEventType.Mcp),
                MakeEnvelope(4, sid, "Here are the GameObjects in your scene:\n\n1. **Main Camera** (ID: 1001)\n2. **Directional Light** (ID: 1002)\n3. **Player** (ID: 1003)\n4. **Floor** (ID: 1004)\n\nWould you like to inspect or modify any of these?", AgentEventType.AssistantMessage),
                MakeEnvelope(5, sid, string.Empty, AgentEventType.SessionIdle),
            };
        }

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateMcpResponseSequence()
        {
            var sid = "mock-session-mcp";
            return new[]
            {
                MakeEnvelope(100, sid, @"{""tool"":""unity-code-mc/list_scene_objects"",""arguments"":{}}", AgentEventType.Tool, sourceJson: @"{""name"":""unity-code-mc/list_scene_objects"",""arguments"":{}}"),
                MakeEnvelope(101, sid, @"{""objects"":[{""name"":""Main Camera"",""instanceId"":1001},{""name"":""Player"",""instanceId"":1003}]}", AgentEventType.Mcp),
                MakeEnvelope(102, sid, "Found 2 GameObjects: Main Camera and Player.", AgentEventType.AssistantMessage),
                MakeEnvelope(103, sid, string.Empty, AgentEventType.SessionIdle),
            };
        }

        // --- Session 4: Multi-turn Debug ---

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateDebugMessages()
        {
            var sid = "mock-session-debug";
            return new[]
            {
                MakeEnvelope(1, sid, "My character falls through the floor", AgentEventType.UserMessage),
                MakeEnvelope(2, sid, "Can you tell me more? Does the floor have a collider? Does the character have a Rigidbody? What collision detection mode is set on the Rigidbody?", AgentEventType.AssistantMessage),
            };
        }

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateDebugSequence1()
        {
            var sid = "mock-session-debug";
            return new[]
            {
                MakeEnvelope(100, sid, "Let me ask a follow-up question to narrow down the issue.", AgentEventType.AssistantDelta),
                MakeEnvelope(101, sid, "Can you confirm: does the floor have a collider attached, and does the character have a Rigidbody component?", AgentEventType.AssistantMessage),
                MakeEnvelope(102, sid, string.Empty, AgentEventType.SessionIdle),
            };
        }

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateDebugSequence2()
        {
            var sid = "mock-session-debug";
            return new[]
            {
                MakeEnvelope(200, sid, @"{""tool"":""unity-code-mc/get_component_info"",""arguments"":{""objectName"":""Player"",""componentType"":""Rigidbody""}}", AgentEventType.Tool, sourceJson: @"{""name"":""unity-code-mc/get_component_info"",""arguments"":{""objectName"":""Player""}}"),
                MakeEnvelope(201, sid, @"{""collisionDetection"":""Discrete"",""mass"":1,""useGravity"":true}", AgentEventType.Mcp),
                MakeEnvelope(202, sid, "The issue is likely the Rigidbody's **Collision Detection** mode. It's set to `Discrete`, which can miss fast-moving objects penetrating thin colliders.\n\n**Fix:** Change the Rigidbody's collision detection to `Continuous` or `ContinuousDynamic`:\n\n```csharp\nGetComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.Continuous;\n```\n\nAlso ensure the floor's collider is at least 0.05 units thick.", AgentEventType.AssistantMessage),
                MakeEnvelope(203, sid, string.Empty, AgentEventType.SessionIdle),
            };
        }

        // --- Session 5: Asset Search ---

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateSearchMessages()
        {
            var sid = "mock-session-search";
            return new[]
            {
                MakeEnvelope(1, sid, "Find all textures in the project", AgentEventType.UserMessage),
                MakeEnvelope(2, sid, @"{""tool"":""search"",""query"":""**/*.png""}", AgentEventType.Tool, sourceJson: @"{""name"":""search"",""arguments"":{""glob"":""**/*.png""}}"),
                MakeEnvelope(3, sid, "I found 23 textures in your project:\n\n- `Assets/Textures/Grass.png`\n- `Assets/Textures/Stone.png`\n- `Assets/Textures/Sky.png`\n- `Assets/UI/Icons/health.png`\n- `Assets/UI/Icons/mana.png`\n- ...and 18 more.\n\nWould you like to filter by folder or rename them?", AgentEventType.AssistantMessage),
                MakeEnvelope(4, sid, string.Empty, AgentEventType.SessionIdle),
            };
        }

        private static IReadOnlyList<AgentServiceEventEnvelope> CreateSearchResponseSequence()
        {
            var sid = "mock-session-search";
            return new[]
            {
                MakeEnvelope(100, sid, @"{""tool"":""search"",""query"":""**/*.png""}", AgentEventType.Tool, sourceJson: @"{""name"":""search"",""arguments"":{""glob"":""**/*.png""}}"),
                MakeEnvelope(101, sid, "Found 23 PNG textures across `Assets/Textures/` and `Assets/UI/Icons/`.", AgentEventType.AssistantDelta),
                MakeEnvelope(102, sid, "Search complete. 23 textures found in 2 folders.", AgentEventType.AssistantMessage),
                MakeEnvelope(103, sid, string.Empty, AgentEventType.SessionIdle),
            };
        }

        // --- Factory ---

        private static AgentServiceEventEnvelope MakeEnvelope(
            long sequenceNumber,
            string sessionId,
            string content,
            AgentEventType type,
            string sourceJson = "")
        {
            return new AgentServiceEventEnvelope(
                SequenceNumber: sequenceNumber,
                SessionId: sessionId,
                TimestampUtc: DateTimeOffset.UtcNow,
                Content: content,
                StreamKey: null,
                Type: type,
                SourceJson: sourceJson ?? string.Empty,
                IsSubAgentEvent: false);
        }
    }
}
```

---

### Task 4: Create `MockServiceBootstrap`

**Files:**
- Create: `Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/MockServiceBootstrap.cs`

- [ ] **Step 1: Create the mock bootstrap class**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;

namespace SignalLoop.UnityCodeAgent.Service.Mock
{
    public sealed class MockServiceBootstrap : IServiceBootstrap
    {
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();

        public async Task<EndpointManifest> ConnectOrStartAsync(UnityCodeAgentPaths paths)
        {
            var stopwatch = Stopwatch.StartNew();
            _log.Info(nameof(MockServiceBootstrap), $"ConnectOrStartAsync begin (mock) projectRoot={paths.ProjectRoot}");

            // Simulate startup delay to exercise the real WaitForManifestAsync path
            await Task.Delay(200).ConfigureAwait(false);

            var manifest = new EndpointManifest
            {
                Version = 1,
                ProjectRoot = paths.ProjectRoot,
                ProjectId = "mock-project",
                UnityProcessId = Process.GetCurrentProcess().Id,
                ServiceProcessId = -1, // mock sentinel — Stop() will no-op
                Port = 0,
                StartedAtUtc = DateTimeOffset.UtcNow,
            };

            var manifestDir = Path.GetDirectoryName(paths.EndpointManifestPath);
            if (!string.IsNullOrEmpty(manifestDir) && !Directory.Exists(manifestDir))
            {
                Directory.CreateDirectory(manifestDir);
            }

            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            File.WriteAllText(paths.EndpointManifestPath, json);

            _log.Info(nameof(MockServiceBootstrap), $"ConnectOrStartAsync completed (mock) elapsedMs={stopwatch.ElapsedMilliseconds} manifestPath={paths.EndpointManifestPath}");
            return manifest;
        }
    }
}
```

---

### Task 5: Create `MockAgentServiceApiClient`

**Files:**
- Create: `Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/MockAgentServiceApiClient.cs`

- [ ] **Step 1: Create the mock API client class**

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;

namespace SignalLoop.UnityCodeAgent.Service.Mock
{
    public sealed class MockAgentServiceApiClient : IAgentServiceApiClient
    {
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();
        private readonly MockServiceState _state;
        private readonly EndpointManifest _manifest;
        private readonly ConcurrentDictionary<string, List<IReadOnlyList<AgentServiceEventEnvelope>>> _responseSequences;
        private readonly ConcurrentDictionary<string, int> _sequenceIndices;
        private long _nextSequenceNumber = 1000;

        public MockAgentServiceApiClient(EndpointManifest manifest)
            : this(manifest, new MockServiceState())
        {
        }

        public MockAgentServiceApiClient(EndpointManifest manifest, MockServiceState state)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _responseSequences = new ConcurrentDictionary<string, List<IReadOnlyList<AgentServiceEventEnvelope>>>();
            _sequenceIndices = new ConcurrentDictionary<string, int>();
        }

        public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
        {
            _log.Debug(nameof(MockAgentServiceApiClient), $"GetSessionsAsync returning {MockSessionData.SessionSummaries.Count} mock sessions.");
            return Task.FromResult(MockSessionData.SessionSummaries);
        }

        public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
        {
            var models = new List<ModelInfoDto>
            {
                new ModelInfoDto("gpt-4o", "GPT-4o"),
                new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4"),
            };
            _log.Debug(nameof(MockAgentServiceApiClient), $"GetModelsAsync returning {models.Count} mock models.");
            return Task.FromResult<IReadOnlyList<ModelInfoDto>>(models);
        }

        public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
        {
            _log.Debug(nameof(MockAgentServiceApiClient), $"OpenSessionAsync sessionId={request.SessionId}");
            var messages = MockSessionData.GetMessages(request.SessionId);
            return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ok", messages));
        }

        public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
        {
            _log.Debug(nameof(MockAgentServiceApiClient), $"CreateSessionAsync sessionId={request.SessionId}");

            // Register a default response sequence for the new session
            var sequences = new List<IReadOnlyList<AgentServiceEventEnvelope>>
            {
                CreateDefaultResponseSequence(request.SessionId),
            };
            _responseSequences[request.SessionId] = sequences;
            _sequenceIndices[request.SessionId] = 0;

            var welcomeMessages = MockSessionData.CreateWelcomeMessages(request.SessionId);
            return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ok", welcomeMessages));
        }

        public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
        {
            _log.Info(nameof(MockAgentServiceApiClient), $"SendPromptAsync sessionId={request.SessionId} (prompt ignored in mock mode)");

            _state.IsPromptInFlight = true;
            _state.ActivePromptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Get the response sequences for this session
            if (!_responseSequences.TryGetValue(request.SessionId, out var sequences))
            {
                // Session was opened (not created) — register its sequences
                var sessionSequences = MockSessionData.GetResponseSequences(request.SessionId).ToList();
                if (sessionSequences.Count == 0)
                {
                    sessionSequences.Add(CreateDefaultResponseSequence(request.SessionId));
                }
                sequences = sessionSequences;
                _responseSequences[request.SessionId] = sequences;
                _sequenceIndices[request.SessionId] = 0;
            }

            var index = _sequenceIndices.GetOrAdd(request.SessionId, 0);
            var sequenceIndex = index % sequences.Count;
            var responseSequence = sequences[sequenceIndex];
            _sequenceIndices[request.SessionId] = index + 1;

            // Enqueue events with updated sequence numbers
            foreach (var template in responseSequence)
            {
                var seq = _state.GetNextSequenceNumber();
                var envelope = new AgentServiceEventEnvelope(
                    SequenceNumber: seq,
                    SessionId: request.SessionId,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Content: template.Content,
                    StreamKey: template.StreamKey,
                    Type: template.Type,
                    SourceJson: template.SourceJson,
                    IsSubAgentEvent: template.IsSubAgentEvent);
                _state.EnqueueEvent(envelope);
            }

            _state.IsPromptInFlight = false;
            return Task.CompletedTask;
        }

        public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
        {
            _log.Info(nameof(MockAgentServiceApiClient), $"AbortPromptAsync sessionId={request.SessionId}");
            _state.CancelActivePrompt();
            return Task.CompletedTask;
        }

        private IReadOnlyList<AgentServiceEventEnvelope> CreateDefaultResponseSequence(string sessionId)
        {
            return new[]
            {
                new AgentServiceEventEnvelope(
                    SequenceNumber: Interlocked.Increment(ref _nextSequenceNumber),
                    SessionId: sessionId,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Content: "This is a mock response. The mock service is active.",
                    StreamKey: null,
                    Type: AgentEventType.AssistantMessage,
                    SourceJson: string.Empty,
                    IsSubAgentEvent: false),
                new AgentServiceEventEnvelope(
                    SequenceNumber: Interlocked.Increment(ref _nextSequenceNumber),
                    SessionId: sessionId,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Content: string.Empty,
                    StreamKey: null,
                    Type: AgentEventType.SessionIdle,
                    SourceJson: string.Empty,
                    IsSubAgentEvent: false),
            };
        }
    }
}
```

---

### Task 6: Create `MockAgentServiceEventStreamClient`

**Files:**
- Create: `Packages/com.signal-loop.unitycodeagent/Editor/Service/Mock/MockAgentServiceEventStreamClient.cs`

- [ ] **Step 1: Create the mock event stream client class**

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;

namespace SignalLoop.UnityCodeAgent.Service.Mock
{
    public sealed class MockAgentServiceEventStreamClient : IAgentServiceEventStreamClient
    {
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();
        private readonly MockServiceState _state;

        public MockAgentServiceEventStreamClient(EndpointManifest manifest)
            : this(manifest, new MockServiceState())
        {
        }

        public MockAgentServiceEventStreamClient(EndpointManifest manifest, MockServiceState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public async Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken)
        {
            _log.Debug(nameof(MockAgentServiceEventStreamClient), $"StreamEventsAsync begin lastEventId={lastEventId?.ToString() ?? "null"}");

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_state.TryDequeueEvent(out var envelope))
                {
                    // Skip events we already delivered (reconnection scenario)
                    if (lastEventId.HasValue && envelope.SequenceNumber <= lastEventId.Value)
                    {
                        continue;
                    }

                    // Simulate SSE delivery delay
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);

                    _log.Debug(nameof(MockAgentServiceEventStreamClient), $"Delivering event seq={envelope.SequenceNumber} type={envelope.Type} sessionId={envelope.SessionId}");
                    onEvent(envelope);
                }
                else
                {
                    // No events available — wait a bit before checking again
                    try
                    {
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            _log.Debug(nameof(MockAgentServiceEventStreamClient), "StreamEventsAsync ended.");
        }
    }
}
```

---

### Task 7: Wire Up `AgentService` Default Constructor

**Files:**
- Modify: `Packages/com.signal-loop.unitycodeagent/Editor/Service/AgentService.cs`

- [ ] **Step 1: Add the `using` for the Mock namespace**

Add at the top of the file, after the existing `using` statements:

```csharp
using SignalLoop.UnityCodeAgent.Service.Mock;
```

- [ ] **Step 2: Replace the default constructor**

Replace the existing default constructor:

```csharp
public AgentService()
    : this(
        new ServiceBootstrap(),
        manifest => new HttpAgentServiceApiClient(manifest),
        manifest => new SseAgentServiceEventStreamClient(manifest),
        CreateDefaultPaths,
        LoadManifest)
{
}
```

With:

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

Note: The `MockServiceBootstrap`, `MockAgentServiceApiClient`, and `MockAgentServiceEventStreamClient` each create their own `MockServiceState` in their public constructors. For the mock event delivery to work (API client enqueues → event stream dequeues), the `MockServiceState` must be shared. The `internal` constructors accepting `MockServiceState` exist for testing, but the default wiring creates separate instances.

To make the default wiring share state, add a static `MockServiceState` singleton:

- [ ] **Step 3: Add shared state to the default constructor**

Update the default constructor to use a shared `MockServiceState`:

```csharp
public AgentService()
    : this(
        UnityCodeAgentSettings.Instance.MockService
            ? CreateMockBootstrap()
            : (IServiceBootstrap)new ServiceBootstrap(),
        manifest => UnityCodeAgentSettings.Instance.MockService
            ? CreateMockApiClient(manifest)
            : (IAgentServiceApiClient)new HttpAgentServiceApiClient(manifest),
        manifest => UnityCodeAgentSettings.Instance.MockService
            ? CreateMockEventStreamClient(manifest)
            : (IAgentServiceEventStreamClient)new SseAgentServiceEventStreamClient(manifest),
        CreateDefaultPaths,
        LoadManifest)
{
}

private static readonly Lazy<MockServiceState> SharedMockState = new Lazy<MockServiceState>();

private static IServiceBootstrap CreateMockBootstrap()
    => new MockServiceBootstrap();

private static IAgentServiceApiClient CreateMockApiClient(EndpointManifest manifest)
    => new MockAgentServiceApiClient(manifest, SharedMockState.Value);

private static IAgentServiceEventStreamClient CreateMockEventStreamClient(EndpointManifest manifest)
    => new MockAgentServiceEventStreamClient(manifest, SharedMockState.Value);
```

This ensures the `MockAgentServiceApiClient` and `MockAgentServiceEventStreamClient` share the same `MockServiceState` instance, so events enqueued by `SendPromptAsync` are delivered by `StreamEventsAsync`.

---

### Task 8: Verify Compilation

**Files:**
- None (verification only)

- [ ] **Step 1: Build the Unity project to verify no compilation errors**

Open the Unity project and check the Console for compilation errors. All new files should compile cleanly with the existing codebase.

Expected: No errors. Unity auto-generates `.meta` files for the new `Mock/` directory and its contents.

---

## Execution Notes

- **No tests required** (per user request).
- **No menu command changes** — all menu commands create `new AgentService()` and pick up mocks automatically.
- **No `ChatEditorWindowClient` changes** — it creates `new AgentService()` internally.
- The `MockServiceState` is shared via `Lazy<MockServiceState>` to ensure API client and event stream client coordinate properly.
- The mock is **input-agnostic** — prompt text is ignored; the selected session determines responses.
- Response sequences cycle after exhaustion (modulo indexing).
