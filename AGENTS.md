# AGENTS

## Project

UnityCodeAgent is a Unity Editor chat client backed by a local .NET 8 ASP.NET Core service that wraps the official GitHub Copilot .NET SDK.

Keep Unity thin. Unity owns editor UI, bootstrap, and temporary user-side inputs. The local service owns Copilot SDK lifecycle, session orchestration, MCP integration, permissions, telemetry, and runtime metadata.

The agent is distributed as a Unity package (`Packages/com.signal-loop.unitycodeagent/package.json`) and includes a package installer that copies the bundled skills into the project on install or update. Installer is located in `Packages/com.signal-loop.unitycodeagent/Editor/Installer/` folder.

## Layout

Unity and the service communicate over loopback HTTP plus SSE. The machine-readable contract is the source of truth:

- `contracts/openapi/agent-service.openapi.yaml`
- `contracts/asyncapi/agent-service-events.asyncapi.yaml`

Shared DTOs live in `Packages/com.signal-loop.unitycodeagent/Editor/Contracts/ServiceContracts.cs` and are linked into the service project.

### Unity Editor Side

- `Packages/com.signal-loop.unitycodeagent/Editor`: Main Unity editor integration, including window bootstrap, editor lifecycle hooks, and thin client orchestration.
- `Packages/com.signal-loop.unitycodeagent/Editor/UI`: UI Toolkit views, presenters, and USS for the chat window and related editor surfaces.
- `Packages/com.signal-loop.unitycodeagent/Editor/Service`: Unity-side service bridge for manifest handling, loopback HTTP calls, SSE subscriptions, and the `AgentService` facade consumed by the UI.
- `Packages/com.signal-loop.unitycodeagent/Editor/Contracts`: Shared DTOs and event types used by both Unity and the local service.
- `Assets/Tests/Editor/Service`: Unity EditMode coverage for transport behavior, reconnect flows, and service restart recovery.
- `.unityCodeAgent/client/logs/unity.log`: Path to the Unity Editor log file.

### Local Service Side

- `Packages/com.signal-loop.unitycodeagent/Editor/CopilotService~`: ASP.NET Core service implementation that owns Copilot SDK integration, session orchestration, MCP wiring, permissions, telemetry, and runtime metadata.
- `Packages/com.signal-loop.unitycodeagent/Editor/CopilotService~/Api`: HTTP and SSE endpoint surface that exposes the machine-readable contract to the Unity client.
- `Packages/com.signal-loop.unitycodeagent/Editor/CopilotService~/Copilot`: GitHub Copilot SDK-specific adapters and runtime coordination.
- `Packages/com.signal-loop.unitycodeagent/Editor/CopilotService~/Infrastructure`, `Options`, `Settings`, `Telemetry`: Cross-cutting service configuration, dependency setup, logging, and operational concerns.
- `CopilotService.Tests`: In-process endpoint and contract tests that host the real `Program` pipeline.
- `.unityCodeAgent/service/runtime/endpoint.json`: Project-scoped runtime endpoint manifest written and consumed during local service bootstrap.
- `Assets/Plugins/UnityCodeAgent/Editor/UnityCodeAgentSettings.asset`: Settings for running UnityCodeAgent.
- `.unityCodeAgent/service/logs/service.log`: Path to Agent Service log file.
- `.unityCodeAgent/service/logs/telemetry.jsonl`: Path to Telemetry log file.

## Conventions

- Use `Agent` naming on shared and Unity-facing surfaces. Reserve `Copilot` for GitHub Copilot SDK-specific implementation types.
- Prefer direct, small implementations. Add interfaces only for a real substitution or test seam.
- Follow SOLID, YAGNI, and KISS.
- Add robust logging and use appropriate log levels.
- Do not create Unit Editor .meta files. Wait for unity to recompile and generate them automatically.

## Git operations

- Do not stage or commit changes without specific instructions.

## UI

- Use UI Toolkit for editor UI.
- Keep UI code and USS together under `Packages/com.signal-loop.unitycodeagent/Editor/UI`.
- Do not use inline styles.

## Verification

- Always ensure that changes are discovered and reloaded by Unity Editor - check by name if new tests are discovered, check UI if changes are visible and use other methods to ensure that Unity Editor discovered changes and reloaded domain. If not, reload the domain using script. This is mandatory step and cannot be omitted.
- For Unity/editor changes, prefer Unity EditMode tests, Unity console logs, and targeted `execute_csharp_script_in_unity_editor` checks.
- For service changes, prefer focused `dotnet test` runs in `CopilotService.Tests`.
- When running Codex skill validation scripts that import `yaml`, use `uv run --with pyyaml ...` instead of the ambient Python interpreter.
- When the local `UnityCodeCopilot.Service` process may already be running, run service tests with an isolated artifact path so the build does not try to overwrite the live exe:
  `dotnet test CopilotService.Tests\UnityCodeCopilot.Service.Tests.csproj --artifacts-path .artifacts\copilot-service-tests -p:UseAppHost=false`
- The contract-spec tests resolve `contracts/openapi/agent-service.openapi.yaml` and `contracts/asyncapi/agent-service-events.asyncapi.yaml` relative to the artifact root, so those files must also be available under `.artifacts\contracts\...` when using the artifact-path workflow.
- Prefer narrow behavior-scoped checks over long live end-to-end runs.
- Keep OpenAPI and AsyncAPI examples aligned with real JSON payloads and tested behavior.

### E2E Tests

- For UI E2E tests, use `[UnityTest]` and small polling helpers that `yield return null` until the editor finishes layout, binding, and async update delivery. Do not assume menu actions, button clicks, or SSE-driven transcript updates complete in the same frame.
- Simulate user interaction through real UI Toolkit events instead of Reflection: use `EditorApplication.ExecuteMenuItem(...)` to open windows, set `TextField.value`, send `NavigationSubmitEvent` to buttons, and send `ClickEvent` to session entries.
- Examples: client-side mock/in-memory setup and event assertions are in [Assets/Tests/Editor/Service/ChatEditorWindowClientE2eTests.cs](Assets/Tests/Editor/Service/ChatEditorWindowClientE2eTests.cs); UI interaction, polling helpers, and editor event simulation are in [Assets/Tests/Editor/Service/ChatEditorWindowUiE2eTests.cs](Assets/Tests/Editor/Service/ChatEditorWindowUiE2eTests.cs).

## External References

- GitHub Copilot .NET SDK source: `../copilot-sdk/dotnet/` (https://github.com/github/copilot-sdk)
- GitHub Copilot CLI source: `../copilot-cli/` (https://github.com/github/copilot-cli)
If sdk source is not found, clone the specific version of repo to `../copilot-sdk/`. Use the exact version as in 'Packages\com.signal-loop.unitycodeagent\Editor\CopilotService~\UnityCodeCopilot.Service.csproj'.
