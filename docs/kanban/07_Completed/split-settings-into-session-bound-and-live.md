# Split settings into session-bound and live

- goal: Separate service-restart-bound settings from user-live settings in the inspector, while internally tracking session-reopen-bound settings, verified by focused settings/session tests and launch-command coverage without changing the serialized settings asset shape unnecessarily.
  - updated: 2026-06-22
  - steps:
      - [x] Add a session request signature that covers provider/model/BYOK, working directory, enabled skill directories, disabled skills, and tool definitions used to open/create sessions.
      - [x] Replace model-only active session change tracking in `ChatEditorWindowClient` with session request signature tracking and keep the footer model label update behavior.
      - [x] Rework `UnityCodeAgentSettingsEditor` grouping into clear service-restart-bound and live-applied sections without renaming serialized fields; settings applied by session reopen belong in the live-applied section from the user's perspective.
      - [x] Add focused client tests proving skill/provider changes reopen before the next prompt and live debug settings do not force reopen.
      - [x] Keep/extend launch-command tests for restart-bound service options.
    ```md
    Research:
    - `UnityCodeAgentSettings` is one serialized `ScriptableObject` containing service bootstrap options, Unity UI/debug options, tool options, BYOK/model provider options, skills, and installer settings.
    - `UnityCodeAgentSettings.CreateUnityContext()` snapshots every setting into `UnityContext`; UI code frequently calls `UnityCodeAgentSettings.GetUnityContext()` at action/drain/render time, so some settings already behave live for the next call.
    - Session open/create payloads are built by `SessionRequestFactory` from `UnityContext.Provider`, `Paths.ProjectRoot`, `SkillDirectories`, `DisabledSkills`, and `UnityAgentToolRegistry.Shared.GetDefinitions()`.
    - `ChatEditorWindowClient` currently tracks only `_activeModelSelectionSignature`; provider/model changes reopen the active session before the next prompt and update the footer label, covered by `ModelChange_ReconfiguresActiveSessionBeforeNextPromptAndUpdatesLabel`.
    - Skill directory and disabled-skill changes are session-bound by request shape, but they are not currently part of the active-session change signature, so toggling skills after opening a session may not reopen before the next prompt.
    - `ServiceBootstrap.BuildServiceArguments()` consumes restart-bound process options: dynamic/fixed port, orphan timeout, service min log level, service file logging, telemetry mode/endpoint/file/content. Tests already cover these in `ServiceBootstrapLaunchCommandTests`.
    - Unity log level/file logging are live for Unity-side logging because `UnityCodeAgentLogger` reads `UnityCodeAgentSettings.Instance.MinLogLevel` and `LogToFile` on each write.
    - Chat debug display settings are live-applied because `ChatEditorWindow` calls `GetUnityContext()` while draining and rendering updates.
    - Tool execution settings such as input actions asset path and additional tool assemblies are live for future Unity-side tool invocation because tool execution receives the current `UnityContext`.
    - The current inspector groups settings by domain labels (`Service`, `Logging`, `Debug`, `Tools`, `Telemetry`, `BYOK`, `Skills`, `Model`), not by apply lifetime.

    Classification:
    - Service-restart-bound settings: `UseDynamicServicePort`, `ServicePort`, `ServiceOrphanTimeoutSeconds`, service-side `MinLogLevel`/`LogToFile`, `TelemetryMode`, `OtlpEndpoint`, `CliTelemetryFilePath`, `TelemetryCaptureContent`, and `MockAgentService`.
    - Internally session-reopen-bound settings: `Model`, `ByokBaseUrl`, `ByokApiKey`, enabled skill directories, disabled skills, working directory/project root, and the tool definitions sent in open/create requests.
    - User-facing live settings: internally session-reopen-bound settings plus `ShowEventsSourceInChat`, `ShowAllEventsInChat`, Unity-side logger level/file logging, input actions asset path, additional tool assemblies for future tool calls, and skill installer target/path behavior.
    - UI rule from product feedback: settings applied by automatically reopening/restarting the active session should be shown in the live-applied section because the user experiences them as live changes and does not need to reason about session lifecycle.
    - Session-reopen-bound live settings may invalidate provider/LLM-side prompt or context caches and can affect token usage or cost; surface this as a concise note near those settings if the UI distinguishes them inside the live section.

    Plan:
    - Keep `UnityCodeAgentSettings` as one asset to avoid a serialized asset migration unless implementation proves separate assets are necessary.
    - Add a deterministic session request signature, likely on `SessionRequestFactory.SessionRequestOptions`, using provider signature, working directory, sorted skill directories/disabled skills, and tool definition identity.
    - Rename the chat client's internal model-only tracking to session request tracking and use it to reopen the active session before the next prompt when any session-bound input changes.
    - Preserve `ChatSetModelLabelUpdate` behavior so provider/model changes still update the footer independently of the broader session signature.
    - Reorganize `UnityCodeAgentSettingsEditor.OnInspectorGUI()` into explicit user-facing sections such as `Live Settings` and `Service Restart Required`; do not expose a separate `Session Settings` section just because implementation reopens sessions behind the scenes.
    - Within `Live Settings`, add concise helper text for settings that cause an automatic session reopen, noting that changing them may invalidate LLM cache and affect cost.
    - Avoid inline UI styles; continue using IMGUI editor controls already used by this settings inspector.
    - Add or extend `ChatEditorWindowClientE2eTests` with a harness that changes disabled skills or skill directories after initialization and asserts the next prompt reopens the current session before sending.
    - Add a negative test for live debug settings changing without causing a session reopen.
    - Keep existing service launch-command tests as the verification surface for restart-bound settings; add assertions only if grouping changes require behavior changes.

    Verification:
    - Passed: Unity EditMode tests `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests.ModelChange_ReconfiguresActiveSessionBeforeNextPromptAndUpdatesLabel`, `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests.SkillChange_ReconfiguresActiveSessionBeforeNextPrompt`, `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests.LiveDebugChange_DoesNotReconfigureActiveSessionBeforeNextPrompt`, and `SignalLoop.UnityCodeAgent.Service.ServiceBootstrapLaunchCommandTests` (9 passed, 0 failed).
    - Passed after manual regression report: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj --filter CopilotSessionManagerTests --artifacts-path .artifacts/tests/session-reopen-skills` (4 passed, 0 failed).

    Completion:
    - `SessionRequestFactory.SessionRequestOptions` now computes a deterministic session request signature from provider, working directory, skill directories, disabled skills, and tool definitions.
    - `ChatEditorWindowClient` now reopens the active session when any session-bound request input changes, while preserving footer model label updates and avoiding reopen for live debug-only settings.
    - `CopilotSessionManager` now compares the full session-bound request signature before reusing an attached runtime session, so disabled-skill changes force SDK session resume with the updated disabled list instead of reusing a stale attached session.
    - The settings inspector now groups user-facing live settings separately from service restart-required settings, with helper text noting that automatic session reopen can invalidate LLM caches and affect token usage or cost.
    ```

