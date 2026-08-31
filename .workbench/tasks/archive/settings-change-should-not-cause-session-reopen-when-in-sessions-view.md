# Settings change should not cause session reopen when in sessions view
- status: Completed
- order: 2100
- goal: Prevent session-bound settings changes from reopening or changing the active session while the chat window is showing the sessions list, verified by client/UI tests, while still applying the latest settings when a session is opened or a new prompt is submitted.
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

