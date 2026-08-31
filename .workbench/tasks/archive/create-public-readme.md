# README

- status: Completed
- order: 2800
- goal: Write a concise public root `README.md` for UnityCodeAgent that explains what it is, prerequisites, installation, setup, and core usage, verified against package metadata and editor/service behavior without changing runtime behavior.
- updated: 2026-06-24
- steps:
    - [x] Create root `README.md` with concise public-facing product overview.
    - [x] Document installation through Unity Package Manager from the Git repository/package path and package requirements.
    - [x] Document source-distribution requirements, including Unity 6.3 and the .NET 8 SDK used by the local service.
    - [x] Document first-use setup through the BYOK and GitHub Copilot quickstart paths.
    - [x] Document normal usage: open chat, send prompts, view sessions, stop long-running responses, and use Unity tool capabilities.
    - [x] Include troubleshooting notes for service startup, missing model selection, logs, and where runtime files are stored.
    - [x] Verify all paths, menu names, package metadata, and command names against the repo before marking done.

Original request:
- Release UnityCodeAgent publicly with a concise GitHub root README.md that explains what it is, how to install it, and how to use it.

Research:
- Package metadata in `Packages/com.signal-loop.unitycodeagent/package.json`: package name `com.signal-loop.unitycodeagent`, display name `Unity Code Agent`, version `0.1.1`, Unity `6000.3` / Unity 6.3, MIT license, dependencies `com.unity.nuget.newtonsoft-json` `3.2.2` and `com.unity.inputsystem` `1.19.0`.
- Unity menu root is `Tools/UnityCodeAgent/` with `Open Chat`, `Open Settings`, `Open MCP Config`, and `Restart Agent Service`; debug utilities include `Debug/Start Agent Service`, `Debug/Stop Agent Service`, `Debug/Agent Service Status`, `Debug/Snapshot`, and `Debug/Get Current Session`.
- The Unity editor side is a thin UI/client. The local .NET 8 service owns GitHub Copilot SDK integration, sessions, MCP loading, model listing, telemetry, and runtime metadata.
- The service starts from source with `dotnet run --project Packages/com.signal-loop.unitycodeagent/Editor/CopilotService~/UnityCodeCopilot.Service.csproj`, so README prerequisites include the .NET 8 SDK.
- Settings expose BYOK BaseUrl/ApiKey, model refresh/selection, skills folders and toggles, skills install target, input actions asset path, extra tool assemblies, MCP config, logging/debug options, service startup options, mock service, and telemetry options.
- Default skills install target is `.agents/skills`; bundled skills are copied from `Packages/com.signal-loop.unitycodeagent/Editor/Skills~` after assembly reload.
- MCP config path is `.unityCodeAgent/client/mcp.json`; the settings UI and menu can create/open it.
- Runtime state uses `.unityCodeAgent/`, including `.unityCodeAgent/service/runtime/endpoint.json`, `.unityCodeAgent/service/runtime/event-cursor.json`, and `.unityCodeAgent/client/mcp.json`.
- Chat supports session history, session list, streaming responses, user prompts, Unity-side tool execution, and a busy-state stop behavior through the same Send button.

Completion:
- Added root `README.md` with public setup and usage guidance.
- Used the package documentation URL `https://github.com/Signal-Loop/UnityCodeAgent` for the release install example because the current `origin` remote still points at the temporary `machinedawn/UnityCodeCopilot` repository.

Verification:
- Checked README menu paths against `Packages/com.signal-loop.unitycodeagent/Editor/Menu/UnityCodeAgentServiceMenu.cs`.
- Checked settings/setup wording against `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettings.cs` and `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettingsEditor.cs`.
- Checked package requirements and dependencies against `Packages/com.signal-loop.unitycodeagent/package.json`.
- Checked service launch and runtime path claims against `Packages/com.signal-loop.unitycodeagent/Editor/Service/ServiceBootstrap.cs` and `Packages/com.signal-loop.unitycodeagent/Editor/Infrastructure/UnityCodeAgentPaths.cs`.
- Checked chat controls against `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatEditorWindow.cs` and `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatWindow.uxml`.

