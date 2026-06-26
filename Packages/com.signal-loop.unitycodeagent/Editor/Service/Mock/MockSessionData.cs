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
                Summary: "Simple code question — how to get player position and verify whether the session label truncation works in the UI",
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
                StreamKey: string.Empty,
                Type: type,
                SourceJson: sourceJson ?? string.Empty,
                IsSubAgentEvent: false);
        }
    }
}
