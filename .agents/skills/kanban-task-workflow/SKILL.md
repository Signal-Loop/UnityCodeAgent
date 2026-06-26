---
name: kanban-task-workflow
description: Use this skill whenever the user asks to work from docs/kanban.md, proceed with kanban tasks, pick the next task, move tasks through Planning/To Do/In Progress/Done, or run an automated task workflow based on task status properties. This skill reads the markdown kanban board, researches and plans tasks before implementation, respects approval and blocked properties, updates task notes, modifies code only for the selected task, and verifies the result.
---

# Kanban Task Workflow

Use this skill to turn `docs/kanban.md` into the active work queue for the repository. The board is the coordination surface: read it before acting, select one task at a time, update only the selected task's task card unless the user explicitly asks for broader board edits, and keep code changes aligned with the task's current status.

## Board Model

The default board path is `docs/kanban.md`. Treat level-two headings as workflow columns and level-three headings as task cards.

Known columns:

- `Backlog`: ideas only. Do not modify or implement these unless the user explicitly names one or asks to pull from backlog.
- `Planning`: tasks that need codebase research and an implementation plan before they can be implemented.
- `To Do`: tasks that have enough plan detail to implement.
- `In Progress`: the active implementation task.
- `Done`: completed tasks. Do not reopen unless the user asks or current verification proves the task is incomplete.

If the board uses extra columns, preserve them and infer their meaning from local text before acting.

## Task Format

Each task is a level-three heading followed by an property list and, when useful, a longer notes.

Use only properties that help the workflow. Do not invent due dates, labels, or estimates when the board does not need them.

Preferred shape:

```markdown
### Task title

- status: todo
- goal: Implement the scoped change, verified by focused tests, while preserving existing public behavior outside the task boundary.
- approval: required
- blocked: false
- steps:
    - [ ] Research current behavior
    - [ ] Implement scoped change
    - [ ] Run focused tests

Longer task description, research notes, plan, verification notes, or blocker details.

```

Supported fields:

- `status`: `backlog`, `planning`, `todo`, `in-progress`, `blocked`, or `done`.
- `goal`: compact completion contract for the task. Write it as an auditable outcome with verification and constraints, following the Codex Goals pattern: desired end state, evidence that proves it, important boundaries, and what to report if blocked.
- `approval`: `required`, `approved`, or `not-required`.
- `blocked`: `true` or `false`.
- `reason`: short blocker or decision note.
- `updated`: ISO date, when useful for longer tasks.
- `steps`: checklist of concrete work items.

When a section heading and `status` property disagree, treat the property as a warning, not an automatic override. Resolve the conflict by reading the task context and, if needed, update the selected task so heading and property match after you decide the correct state.

Do not add every possible property. Add or update only the fields that help selection, approval, implementation, verification, or future continuation.

Use `goal` to keep the task objective visible across turns. A good goal is narrow enough to verify but broad enough to let the agent choose the next useful action. Avoid vague goals like "improve this"; prefer statements that name what should be true, how to check it, what must not regress, and what to report if the evidence cannot be gathered.

## Task Selection

When the user says to proceed, continue, work the board, pick the next task, or similar:

1. Read `AGENTS.md` or the user-provided repository instructions when available.
2. Read `docs/kanban.md`.
3. Select exactly one task:
   - First choose the first unblocked task in `In Progress`.
   - Otherwise choose the first unblocked task in `To Do` whose `approval` property is `approved` or `not-required`.
   - Otherwise choose the first task in `Planning`.
   - Do not choose from `Backlog` without explicit user direction.
4. If no actionable task exists, report the board state and the smallest next decision needed from the user.

Keep task order stable unless moving the selected task between columns is part of the workflow.

## Workflow By Status

### Planning

Research before planning. Inspect the relevant code, tests, contracts, docs, and recent board notes. Use `rg`/`rg --files` first for local search. Browse the internet only when the task depends on current external facts or the user asks for external research. Plan should be robust, reliable, cover edge cases, and be strictly scoped to the task. Tests should be focused, fast, and verify the task's goal without assuming unrelated behavior.

Then update the selected task with a concise implementation plan:

```markdown
- status: todo
- goal: Produce a researched implementation plan, verified against the relevant code and tests, with blockers and approval needs called out explicitly.
- approval: required
- blocked: false
- steps:
    - [ ] Implement step...
    - [ ] Verify behavior...

Research:
- Finding...

Plan:
- Step...

Verification:
- Test or check...
```

Move the task to `To Do` only after after it has been approved by user. Approval can come from the user's current message, `approval: approved`, `approval: not-required`, or a board note that clearly says the plan was accepted.

### To Do

Before editing code, confirm the task has enough plan detail and is approved.

If approval is missing, update the task with `approval: required` and ask for approval. If approved, move the task to `In Progress`, update the `status` property, and implement the scoped plan.

### In Progress

Implement the task end to end. Keep edits narrow and aligned with repository conventions. Update contracts, DTOs, tests, docs, or examples only when the task requires those surfaces to stay consistent.
Implementation should follow KISS, YAGNI and SOLID principles, , avoid unnecessary refactors, introduce abstractions only if they provide clear value, and preserve existing behavior outside the task scope. If a blocker arises, update the task with `blocked: true` and a concrete reason, then ask for the decision or external input needed to continue.

After implementation:

1. Run the most focused verification available.
2. If verification passes, move the task to `Done`, update steps statuses and set `status: done`. Move full task text and add a short completion note with the checks run. 
3. If verification fails or a blocker remains, keep the task in `In Progress` or mark it `blocked: true` with a concrete reason and next action.

### Blocked

Do not keep coding through a real blocker. Record the blocker in the selected task with `blocked: true` and `reason: ...`, include what was tried, and ask only for the decision or external input needed to continue.

## Editing The Board

When modifying `docs/kanban.md`:

- Preserve unrelated task text, ordering, spelling, and formatting.
- Move only the selected task card between columns.
- Keep `Backlog` read-only unless explicitly requested.
- Add short notes that help the next agent continue: research findings, plan, verification, blockers, and completion summary.
- Avoid rewriting the entire file for cosmetic cleanup.

## Verification Defaults

Choose verification by the surface touched:

- Unity/editor changes: prefer Unity EditMode tests, Unity console logs, and targeted Unity editor C# checks.
- Local service changes: prefer focused `dotnet test` runs for `CopilotService.Tests`.
- Contract changes: keep OpenAPI/AsyncAPI examples and shared DTO behavior aligned.
- UI E2E changes: use Unity tests that wait for UI Toolkit layout and async updates instead of assuming same-frame completion.

If verification cannot be run, state why and record the residual risk in the task note.

## Response Format

When you finish a kanban workflow turn, report:

- Selected task and starting status.
- What changed in code and board state.
- Verification run and result.
- Current task status and next action.

Keep the final response concise. The board should carry detailed continuity notes; the chat response should summarize the outcome.
