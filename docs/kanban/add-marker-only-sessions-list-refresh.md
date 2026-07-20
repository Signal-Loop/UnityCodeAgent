# Add marker-only sessions list refresh
- status: Backlog
- order: 1200
- goal: Add live sessions-list marker refresh without caching session summaries in `ChatEditorWindowClient`, verified by focused client/UI tests while keeping session list ownership simple.
- updated: 2026-07-07
- steps:
    - [ ] Research current `ChatShowSessionsUpdate` and sessions-list rendering responsibilities
    - [ ] Design a marker-only client update for changed-session ids
    - [ ] Implement UI row marker refresh without rebuilding from cached session DTOs
    - [ ] Add focused tests for live marker refresh while sessions list is open

When the sessions list is open and a background event marks a session changed, the UI could update row marker styling immediately. The previous `_visibleSessions` approach cached the last rendered `SessionSummaryDto` list in `ChatEditorWindowClient` so it could emit another full `ChatShowSessionsUpdate`. That adds avoidable client-side render cache state.

Future implementation should prefer a cleaner update shape, for example `ChatUpdateSessionMarkersUpdate`, that carries only changed-session ids. `ChatEditorWindow` can then update the existing sessions-list rows in place. `ShowSessionsAsync` should remain the only path that fetches and sends full session summaries.
Prefer logic not UI oriented name, like ChatSessionChangedUpdate, not to leak UI details into the Client/Service contract. The client can then decide how to update the UI, whether by refreshing markers or rebuilding the list.
