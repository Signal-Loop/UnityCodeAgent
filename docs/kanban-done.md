
### Unreliable test ConnectOrStartAsync_FromBackgroundThread_PublishesManifest 

[ERROR] #UnityCodeAgent [AgentService] StartAsync failed elapsedMs=21134
Exception:
System.InvalidOperationException: Service process exited before publishing its endpoint manifest. exitCode=1. Captured output:
stdout:
C:\Program Files\dotnet\sdk\10.0.301\Microsoft.Common.CurrentVersion.targets(5397,5): warning MSB3026: Could not copy "<repo-root>\Packages\com.signal-loop.unitycodeagent\Editor\CopilotService~\obj\Debug\net8.0\apphost.exe" to "bin\Debug\net8.0\UnityCodeCopilot.Service.exe". Beginning retry 1 in 1000ms. The process cannot access the file '<repo-root>\Packages\com.signal-loop.unitycodeagent\Editor\CopilotService~\bin\Debug\net8.0\UnityCodeCopilot.Service.exe' because it is being used by another process. The file is locked by: "UnityCodeCopilot.Service (11228)" [<repo-root>\Packages\com.signal-loop.unitycodeagent\Editor\CopilotService~\UnityCodeCopilot.Service.csproj]


### Provide more informational messages on auth errors

- status: planning
- goal: Show provider-specific authentication failure guidance for BYOK and GitHub Copilot paths in settings, chat errors, and chat progress messages, verified by focused service/client/UI tests, without exposing API keys or changing successful model/session behavior.
- approval: required
- blocked: false
- updated: 2026-06-24
- steps:
    - [ ] Add provider-aware auth status/error handling in the service auth paths.
    - [ ] Return BYOK-specific guidance when custom provider auth fails.
    - [ ] Return GitHub Copilot-specific guidance when default Copilot auth fails.
    - [ ] Surface auth failures in the chat window as progress messages as well as normal errors.
    - [ ] Preserve raw exception details in service logs while keeping user-facing messages safe.
    - [ ] Add focused tests for BYOK and GitHub Copilot auth error responses.

Original request:
- Provide more informational messages on auth errors.
- Different messages when BYOK is enabled and failed and when Copilot is used and failed.

Research:
- `UnityCodeAgentSettings.TryCreateProviderConfig` treats a non-empty `ByokBaseUrl` as BYOK; otherwise the service uses the default GitHub Copilot authentication flow.
- Model refresh calls `AgentService.GetModelsAsync`, which posts `/api/models` with `ListAgentModelsRequestDto.Provider`; the settings inspector displays the thrown exception message directly in a help box.
- `HttpAgentServiceApiClient.EnsureSuccessAsync` already preserves `AgentServiceErrorResponse.Message`, so improving service responses will flow through to Unity without a UI rewrite.
- `ServiceEndpoints.CreateErrorResult` currently returns the innermost exception message for every expected request failure, with generic `operation_failed` for non-session failures.
- BYOK model listing uses `ByokOpenAiProvider.ListModelsAsync`, which calls `{BaseUrl}/models` with the configured bearer token and currently throws the raw provider body or reason phrase on non-success.
- Default GitHub Copilot model/session operations go through `CopilotClientHost` and the GitHub Copilot SDK. The SDK exposes `GetAuthStatusAsync`, and invalid auth in SDK tests can surface as exception text containing `401 Unauthorized`.
- Copilot SDK source analysis:
  - `CopilotClient.GetAuthStatusAsync` calls `auth.getStatus` and returns `GetAuthStatusResponse` with `IsAuthenticated`, `AuthType`, `Host`, `Login`, and `StatusMessage`.
  - SDK `ClientE2ETests.Should_List_Models_When_Authenticated` checks `GetAuthStatusAsync` before `ListModelsAsync`; the test skips model listing when `IsAuthenticated` is false, confirming `models.list` is auth-gated.
  - SDK `CopilotClient.ListModelsAsync` calls `models.list` only for the default Copilot path. BYOK model listing uses `OnListModels`, so BYOK does not benefit from `auth.getStatus`.
  - SDK `CreateSessionAsync` and `ResumeSessionAsync` call `session.create` / `session.resume` and pass `GitHubToken` through session config when supplied. UnityCodeAgent currently does not supply a GitHub token, so the default path depends on the logged-in user / gh CLI auth.
  - SDK startup passes explicit `GitHubToken` through `COPILOT_SDK_AUTH_TOKEN` and adds `--no-auto-login` when `UseLoggedInUser` is false. UnityCodeAgent does not set either option, so the reliable default guidance is to refresh GitHub/gh CLI login rather than edit Unity BYOK settings.
  - SDK JSON-RPC errors become internal `RemoteRpcException` instances, then `CopilotClient.InvokeRpcAsync` wraps them as `IOException` with message `Communication error with Copilot CLI: {remote message}`. The internal exception has public `ErrorCode` and `ErrorData`, but because the type is internal, production code should prefer explicit `GetAuthStatusAsync` checks over reflection-based classification.
  - SDK `PerSessionAuthE2ETests.ShouldFailWithInvalidToken` asserts invalid auth can surface as `401 Unauthorized`, so a fallback classifier should still recognize `401`, `403`, `Unauthorized`, `Forbidden`, and authentication wording in SDK exception messages.
- Service endpoint contract tests can fake `IAgentModelCatalog` failures in-process; Unity HTTP client tests already prove contract-shaped error messages surface unchanged.
- Chat progress source analysis:
  - `AgentService` accepts a `showProgressMessage` callback and already emits operation progress such as `Loading agent models...`, `Creating chat session...`, `Sending prompt...`, and service reconnect messages.
  - `ChatEditorWindowClient` constructs `AgentService(ShowProgressMessage)` and maps that callback to `ChatShowProgressMessageUpdate`.
  - `ChatEditorWindow` maps `ChatShowProgressMessageUpdate` to `ChatProgressMessages.ShowProgressMessage`, which renders a transient progress row using `ChatMessageTemplateProgress.uxml`.
  - `ChatShowErrorUpdate` currently appends a persistent error bubble. Submit/open/init/session-refresh failure paths enqueue error updates, but they do not explicitly enqueue a progress update containing the final user-facing failure text.
  - `ChatProgressMessages` removes trailing progress when visible transcript messages arrive or when busy state changes to false, so an auth-failure progress update must be ordered deliberately or handled by a dedicated helper to avoid being immediately removed.

Plan:
- Prefer explicit checks over broad string classification:
  - Add a small `CopilotClientHost.GetAuthStatusAsync` wrapper around the SDK `GetAuthStatusAsync`.
  - For default Copilot model listing, check auth status before `ListRuntimeModelsAsync`; if unauthenticated, throw a purpose-built user-facing exception/message.
  - For default Copilot create/open session paths, check auth status before `CreateSessionAsync` / `ResumeSessionAsync` so chat/session failures get the same clear guidance.
  - Do not call SDK auth status for BYOK model listing, because BYOK uses `OnListModels` and provider auth is independent from GitHub auth.
- Add a very small provider-aware fallback formatter near `ServiceEndpoints` or a focused API helper for failures that occur after preflight. Inputs should include the caught exception, endpoint operation context, and whether the request provider has BYOK enabled.
- Keep `AgentServiceErrorResponse` shape stable unless a concrete need appears. Prefer clearer `Message` text and existing `operation_failed` code to avoid contract churn.
- For BYOK model refresh, use the actual HTTP status code in `ByokOpenAiProvider.EnsureProviderResponseSuccess`: return BYOK auth guidance for 401/403 and leave non-auth provider failures specific to the raw status/body. Do not include the API key or full headers in any user-facing message.
- For BYOK session failures, use the fallback formatter only when the exception contains clear auth/status signals (`401`, `403`, `Unauthorized`, `Forbidden`, `invalid_api_key`, `invalid x-api-key`, `authentication`). Do not label network failures or malformed provider responses as auth errors.
- For default GitHub Copilot failures, use `GetAuthStatusAsync` unauthenticated status as the primary signal. The fallback formatter should recognize only clear SDK-auth signals (`401`, `403`, `Unauthorized`, `Forbidden`, `not authenticated`, `authentication`) and otherwise return the original service error message.
  - Suggested default Copilot message: `GitHub Copilot authentication failed. Run gh auth login or refresh your GitHub sign-in, then restart or refresh UnityCodeAgent.`
  - Suggested BYOK message: `BYOK provider authentication failed. Check the BaseUrl and ApiKey in UnityCodeAgent settings, then refresh models again.`
- Add a small Unity-side helper in `ChatEditorWindowClient` for auth failures, for example `EnqueueAuthFailureUpdates(message, stackTrace)`, so all chat operation failures use the same update ordering.
- When the caught exception is an auth failure, enqueue both:
  - a `ChatShowErrorUpdate` so the failure remains in the transcript as an error;
  - a `ChatShowProgressMessageUpdate` with the same user-facing auth guidance so the chat window also shows the auth state through the progress surface.
- Order the auth progress update after `ChatSetBusyStateUpdate(false)`, or adjust `ChatProgressMessages` only if needed, so the progress text is not removed by normal busy-state cleanup in the same update batch.
- Apply the chat-progress behavior to prompt submission, opening/reconfiguring sessions, and initialization paths that can hit Copilot/BYOK auth errors. Keep model-refresh-only settings failures in the settings help box unless the chat window operation also observes the same auth failure.
- Keep non-auth failures unchanged: they should continue to produce normal error bubbles without duplicating every generic exception as progress.
- Leave logs unchanged or more detailed than the user response: log the original exception and properties, but never log API keys.
- Keep implementation small and direct. Do not use reflection against SDK internal exception types unless explicit auth-status preflight and clear fallback message checks prove insufficient. Do not add new UI controls, new DTO fields, or broad error-code taxonomy unless tests prove the message-only approach is insufficient.

Verification:
- Add or extend `CopilotService.Tests/AgentServiceEndpointContractTests.cs` with fake model/session services that throw auth-like exceptions and assert distinct BYOK vs GitHub Copilot messages.
- Add focused tests around the new auth-status/preflight helper with a fake runtime host or fake auth-status provider so default Copilot unauthenticated status produces the Copilot guidance without needing a live SDK runtime.
- Add `ChatEditorWindowClientE2eTests` coverage where a fake API client throws an auth-shaped `AgentServiceApiException`; assert the result includes both `ChatShowErrorUpdate` and `ChatShowProgressMessageUpdate` with the provider-specific auth message.
- Add or extend `ChatEditorWindowUiE2eTests` only if update ordering needs visual confirmation; assert the auth progress text remains visible after the failed operation and is still removed/replaced by the next real transcript message.
- Add a narrow `ByokOpenAiProvider` unit test if the implementation changes provider response handling directly; otherwise keep tests at the endpoint formatting boundary.
- Run `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj`.
- Run Unity EditMode tests only if Unity-side client/message handling changes; otherwise existing `HttpAgentServiceApiClientTests` already cover message propagation.

### README

- status: done
- goal: Write a concise public root `README.md` for UnityCodeAgent that explains what it is, prerequisites, installation, setup, and core usage, verified against package metadata and editor/service behavior without changing runtime behavior.
- approval: approved
- blocked: false
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
- Used the package documentation URL `https://github.com/Signal-Loop/UnityCodeAgent` for the release install example because the current `origin` remote still points at the previous private repository.

Verification:
- Checked README menu paths against `Packages/com.signal-loop.unitycodeagent/Editor/Menu/UnityCodeAgentServiceMenu.cs`.
- Checked settings/setup wording against `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettings.cs` and `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettingsEditor.cs`.
- Checked package requirements and dependencies against `Packages/com.signal-loop.unitycodeagent/package.json`.
- Checked service launch and runtime path claims against `Packages/com.signal-loop.unitycodeagent/Editor/Service/ServiceBootstrap.cs` and `Packages/com.signal-loop.unitycodeagent/Editor/Infrastructure/UnityCodeAgentPaths.cs`.
- Checked chat controls against `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatEditorWindow.cs` and `Packages/com.signal-loop.unitycodeagent/Editor/UI/ChatWindow.uxml`.


### Restart service menu item

- status: done
- goal: Add an explicit UnityCodeAgent restart-service menu action that stops the current service process and starts a fresh one through the existing bootstrap path, verified by focused EditMode tests, without racing the automatic reconnect/start behavior or duplicating service startup logic.
- approval: approved
- blocked: false
- updated: 2026-06-24
- steps:
    - [x] Add a reusable `AgentService.RestartAsync(UnityContext, CancellationToken)` path that composes existing stop/start behavior.
    - [x] Add `Tools/UnityCodeAgent/Restart Service` menu item that invokes the restart path in the background.
    - [x] Ensure restart and automatic start/reconnect share a lifecycle gate, and restart requests skip instead of queueing when the gate is already held.
    - [x] Add focused EditMode tests for explicit restart behavior and restart/start concurrency.
    - [x] Verify the existing automatic restart/reconnect tests still pass.

Completion:
- Added `AgentService.RestartAsync` and `RestartInBackground`, with a static lifecycle gate shared by start and restart so manual restart cannot interleave with automatic reconnect startup.
- Updated restart behavior so repeat restart clicks or restart during an active start are skipped instead of queued.
- Added the `Tools/UnityCodeAgent/Restart Service` menu item.
- Extended `AgentServiceRestartRecoveryTests` with explicit stop-before-start, restart-during-start skip, and duplicate-restart skip coverage.
- Verification: Unity EditMode `SignalLoop.UnityCodeAgent.Service.AgentServiceRestartRecoveryTests` passed, 12 tests; editor reflection check found `Tools/UnityCodeAgent/Restart Service`.

### Settings change should not cause session reopen when in sessions view

  - status: done
  - goal: Prevent session-bound settings changes from reopening or changing the active session while the chat window is showing the sessions list, verified by client/UI tests, while still applying the latest settings when a session is opened or a new prompt is submitted.
  - approval: approved
  - blocked: false
  - updated: 2026-06-23
  - steps:
      - [x] Gate automatic session-request refresh while `_isShowingSessions` is true.
      - [x] Keep the model label/live display update behavior appropriate for the current view without opening a session from the sessions list.
      - [x] Ensure opening an existing session from the list uses the latest settings and updates the stored session request signature.
      - [x] Ensure submitting a prompt from the sessions view still creates a new session with the latest settings.
      - [x] Add focused client tests proving settings changes in sessions view do not call `OpenSessionAsync` until the user opens a session or submits a prompt.
    ```md
    Research:
    - `ChatEditorWindowClient.ShowSessionsAsync(...)` sets `_isShowingSessions = true` and returns `ChatShowSessionsUpdate`.
    - `ChatEditorWindow.HandleEditorUpdate()` calls `DrainUpdates(UnityCodeAgentSettings.GetUnityContext())` every editor update.
    - `DrainUpdates(...)` calls `StartSessionRequestRefreshIfNeeded(context)` before draining service events.
    - `StartSessionRequestRefreshIfNeeded(...)` currently skips refresh only while already refreshing, busy, hydrating history, or inside the 500 ms throttle; it does not check `_isShowingSessions`.
    - `RefreshSessionRequestAsync(...)` calls `EnsureSessionRequestAppliedAsync(...)`.
    - `EnsureSessionRequestAppliedAsync(...)` computes the latest session request signature and calls `_service.OpenSessionAsync(context, _activeSessionId, ...)` when the signature differs and an active session id exists.
    - Therefore, changing session-bound settings while the sessions list is visible can reopen the previously active session even though the user is browsing sessions.
    - `OpenSessionAsync(...)` already uses the current `UnityContext`, updates `_activeSessionRequestSignature`, and sets `_isShowingSessions = false`, which is the right point to apply settings for a chosen session.
    - `SubmitPromptAsync(...)` calls `EnsureSessionRequestAppliedAsync(...)` before clearing sessions-view state; when `_isShowingSessions` is true it then clears `_activeSessionId` and creates a new session, so implementation should avoid reopening the previous active session on this path.

    Plan:
    - Add `_isShowingSessions` to the guard in `StartSessionRequestRefreshIfNeeded(...)` so editor update polling cannot reopen a session while the sessions list is visible.
    - Adjust `SubmitPromptAsync(...)` ordering if needed so a prompt submitted from sessions view does not run `EnsureSessionRequestAppliedAsync(...)` against the previous active session before clearing `_activeSessionId`.
    - Preserve `OpenSessionAsync(...)` behavior so selecting a listed session applies the latest context, refreshes `_activeSessionRequestSignature`, updates model label, leaves sessions view, and renders that session history.
    - Preserve the existing model/skill change behavior outside sessions view, including `ModelChange_ReconfiguresActiveSessionBeforeNextPromptAndUpdatesLabel`, `SkillChange_ReconfiguresActiveSessionBeforeNextPrompt`, and `LiveDebugChange_DoesNotReconfigureActiveSessionBeforeNextPrompt`.
    - Add a `ChatEditorWindowClientE2eTests` case using a recording API client: initialize session, show sessions, change model or skill context, call `DrainUpdates`, and assert no additional `open:*` operation occurs while still in sessions view.
    - Add a second assertion or test that selecting a session after the settings change performs exactly one `open:selected-session` with the new context.
    - Add or extend the sessions-view submit test so changing settings while in sessions view followed by prompt submission creates/sends on a new session without reopening the previous active session.

    Verification:
    - Run Unity EditMode tests for `ChatEditorWindowClientE2eTests` focused on model/skill session refresh and sessions-view behavior.
    - If UI behavior changes beyond client state, run the targeted `ChatEditorWindowUiE2eTests` sessions-list tests.

    Completion:
    - Added sessions-view guards so editor update polling does not reopen a session while browsing sessions, while still publishing changed model labels in that view.
    - Prompt submission from sessions view clears the previous active session before applying session-bound request state.
    - Added `SessionsView_ModelChange_DoesNotReopenUntilSessionSelected` and `SessionsView_ModelChange_SubmitPromptCreatesNewSessionWithLatestModel`.
    - Verified with Unity EditMode `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests`: passed 16, failed 0.
    ```


### Service should quit if not in manifest

  - status: done
  - goal: Ensure an older service process exits when its manifest is replaced by another service process for the same project, so there are no orphaned processes, verified by focused service lifecycle tests and a real bootstrap-style check where practical, while preserving owned manifest cleanup behavior.
  - approval: approved
  - blocked: false
  - updated: 2026-06-23
  - steps:
      - [x] Add a service-side manifest ownership check that reads the current endpoint manifest and stops the process if the manifest belongs to a different service process for the same project.
      - [x] Run the ownership check after manifest publication and periodically during service lifetime so an orphaned process exits after another service wins the manifest.
      - [x] Preserve `DeleteIfOwned(Environment.ProcessId)` semantics so a losing process does not delete the winning process manifest on shutdown.
      - [x] Add focused service tests for matching manifest ownership, replaced manifest ownership, missing manifest, malformed manifest, and different-project manifest behavior.
      - [x] Add or extend a Unity bootstrap/E2E test with two real service starts for the same project when feasible, asserting only the manifest-owned service remains healthy/running.
    ```md
    Research:
    - `EndpointManifest` already includes `projectRoot`, `projectId`, `unityProcessId`, `serviceProcessId`, `port`, and `streamGenerationId`.
    - The service writes the manifest from `ServiceRuntimeLifecycle.OnStarted()` through `EndpointManifestStore.WriteAsync(...)`.
    - `EndpointManifestStore.WriteAsync(...)` atomically replaces any existing manifest, so a later service start can overwrite an earlier service manifest.
    - Service shutdown already calls `EndpointManifestStore.DeleteIfOwned(Environment.ProcessId)`, which avoids deleting another service's manifest.
    - There is no service-side check that the current manifest still names `Environment.ProcessId` after another process replaces it.
    - Unity bootstrap rejects stale manifests by project, Unity process id, service process liveness, and `/health`, then deletes the unusable manifest before starting a new service.
    - `ParentProcessMonitor` only watches the Unity parent process identity and does not check manifest ownership.
    - Existing real-process coverage in `ServiceBootstrapE2eTests` verifies one service publishes a manifest, but does not currently start competing service processes.

    Plan:
    - Extend `EndpointManifestStore` with a small read method that returns the current manifest identity needed for ownership checks: project root/id and service process id.
    - Add a hosted manifest ownership monitor or extend runtime lifecycle with a timer-backed check that starts after successful publication.
    - Treat a manifest for the same project with a different positive `serviceProcessId` as a lost-ownership signal; log a warning and call `IHostApplicationLifetime.StopApplication()`.
    - Treat missing or temporarily unreadable/malformed manifests conservatively: log at debug/warning and retry rather than immediately exiting, unless implementation evidence shows immediate exit is safer.
    - Keep different-project manifests from triggering shutdown, so shared parent directories or test temp folders do not stop unrelated services.
    - Avoid killing external processes directly. The losing service should shut itself down through host lifetime, letting existing cleanup rules protect the winning manifest.
    - Add focused `CopilotService.Tests` coverage around the new ownership decision logic and manifest store parsing using temporary paths.
    - If the real-process Unity E2E test is stable enough, start a first service, intentionally force a second start for the same temp project, wait for manifest ownership to settle, and assert the first service is no longer healthy or has exited while the manifest names the second process.

    Completion:
    - Added `EndpointManifestStore.ReadCurrentIdentity()` so service-side lifecycle code can read `projectRoot`, `projectId`, and `serviceProcessId` without throwing on missing, locked, or malformed manifests.
    - Added hosted `ManifestOwnershipMonitor`, registered with the service host. It keeps running for missing, malformed, or different-project manifests, and calls `IHostApplicationLifetime.StopApplication()` only when the same project manifest names a different positive service process id.
    - `EndpointManifestStore.DeleteIfOwned(...)` now uses the same identity reader and still only deletes the manifest when it is owned by the current service process.
    - Added `ManifestOwnershipMonitorTests` for current ownership, replaced ownership, missing manifest, malformed manifest, different-project manifest, and non-owned cleanup preservation.
    - Skipped adding a real two-process Unity bootstrap E2E test in this pass because `ServiceBootstrapE2eTests` currently covers a single real service start; forcing two competing real service processes would add slower and more brittle editor-process coverage. Residual risk is limited to real process timing around the 2-second monitor interval, while ownership decision logic and DI registration are covered by service tests.

    Verification:
    - Passed: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj --filter Manifest --artifacts-path .artifacts/tests/manifest-ownership` (6 passed, 0 failed).
    - Passed: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj` (31 passed, 0 failed).
    - Noted: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj --artifacts-path .artifacts/tests/service-all-manifest-ownership` failed in 9 existing contract tests because `ContractSpecExampleCatalog` looked for `contracts/openapi/agent-service.openapi.yaml` under `.artifacts/tests`; the same full suite passed without custom artifacts path.
    ```

### CreateRequestSignature and CreateSignature code is duplicated in SessionRequestFactory and CopilotSessionManager

  - status: done
  - goal: Centralize the deterministic session request signature in shared contracts so Unity-side session request tracking and service-side attached-session reuse use one implementation, verified by focused Unity/service tests, without changing request JSON shape or provider signature semantics.
  - approval: approved
  - blocked: false
  - updated: 2026-06-22
  - steps:
      - [x] Add a shared session request signature helper to `Packages/com.signal-loop.unitycodeagent/Editor/Contracts/ServiceContracts.cs`.
      - [x] Replace `SessionRequestFactory.SessionRequestOptions.CreateSignature` with the shared helper.
      - [x] Replace `CopilotSessionManager.CreateRequestSignature` and its local append helpers with the shared helper.
      - [x] Verify whether `ProviderConfigDto.CreateSignature` is still the right provider-level helper; keep it if provider signatures remain a distinct concept.
      - [x] Add or update focused tests for deterministic ordering and service-side reconfiguration behavior.
    ```md
    Research:
    - The named task started in Backlog and was explicitly requested for preparation.
    - `SessionRequestFactory.SessionRequestOptions` computed `Signature` by appending provider signature, working directory, sorted skill directories, sorted disabled skills, and sorted tool identity (`Name`, `Description`, `InputSchemaJson`).
    - `CopilotSessionManager` duplicated the same request signature algorithm to decide whether an already attached runtime session could be reused or must be reopened.
    - `Packages/com.signal-loop.unitycodeagent/Editor/Contracts/ServiceContracts.cs` is compiled into the Unity editor assembly and linked into `UnityCodeCopilot.Service.csproj`, so it is a viable shared location.
    - `ProviderConfigDto.Signature` and `ProviderConfigDto.CreateSignature` are provider-level signatures that hash the API key and normalize base URL/type/wire API. They are still used by the shared request signature algorithm and should remain as the provider-scoped primitive.

    Completion:
    - Added `AgentSessionRequestSignature` to shared contracts with the existing length-prefixed field encoding and normalization rules.
    - `SessionRequestFactory.SessionRequestOptions` now calls the shared helper for Unity-side active-session request tracking.
    - `CopilotSessionManager` now calls the shared helper for service-side attached-session reuse/reconfiguration checks, and its duplicated private request signature helpers were removed.
    - Added `AgentSessionRequestSignatureTests` covering deterministic ordering/whitespace normalization and changes to provider, working directory, disabled skills, and tool schema.

    Verification:
    - Passed: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj --filter "CopilotSessionManagerTests|AgentSessionRequestSignatureTests" --artifacts-path .artifacts/tests/shared-session-signature` (6 passed, 0 failed).
    - Passed: Unity EditMode tests `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests.SkillChange_ReconfiguresActiveSessionBeforeNextPrompt`, `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests.ModelChange_ReconfiguresActiveSessionBeforeNextPromptAndUpdatesLabel`, and `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests.LiveDebugChange_DoesNotReconfigureActiveSessionBeforeNextPrompt` (3 passed, 0 failed).
    ```

### Split settings into session-bound and live

  - status: done
  - goal: Separate service-restart-bound settings from user-live settings in the inspector, while internally tracking session-reopen-bound settings, verified by focused settings/session tests and launch-command coverage without changing the serialized settings asset shape unnecessarily.
  - approval: approved
  - blocked: false
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

### Ensure logs roll over

  - status: done
  - goal: Add bounded size-based log rotation for Unity and service logs, verified under concurrent read/write expectations without blocking the Unity UI.
  - approval: approved
  - blocked: false
  - updated: 2026-06-22
  - steps:
      - [x] Add a shared or parallel size-based rotation helper for active log path, maximum bytes, and retained suffix count.
      - [x] Apply rotation before appending Unity file logs and include an ISO timestamp in Unity file log lines.
      - [x] Apply the same bounded rotation behavior before appending service file logs.
      - [x] Add focused Unity EditMode coverage for Unity logger timestamping and rollover file retention.
      - [x] Add focused `CopilotService.Tests` coverage for service logger rollover and retained file naming.
    ```md
    Research:
    - Unity client logger is `Packages/com.signal-loop.unitycodeagent/Editor/Logging/UnityCodeAgentLogger.cs`.
    - Unity file logs append to `.unityCodeAgent/client/logs/unity.log` using `FileStream(..., FileShare.ReadWrite)` under a static `GlobalFileSync` lock.
    - Unity logger currently formats console and file output identically and has a TODO to include timestamps in file logs.
    - Service logger is `Packages/com.signal-loop.unitycodeagent/Editor/CopilotService~/Settings/UnityCodeCopilotServiceLogger.cs`.
    - Service file logs append to `.unityCodeAgent/service/logs/service.log` using `File.AppendAllText` under an instance lock.
    - Service lines already include `DateTimeOffset.UtcNow:O`; Unity lines do not.
    - Service options currently expose `LogToFile` and `MinLogLevel`, but no rotation settings.
    - Existing service tests live in `CopilotService.Tests`; Unity editor tests for launch/log settings live under `Assets/Tests/Editor/Service`.

    Plan:
    - Keep the public settings surface small for the first pass: use internal constants unless product settings are explicitly requested.
    - Prefer a small log rotation helper that can be used from both logger implementations if assembly boundaries allow it; otherwise duplicate only the minimal algorithm to avoid widening package dependencies.
    - Rotate while holding each logger's existing write lock so rename/delete decisions cannot race with writes from the same process.
    - Use a bounded naming scheme such as `unity.log.1`, `unity.log.2` and `service.log.1`, `service.log.2`; delete the oldest file when retention is exceeded.
    - Check active file size before append; if the next line would exceed the limit, shift retained files and start a new active file.
    - Preserve `FileShare.ReadWrite` for Unity writes so external readers and tools can inspect active logs while Unity is running.
    - Add a test-only constructor or narrowly scoped overload if needed to inject a temporary log path and small rotation threshold without touching global project settings.
    - Add Unity EditMode tests that write enough lines to force multiple rotations, verify retained file count, verify active log remains writable/readable, and verify file lines include timestamps.
    - Add service unit tests that instantiate `UnityCodeCopilotServiceLogger` with temporary `ProjectPaths` and small rotation settings/overload, then assert active and retained files are bounded.

    Verification:
    - Passed: Unity EditMode tests `SignalLoop.UnityCodeAgent.Service.UnityCodeAgentLoggerTests.FileLogging_IncludesTimestampAndRollsOverWithRetention` and `SignalLoop.UnityCodeAgent.Service.UnityCodeAgentLoggerTests.FileLogging_AllowsConcurrentReadWhileWriting`.
    - Passed: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj --filter UnityCodeCopilotServiceLoggerTests --artifacts-path .artifacts/tests/logs-rollover`.

    Completion:
    - Unity and service loggers now rotate active logs before appending when the next line would exceed the bounded size.
    - Both loggers retain a small suffix chain (`.1`, `.2`, `.3` by default) and preserve read sharing while writing.
    - Unity file logs now include an ISO-8601 timestamp while Unity console output keeps the previous non-timestamped format.
    ```


### Model management

  - status: done
  - goal: Automatically apply provider/model changes to the active chat session, verified by restart behavior and footer model label updates.
  - steps:
      - [ ] Verify current behavior when provider/model is changed during an active session.
      - [ ] Implement automatic session restart and provider/model switch when the provider/model is changed.
      - [ ] Add label with current provider/model in the chat window footer that updates when provider/model is changed.
    ```md
    Currently after provider is changed, the session needs to be restarted to work with the new provider.

    Expected behaviour:
    When provider/model is changed, the current session is restarted automatically and the new provider/model is used for subsequent prompts without requiring manual restart.

    Footer label detail:
    When label is clicked, Settings asset is pinged and opened in inspector.
    ```

### Copy Unity code mcp server tools to Agent

  - status: done
  - goal: Bundle Unity Code MCP server tools into the Agent package, verified by tool availability without depending on the external UnityCodeMCPServer package.
    ```md
    Currently Agent uses UnityCodeMCPServer to use unity tools as part of the agent's features.

    Expected behaviour:
    Unity code mcp server tools are copied to Agent, so that they can be used without depending on the UnityCodeMCPServer package.
    ```

### Show progress bessages for better UX

  - status: done
  - goal: Surface startup and recovery progress in chat via progress handlers, verified by E2E tests and targeted Unity editor checks.
  - steps:
      - [ ] Use ShowProgressMessageHandler from ChatEditorWindow.
      - [ ] Pass it as method parameter to service method that need to report progress.
      - [ ] Replace selected persisted service messages with progress messages.
      - [ ] Add useful startup and long-operation progress messages.
      - [ ] When opening chat window, disable buttons instead of the full window.
    ```md
    Use progress messages instead of:
    - `PublishServiceEvent(onEvent, lastSessionId, "Agent service connection restored.");`
    - `"Agent service event stream ended. Restarting service connection."`

    Add messages like Starting agent service, Agent service started and other messages that can be useful for user to understand what is going on, especially when there are long operations or opening chat window.

    Plan thoroughly how to implement those changes, follow best architecture practices and clean code and isolation practices.
    Follow KISS and YAGNI principles, do not overengineer, keep it simple and maintainable.
    Implement tests for new features, use E2E tests to verify features, if proper messages are displayed in the right way.
    Use `execute_csharp...` tool for verification, use mocking service for tests and verification.
    ```

### Improve user expeorience by adding progress messages in the chat

  - status: done
  - goal: Implement ephemeral progress chat messages that replace or clear correctly, verified by UI E2E coverage and focused helper tests.
  - steps:
      - [ ] Add Progress to AgentEventType.
      - [ ] Use Progress to report ephemeral status and loading messages.
      - [ ] Replace or remove trailing progress messages correctly.
      - [ ] Create new template for progress message based on tool message.
      - [ ] Show progress message instead of Service message in stream recovery paths.
      - [ ] Create delayed waiting-response progress helper.
      - [ ] Implement focused tests and E2E verification.
    ```md
    When showing UI, progress messages are appended in ChatEditorWindow as follows:
    - existing messages are checked if last message is progress message
    - if it is, it is replaced with the new message, if not, the new message is added to the end of the list
    - if next message is added, and progress message is last, it is removed, so that progress messages are not mixed with regular messages and are always up to date

    Show progress message instead of Service message in PublishStreamRecoveryEvents in AgentsService.
    Add Progress message when service start is finished in StreamEventsAsync in AgentsService.

    Create progress message feature that spawns progress messages when waiting for response from service:
    - after 1 second after last message is displayed start showing `Thinking...`, `Analyzing...`, `Waiting for response...` random messages
    - update them after 1 second
    - create separate helper for this feature and use it in ChatEditorWindow, which should manage this

    Use single responsibility - create separate helpers for separate features.
    Keep it small and simple, do not overengineer, do not create complex system for progress messages, keep it simple and easy to maintain.

    Current problem:
    There is no user feedback for long running operations, like starting/restarting service, waiting for response, etc.

    Expected behaviour:
    User sees status messages when long operation is started or there are no messages for longer time. ChatShowAgentEventUpdate is used to publish these messages.
    ```

### Add skills/mcp to settings UI

  - status: done
  - goal: Expose skill folders, skill toggles, and MCP config access in settings UI, verified by settings behavior and file-opening interactions.
  - steps:
      - [x] Add Settings button on the right of sessions button in chat window UI.
      - [x] Add configurable skill folders with `.agents/skills` as default.
      - [x] List skill names from configured folders with include/exclude toggles.
      - [x] Add MCP configuration path to settings and open it in a text editor when clicked.
    ```md
    Settings button is always visible.
    When clicked, it pings settings asset and opens it in inspector.

    Skills folder paths are project relative.
    Skills from configured folders are used as context for the session.
    Reference: https://github.com/github/copilot-sdk/blob/main/docs/features/skills.md

    Skill names from the configured folders are listed in the settings.
    They have on/off toggle to include/exclude them from the context, and when clicked, skill file is opened in a text editor.
    MCP path is not editable.

    Expected behaviour:
    Settings contain Skills, MCP, user can add/remove skill folders, toggle skills, and open mcp configuration file.
    ```

### Improve service lifecycle management and session recovery

  - status: done
  - goal: Recover active sessions across local service restarts, verified by restart/reopen behavior without surfacing stale unavailable-session errors.
  - steps:
      - [x] Research current behavior when the service is stopped during an active session and a new prompt is sent.
      - [x] Create plan for improving session management to handle service restarts gracefully, including automatic service restart and session recovery.
      - [x] Plan is accepted by user.
      - [x] Implement improvements to session management and service lifecycle handling based on the plan.
    ```md
    Current behavior:
    When the service is stopped during a session, an error is thrown and displayed.
    When the next prompt is sent, an error indicating the session is unavailable is thrown and displayed.

    Expected behaviour:
    Unity client manages gracefully service lifecycle and restarts the service, short messages about it can be displayed in chat, reopens the current session, then continues.
    ```
