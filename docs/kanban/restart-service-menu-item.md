# Restart service menu item
- status: Completed
- order: 1800
- goal: Add an explicit UnityCodeAgent restart-service menu action that stops the current service process and starts a fresh one through the existing bootstrap path, verified by focused EditMode tests, without racing the automatic reconnect/start behavior or duplicating service startup logic.
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

