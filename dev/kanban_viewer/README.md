# Kanban task viewer

A small local board for the repository's Markdown kanban tasks. It reads each task's H1 title, `status`, and `order`, lets you drag cards between the seven workflow states, and opens task files in VS Code.

## Setup

From `dev/kanban_viewer`:

```powershell
npm ci
```

Node.js `^20.19.0` or `>=22.12.0`, npm, and the VS Code `code` command must be available on `PATH`.

## Run

```powershell
npm run dev
```

This starts the Fastify service through `tsx watch` at `http://127.0.0.1:8765` and Vite at `http://127.0.0.1:5173`. Open the Vite URL in a browser. The default task folder is `<workspace>/docs/kanban`; enter an absolute path or a workspace-relative path to use another folder.

The selected folder is stored only in the browser. The Node service binds to loopback, validates that task paths stay inside that folder, and edits only the `status` and `order` property lines. Shared TypeBox schemas under `shared/` keep the React client and Fastify API on the same TypeScript contract.

## Verify

```powershell
npm run check
```

The complete check runs ESLint, Vitest with coverage, the production build, and Playwright. Focused commands are `npm test`, `npm run test:backend`, `npm run test:e2e`, and `npm run build`.
