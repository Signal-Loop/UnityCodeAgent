# Auto-Restart Session on Model Change — Design Spec

**Date:** 2026-06-15
**Status:** Approved for implementation planning

## Problem

`UnityCodeAgentSettings.Model` is selected via the settings inspector, but it is only
read at session-open time (`SessionRequestFactory.CreateOptions` reads it once during
`CreateSessionAsync` / `OpenSessionAsync` / `GetCurrentSessionAsync`). After a session
is open, switching the model in the inspector does not affect the running session —
the assistant keeps using the old model until the user manually restarts the chat
window or starts a new session.

Additionally, the chat window shows no indication of which model is in use, so users
have to open the settings asset to find out.

### Expected behavior

- When the user picks a different model in the settings inspector while the chat
  window is open, the current session is **silently reopened with the new model**.
  History is preserved, the transcript is not reloaded, and the user sees only a
  brief progress message.
- If the model is changed mid-turn, the swap is deferred until the current response
  becomes idle, then the reopen happens.
- The chat window footer shows the active model at all times. Clicking the label
  pings the settings asset in the project window.
- The selected model is always the model the service uses for the next prompt — even
  when the swap was deferred.

## Solution

Drive model changes from a small, focused event in `UnityCodeAgentSettings`, plumb
it through `ChatEditorWindowClient` (which owns session lifecycle), and surface the
new active model in the chat window via a new `ChatClientUpdate`. The reopen
mechanism reuses the existing `AgentService.OpenSessionAsync` path, which already
reads the current model from settings at call time.

### Architecture

```
[Inspector]            [Settings]                [Client]                  [Window]
SelectModel ──> SetModel ──> ModelChanged event ──> handler ──> ChatSetActiveModelUpdate ──> SetActiveModelLabel
                                                       │
                                                       └──> AgentService.OpenSessionAsync(_activeSessionId)
                                                              (reopen with current settings.Model)
```

The client owns session lifecycle and is the single place that interprets
`ModelChanged`. The window only reacts to a presentation-layer update.

### Components

#### 1. `UnityCodeAgentSettings` (modified)

- Add a public static `event Action ModelChanged;`.
- Add `public void SetModel(ModelInfoDto model)` that assigns `Model`, calls
  `EditorUtility.SetDirty(this)`, and raises `ModelChanged`. This is the single
  write path used by the inspector.
- `SetAvailableModels` keeps its existing direct assignment for `AvailableModels`
  bookkeeping, but **must raise `ModelChanged` if the resolved `Model` reference
  changes** (including the case where the prior model is no longer in the
  refreshed list and is set to `null`). The chat window's label must always
  reflect the actual model — a "Refresh models" that nulls out a stale selection
  must update the label and, if a session is open, reopen it.
- The static event is what the client subscribes to. Subscribers must unsubscribe
  on dispose. (The instance is a `ScriptableObject` loaded from disk, so the
  static event will hold a strong reference; the client must manage its own
  subscription lifetime.)

#### 2. `UnityCodeAgentSettingsEditor` (modified)

- `SelectModel(settings, model)` calls `settings.SetModel(model)` instead of
  `settings.Model = model`. The rest of the inspector (`RefreshModels`,
  `SetAvailableModels`) is unchanged.

#### 3. `ChatEditorWindowClientContracts` (extended)

- Add `public sealed class ChatSetActiveModelUpdate : ChatClientUpdate` with
  `ModelInfoDto? Model` (nullable so the label can show "No model selected").

#### 4. `ChatEditorWindowClient` (modified)

- Subscribe to `UnityCodeAgentSettings.ModelChanged` in the constructor.
- Unsubscribe in `Dispose`.
- Maintain a `private ModelInfoDto? _pendingModelChange;` to defer swaps mid-turn.
- Add a private `HandleModelChanged` method:
  - Read `settings.Model` once into a local.
  - Always enqueue `ChatSetActiveModelUpdate(local)`.
  - If `_activeSessionId` is empty: do nothing else; the next `SubmitPromptAsync`
    calls `CreateSessionAsync`, which reads `settings.Model.Id` from the factory
    at that moment.
  - Else if `_isBusy`: store `local` in `_pendingModelChange`. Show progress
    `"Will switch model after current response"`.
  - Else: call a new `ReopenActiveSessionAsync()` (see below).
- In `ApplyServiceEvent`, when handling `SessionIdle` for the active session and
  `_pendingModelChange.HasValue`: clear the pending value, call
  `ReopenActiveSessionAsync()`. This is the only place the busy branch resolves.
- `ReopenActiveSessionAsync`:
  - Calls `_service.OpenSessionAsync(_activeSessionId, ct)`.
  - Updates `_replayAfterSequenceNumber = response.LastEventId`.
  - Does **not** enqueue a `ChatShowMessagesUpdate` (no history reload).
  - Shows progress `"Switching model..."` via `_showProgressMessage`.

The new `OpenSessionAsync` call inside `ReopenActiveSessionAsync` reuses the
existing service path. The factory reads the current `settings.Model.Id` at the
time of the call, so the service-side session is rebound to the new model
transparently.

#### 5. `ChatEditorWindow` (modified)

- `BuildUi` resolves a new `Label` named `model-label` from the UXML tree.
- `ApplyUpdate` handles `ChatSetActiveModelUpdate` by calling a new
  `SetActiveModelLabel(model)`.
- `SetActiveModelLabel(model)` renders `"Model: <Name>"` or `"No model selected"`
  and applies a muted style class (`chat-model-label--empty`) when null.
- `model-label` click handler pings the settings asset (refactor the existing
  `HandleSettingsButtonClicked` into a private `PingSettingsAsset()` helper and
  reuse it for both the settings button and the label).
- The label is wired up in `BuildUi` and updated on each `ChatSetActiveModelUpdate`.
  An initial update is fired after the first successful `InitializeAsync` so the
  label reflects whatever model is in `settings.Model` at startup.

#### 6. `ChatWindow.uxml` (extended)

- Add `<ui:Label name="model-label" class="chat-model-label" />` under the
  `chat-actions` element.

#### 7. `ChatWindow.uss` (extended)

- Add `.chat-model-label { font-size: 11px; opacity: 0.7; margin: 2px 0 0 0;
  -unity-text-align: lower-left; }`.
- Add `.chat-model-label--empty { opacity: 0.4; -unity-font-style: italic; }`.

### Data flow

| Step | Inspector | Settings | Client | Window | Service |
|------|-----------|----------|--------|--------|---------|
| 1 | User picks model in dropdown | | | | |
| 2 | `SelectModel` → `settings.SetModel(model)` | raises `ModelChanged` | | | |
| 3 | | | `HandleModelChanged` | | |
| 4 | | | enqueue `ChatSetActiveModelUpdate(settings.Model)` | | |
| 5 | | | if active session and not busy: `_service.OpenSessionAsync(_activeSessionId)` | | reads `settings.Model.Id`, sends reopen request |
| 6 | | | | `ApplyUpdate` → `SetActiveModelLabel` | |
| 7 | | | | label text updated; user sees progress "Switching model..." | |

When busy:

| Step | Inspector | Settings | Client | Service | Idle handler |
|------|-----------|----------|--------|---------|--------------|
| 1-4 | same as above | | | | |
| 5 | | | `_pendingModelChange = settings.Model` | | |
| 6 | | | show progress "Will switch model after current response" | | |
| 7 | | | | current response continues, eventually emits `SessionIdle` | |
| 8 | | | `ApplyServiceEvent(SessionIdle)` clears `_pendingModelChange`, calls `ReopenActiveSessionAsync` | | reads `settings.Model.Id` (still the new one) |

### Error handling

- `ReopenActiveSessionAsync` failures are surfaced via the existing
  `ChatShowErrorUpdate` path that `OpenSessionAsync` already uses. The label still
  updates to the new model because the `ChatSetActiveModelUpdate` is enqueued
  first. If reopen fails, the user can retry by picking the model again or
  restarting the window.
- `_pendingModelChange` is cleared even on reopen failure so the next `SessionIdle`
  does not retry a stale value. (Use try/finally in the idle handler.)
- The static `ModelChanged` event is best-effort. If the chat window is closed,
  the client is disposed and the subscription is removed — no leak.

### Testing

#### EditMode tests (`ChatEditorWindowClientE2eTests.cs`)

1. `ModelChanged_WithActiveSession_ReopensSession`
   - Setup: initialized client with an active session.
   - Act: raise `ModelChanged` with a new model.
   - Assert: a `ChatSetActiveModelUpdate` is enqueued; `OpenSessionAsync` was
     called and the request carried the new model id; no `ChatShowMessagesUpdate`
     was enqueued (no history reload).
2. `ModelChanged_WhileBusy_DefersReopenUntilIdle`
   - Setup: client busy, mid-turn.
   - Act: raise `ModelChanged`.
   - Assert: no `OpenSessionAsync` yet; `ChatSetActiveModelUpdate` enqueued;
     `_pendingModelChange` set.
   - Act: deliver `SessionIdle`.
   - Assert: `OpenSessionAsync` was called with the new model id; pending value
     cleared.
3. `ModelChanged_WhileBusy_TwoSwaps_OnlyLastOneIsApplied`
   - Setup: client busy.
   - Act: raise `ModelChanged` with model A, then with model B.
   - Act: deliver `SessionIdle`.
   - Assert: only one `OpenSessionAsync` call, with model B.
4. `ModelChanged_NoActiveSession_OnlyUpdatesLabelAndNextPromptUsesNewModel`
   - Setup: client with no `_activeSessionId`.
   - Act: raise `ModelChanged`; submit a prompt.
   - Assert: `CreateSessionAsync` request carried the new model id; label update
     was enqueued.
5. `ModelChanged_OnDispose_Unsubscribes`
   - Setup: client; raise `ModelChanged`; dispose; raise `ModelChanged` again
     on a stale subscription stub.
   - Assert: no callbacks after dispose.
6. `SetAvailableModels_ThatClearsModel_RaisesModelChangedAndReopensSession`
   - Setup: client with active session and a non-null `settings.Model`.
   - Act: call `settings.SetAvailableModels(emptyList)`. The method nulls
     `settings.Model` because the prior model is not in the new list.
   - Assert: a `ChatSetActiveModelUpdate` is enqueued with `Model.Id == ""`
     (the "no model selected" sentinel); the active session is reopened
     (which will fail at the service because the request has an empty model
     id — that is acceptable; the test only verifies the reopen was
     attempted via `harness.ApiOperations`).

   Note: in production, the service's reopen with an empty model id will
   fail with `InvalidOperationException` from `SessionRequestFactory`. This
   is acceptable because the alternative (silently keeping a stale label)
   is worse, and the error surfaces as a `ChatShowErrorUpdate` in the
   transcript. The test can use a mock harness where `OpenSessionAsync`
   accepts an empty model id gracefully to avoid coupling this test to
   `SessionRequestFactory` validation.

#### EditMode UI tests (`ChatEditorWindowUiE2eTests.cs`)

6. `ModelLabel_UpdatesOnModelChange`
   - Open the chat window.
   - Assert: initial label text reflects `settings.Model`.
   - Act: change `settings.SetModel(...)` to a new value.
   - Assert: label text reflects the new model; session was reopened (verify by
     using a recording API client and asserting `OpenSessionAsync` carried the
     new model id).
7. `ModelLabel_Click_PingsSettingsAsset`
   - Open the chat window.
   - Act: click the model label via `ClickEvent`.
   - Assert: `Selection.activeObject` is the settings asset. (Skip if asset is
     not created in the test environment — gate on `settings != null`.)

#### Editor settings tests (new file, optional)

8. `UnityCodeAgentSettings_SetModel_RaisesModelChanged`
   - Set up a `UnityCodeAgentSettings` instance.
   - Subscribe a counter to `ModelChanged`.
   - Call `SetModel(new ModelInfoDto("id", "name"))`.
   - Assert: counter == 1.
9. `UnityCodeAgentSettings_SetModel_SameInstance_DoesNotRaiseDuplicate`
   - Call `SetModel` with the current model.
   - Assert: counter incremented exactly once (the comparison is by reference,
     so this test verifies the same reference still raises once — not a no-op).
   - Note: a true "no-op when same value" optimization is intentionally out of
     scope to keep semantics simple; the inspector only calls `SetModel` on
     user selection.

## Out of scope (YAGNI)

- No polling tick in `ChatEditorWindow.HandleEditorUpdate` to detect model
  changes. The event-driven path is sufficient.
- No changes to `SessionRequestFactory` — it already reads the current model
  from settings on every call.
- No new SSE event types. The reopen just hits `OpenSessionAsync`.
- No retry on reopen failure. The error is surfaced once via the existing
  `ChatShowErrorUpdate` path. The user can pick the model again or restart.
- No model-change history (e.g., "you previously used GPT-4o, now Claude
  Sonnet 4"). Only the active model is tracked.
- No "reset pending change on disconnect" handling — `_pendingModelChange` is
  cleared on reopen attempt regardless of success, and a stale value cannot be
  applied because the next reopen will read the current `settings.Model` (which
  is still the latest user-picked value).
