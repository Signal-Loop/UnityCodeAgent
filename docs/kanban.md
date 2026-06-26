# UnityCodeCopilot Kanban Board

## Backlog

_Ideas for tasks. Not to be modified without specific request._

### Add missing lifecycle/progress chat messages

- status: backlog
- goal: Cover remaining silent long operations with transient progress messages, verified by focused UI/client tests, while avoiding persisted transcript noise.

Current progress plumbing exists: `AgentEventType.Progress`, `ChatProgressMessages`, progress template, and client progress callback are in place.

Audit remaining long operations that still feel silent:
- model refresh
- session list load
- session open
- service stop/restart
- tool invocation wait
- settings/model validation failures

Prefer progress updates over persisted transcript `Service` messages for transient state.
Add focused UI/client tests only for newly covered flows.

### Split Send and Stop button

- status: backlog
- goal: Separate Send and Stop controls so busy sessions abort through Stop instead of submitting again, verified by UI E2E coverage.

Current UI has one `send-button` that changes text to `Stop` while busy, but click still calls `SubmitPromptAsync`.

Add a separate `stop-button` in UXML/USS and wire it to `AbortPromptAsync` for the active session.
Keep Send disabled while busy; keep Stop visible/enabled only when the current session is busy.
Cover with UI E2E: submit prompt, busy state shows Stop, Stop sends abort instead of another prompt.

### Filter session by working directory

- status: backlog
- goal: Show and operate on sessions for the active working directory only, verified by session list behavior tests without losing access to existing sessions.

### Improve transcript scroll behavior

- status: backlog
- goal: Make transcript scrolling predictable during new messages and async updates, verified by focused UI behavior tests without introducing layout jumps.

### Ollama is supported

- status: backlog
- goal: Support local OpenAI-compatible Ollama endpoints over loopback HTTP, verified by fake `/models` responses, while keeping non-loopback BYOK URLs HTTPS-only.

Current BYOK validation requires HTTPS, which blocks common local Ollama URLs like `http://127.0.0.1:11434/v1`.
Current BYOK model listing uses OpenAI-compatible `/models`, which should fit Ollama's OpenAI-compatible endpoint.

Add explicit provider type/wire API settings if needed; do not special-case beyond OpenAI-compatible local provider support.
Allow loopback HTTP for local providers while keeping non-loopback BYOK URLs HTTPS-only.
Add model-list test using a fake OpenAI-compatible `/models` response.

### Service logs are JSONL and replayable in tests

- status: backlog
- goal: Service events can be replayed using service log, which is JSONL, verified by parser/replay tests and at least one fixture from real event envelopes.

Current service file log is plain text `service.log`; SDK telemetry can write `telemetry.jsonl`.

Add stable JSONL schema.
Add parser/replay helper in tests and one fixture generated from real event envelopes.
Parser has editor window UI that can be opened from menu.
Parser UI refreshes on demand and lists session names based from log. selected session service events can be replayed.
Button 'Copy to file' copies selected session events to a separate JSONL file for later replay. Ensure that this file is not deleted when rolling logs.
Add focused tests for parser/replay and one fixture from real event envelopes.

## Planning

_Tasks with this status should be researched against the codebase and planned. A task in planning can move to To Do once it has a specific implementation plan and is approved for implementation._

## To Do

_Tasks in this status are ready to be implemented. They have been researched and planned, and can move to In Progress when work begins._

## In Progress

## Done

### Refresh model list should not block Unity UI

- status: done
- goal: Refreshing the settings model list runs without blocking the Unity editor UI, reports service startup and model-loading progress in the existing model info box, and is verified by focused Unity editor tests without changing the service API contract.
- approval: required
- blocked: false
- updated: 2026-06-26
- steps:
    - [x] Make settings model refresh asynchronous from the editor event path.
    - [x] Route existing `AgentService` progress messages into the model refresh info box.
    - [x] Guard duplicate refresh clicks and cancellation/disposal edge cases.
    - [x] Add focused Unity EditMode coverage for non-blocking refresh state and progress.
    - [x] Run targeted Unity tests for the settings refresh behavior.

Research:
- The blocking call is in `Packages/com.signal-loop.unitycodeagent/Editor/Settings/UnityCodeAgentSettingsEditor.cs`: the Refresh button calls `RefreshModels(settings)`, and `RefreshModels` synchronously waits on `service.GetModelsAsync(...).GetAwaiter().GetResult()`.
- `AgentService.GetModelsAsync` already emits relevant progress through its constructor callback: authentication check, `Loading agent models...`, service start, reconnect, and failure messages.
- The settings editor currently creates `new AgentService()` without a progress callback, so those messages never reach the existing `_modelRefreshMessage` help box.
- Service startup waits for `.unityCodeAgent/service/runtime/endpoint.json` for up to 60 seconds in `ServiceBootstrap.WaitForManifestAsync`; doing this from `OnInspectorGUI` freezes the editor path.
- Existing restart/model tests cover `AgentService.GetModelsAsync` retry behavior, so the implementation should not change `AgentService` or the `/api/models` contract for this task.

Plan:
- Replace the synchronous `RefreshModels` call from the settings inspector with an async refresh coordinator owned by `UnityCodeAgentSettingsEditor`.
- Track `_modelRefreshTask`, `_modelRefreshCancellation`, and `_isModelRefreshInProgress`; disable or relabel the Refresh button while a refresh is active.
- Construct `AgentService` with a progress callback that marshals updates back to the editor thread, assigns `_modelRefreshMessage`, sets `MessageType.Info`, and calls `Repaint`.
- Capture provider validation on the editor thread before starting work; if validation fails, keep the current immediate error behavior.
- Run `GetModelsAsync` asynchronously with a cancellation token. On completion, marshal back to the editor thread to call `settings.SetAvailableModels`, mark the asset dirty, save assets, update `serializedObject`, and show the existing success/empty-result messages.
- On failure, show the exception message in the same help box with `MessageType.Error`; on editor disable, cancel any outstanding refresh and avoid touching disposed editor state after cancellation.
- Keep progress text based on existing `AgentService` messages unless a local editor-only message is needed for the initial state, such as `Refreshing models...`.

Verification:
- Add a Unity EditMode test around a testable refresh coordinator or editor helper that proves starting refresh returns before a delayed model task completes and sets an in-progress/progress message.
- Add coverage that an `AgentService` progress callback updates the model refresh message before completion.
- Add coverage that completion persists returned models and clears the in-progress state, and that failure reports the error in the existing message surface.
- Run the targeted Unity EditMode test assembly or the narrow new tests through the Unity test runner.

Completion:
- Implemented `ModelRefreshCoordinator` and updated the settings inspector to start refresh work asynchronously, disable duplicate refresh clicks while loading, route progress into the model help box, and cancel outstanding work on editor disable.
- Added focused EditMode coverage in `ModelRefreshCoordinatorTests`.
- Verified with Unity EditMode tests: `SignalLoop.UnityCodeAgent.Service.ModelRefreshCoordinatorTests.*` passed, 5 passed / 0 failed.
