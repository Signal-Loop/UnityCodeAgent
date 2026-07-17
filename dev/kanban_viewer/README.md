# Kanban task viewer

A small local board for the repository's Markdown kanban tasks. It reads each task's H1 title, `status`, and `order`, lets you drag cards between the seven workflow states, and opens task files in VS Code.

## Setup

From `dev/kanban_viewer`:

```powershell
uv python install 3.11
uv sync
npm install
```

uv, Node.js, npm, and the VS Code `code` command must be available on `PATH`. The checked-in `.python-version` pins the backend to Python 3.11; uv creates and maintains its environment from `pyproject.toml` and `uv.lock` automatically.

## Run

```powershell
npm run dev
```

This starts FastAPI through uv at `http://127.0.0.1:8765` and Vite at `http://127.0.0.1:5173`. On the first run, uv installs the pinned Python and backend dependencies when needed. Open the Vite URL in a browser. The default task folder is `<workspace>/docs/kanban`; enter an absolute path or a workspace-relative path to use another folder.

The selected folder is stored only in the browser. The backend binds to loopback, validates that task paths stay inside that folder, and edits only the `status` and `order` property lines.

## Verify

```powershell
npm run check
```

Individual commands are `npm run lint`, `npm test`, `npm run test:backend`, and `npm run build`.
