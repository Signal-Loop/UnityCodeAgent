# AGENTS

## Scope and specification

These instructions apply to `dev/kanban_viewer`. They supplement the repository-root `AGENTS.md`; follow both, with this file taking precedence in this subtree.

## Purpose and layout

This is a self-contained, Node-only local development tool for browsing and updating Markdown kanban tasks:

- `src/`: React 19, Vite, Tailwind, and dnd-kit client.
- `server/`: loopback-only Fastify service for HTTP/SSE, filesystem access, watching, and VS Code launch.
- `shared/`: TypeBox schemas, shared DTOs, and `KANBAN_STATUSES`.
- `tests/`: server and filesystem Vitest tests.
- `e2e/`: Playwright tests; frontend unit tests live beside `src/` files.

Keep the viewer independent from Unity and the Copilot service unless integration is explicitly requested. Do not reintroduce Python tooling.

## Contracts and boundaries

- Treat `shared/contracts.ts` as the source of truth for statuses and API types. Update runtime schemas, inferred types, client, server, and tests together.
- Keep these routes and their JSON fields wire-compatible: `GET /api/config`, `GET /api/board`, `PATCH /api/board/tasks`, `POST /api/editor/open`, and `GET /api/events`.
- Preserve existing `400`, `409`, `422`, and `503` mappings. Log unexpected storage failures without returning task contents.
- Keep browser concerns in `src/`, filesystem/process concerns in `server/`, and transport-safe definitions in `shared/`.
- Keep the Fastify app factory separate from `server/index.ts`. The executable binds only to `127.0.0.1:8765`.
- Use structured Fastify logging and a small injectable seam for VS Code launching. Never construct a shell command from user input.

## Markdown and filesystem invariants

- Recursively discover `.md` files and exclude `README.md` case-insensitively.
- Parse the first H1 title, exactly one supported `- status:`, exactly one positive-integer `- order:`, and an optional `- goal:`.
- Preserve status order: `Backlog`, `Started`, `Planning`, `Ready`, `ToDo`, `InProgress`, `Completed`.
- Sort by status, order, then case-insensitive relative path. Report malformed files, unreadable files, and duplicate orders as warnings without failing the board.
- Accept workspace-relative and absolute task directories, canonicalize them, and prevent traversal and outside-root symlink reads, writes, or editor opens.
- A move may edit only `status` and `order`. Preserve BOM, newline style, permissions, untouched properties, body bytes, and every unaffected file.
- Preserve sparse positive integer ordering. Use `100` for an empty column; use `max(1, floor(firstOrder / 2))` at the beginning when distinct; use `previousOrder + 100` when appending; and use `previousOrder + floor((followingOrder - previousOrder) / 2)` when the gap exceeds one. Only when no distinct positive integer exists, normalize the target column to `100`, `200`, `300`, and so on.
- Keep board operations serialized. Revalidate revisions and source hashes, stage and flush all outputs before replacement, retry only bounded transient Windows `EPERM`/`EBUSY` failures, roll back partial replacements, and clean temporary files.

## Watching and client orchestration

- Maintain one reference-counted Chokidar watcher per canonical directory. Do not follow symlinks; filter to task Markdown and exclude `README.md`.
- Preserve write stabilization, burst debouncing, the SSE event name and retry directive, heartbeat comments, prompt disconnect cleanup, final-subscriber watcher release, and watcher-error logging.
- Keep loads and moves generation/request-aware. Abort supported requests and ignore stale initialization, refresh, watcher, or mutation completions after a directory change.
- Coalesce watcher notifications while work is active and perform at most one authoritative follow-up refresh.
- Serialize moves, keep the board visible, disable drag handles while saving, and expose pending state with `aria-busy` and concise feedback.
- Apply valid moves optimistically. On failure, load the authoritative board; restore the captured snapshot only if refresh also fails and the same directory remains active.
- Persist only the last successfully selected directory in browser storage.

## Drag and drop and UI

- Keep a visible, focusable drag-handle button separate from the task-title button that opens VS Code.
- Preserve mouse, pen, touch, and keyboard movement, screen-reader instructions and announcements, live insertion feedback, column highlighting, horizontal scrolling, empty-column messaging, and edge auto-scroll.
- Derive targets from typed dnd-kit data, not `document.elementFromPoint`.
- Centralize destination calculation in pure typed helpers covering task and column targets, clamping, empty columns, appends, cross-column moves, and same-position no-ops.
- Restore dnd-kit's optimistic DOM movement on every terminal path—success, cancellation, invalid target, Escape, and unmount—before controlled React state is applied.
- Use strict TypeScript, React function components, and hooks. Keep pure board logic outside components where practical.
- Use Tailwind utilities in JSX and shared/global rules in `src/index.css`; avoid inline style props unless a runtime-only value requires them.
- This package is ESM. Retain explicit `.js` extensions in server/shared imports compiled with `NodeNext`.

## Verification

Run commands from `dev/kanban_viewer`. Node.js must satisfy `^20.19.0 || >=22.12.0`.

```powershell
npm ci
npm run dev
npm test
npm run test:backend
npm run test:e2e
npm run build
npm run lint
npm run check
```

- Add focused `src/**/*.test.{ts,tsx}` coverage for client behavior, `tests/**/*.test.ts` coverage for server/filesystem behavior, and `e2e/*.spec.ts` coverage for critical real browser interactions.
- Prefer Fastify `inject` and temporary directories. Tests must not mutate repository task files.
- Cover applicable failure paths: invalid paths, stale revisions, concurrent moves, partial writes and rollback, malformed Markdown, duplicate ordering, watcher cleanup, stale async responses, and drag cancellation.
- Keep coverage at or above 90% statements, lines, and functions and 85% branches.
- Use the narrowest relevant check while iterating. Before handing off a substantive change, run `npm run check`; report any skipped stage and its exact environment limitation.
- Update dependencies through npm so `package.json` and `package-lock.json` remain synchronized. Do not hand-edit the lockfile.

Do not commit generated contents from `node_modules/`, `dist/`, `coverage/`, `test-results/`, `playwright-report/`, `.artifacts/`, or TypeScript build-info files.
