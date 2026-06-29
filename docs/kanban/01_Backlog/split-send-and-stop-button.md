# Split Send and Stop button

- goal: Separate Send and Stop controls so busy sessions abort through Stop instead of submitting again, verified by UI E2E coverage.

Current UI has one `send-button` that changes text to `Stop` while busy, but click still calls `SubmitPromptAsync`.

Add a separate `stop-button` in UXML/USS and wire it to `AbortPromptAsync` for the active session.
Keep Send disabled while busy; keep Stop visible/enabled only when the current session is busy.
Cover with UI E2E: submit prompt, busy state shows Stop, Stop sends abort instead of another prompt.

