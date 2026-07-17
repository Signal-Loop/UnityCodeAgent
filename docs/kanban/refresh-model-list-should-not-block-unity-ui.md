# Refresh model list should not block Unity UI
- status: Completed
- order: 1700
- goal: Refreshing the settings model list runs without blocking the Unity editor UI, reports service startup and model-loading progress in the existing model info box, and is verified by focused Unity editor tests without changing the service API contract.
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

