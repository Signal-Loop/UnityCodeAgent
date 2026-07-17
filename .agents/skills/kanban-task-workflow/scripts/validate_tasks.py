# /// script
# requires-python = ">=3.12"
# ///

"""Validate property-based kanban task files.

Run with: uv run .agents/skills/kanban-task-workflow/scripts/validate_tasks.py
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path


STATUSES = ("Backlog", "Started", "Planning", "Ready", "ToDo", "InProgress", "Completed")
GOAL_REQUIRED_STATUSES = frozenset(("Ready", "ToDo", "InProgress", "Completed"))
PROPERTY_PATTERN = re.compile(r"^\s*-\s*([a-z][a-z_-]*)\s*:\s*(.*?)\s*$", re.MULTILINE)
TITLE_PATTERN = re.compile(r"^#\s+(\S.*?)\s*$", re.MULTILINE)


def parse_properties(content: str) -> dict[str, str]:
    """Return simple bullet-list properties from a task file."""
    return {name: value for name, value in PROPERTY_PATTERN.findall(content)}


def validate_task(path: Path, root: Path) -> list[str]:
    """Return actionable validation errors for one task file."""
    relative_path = path.relative_to(root)
    content = path.read_text(encoding="utf-8")
    properties = parse_properties(content)
    errors: list[str] = []

    if not TITLE_PATTERN.search(content):
        errors.append(f"{relative_path}: missing required H1 task title; add `# ...` before the task properties.")

    status = properties.get("status", "")
    if not status:
        errors.append(f"{relative_path}: missing required `status` property; use one of: {', '.join(STATUSES)}.")
    elif status not in STATUSES:
        errors.append(f"{relative_path}: invalid `status: {status}`; use one of: {', '.join(STATUSES)}.")

    order = properties.get("order", "")
    if not order:
        errors.append(f"{relative_path}: missing required positive-integer `order`; use spaced values such as 100, 200.")
    elif not order.isdecimal() or int(order) <= 0:
        errors.append(f"{relative_path}: invalid `order: {order}`; use a positive integer such as 100.")

    if status in GOAL_REQUIRED_STATUSES and not properties.get("goal", ""):
        errors.append(f"{relative_path}: `goal` is required when status is `{status}`; add a concise auditable outcome.")

    return errors


def task_files(root: Path) -> list[Path]:
    """Find markdown task files while ignoring board documentation."""
    return sorted(path for path in root.rglob("*.md") if path.name.lower() != "readme.md")


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate property-based kanban task files.")
    parser.add_argument("task_root", nargs="?", type=Path, default=Path("docs/kanban"))
    args = parser.parse_args()
    root = args.task_root.resolve()

    if not root.is_dir():
        print(f"Kanban task root does not exist or is not a directory: {root}")
        return 2

    files = task_files(root)
    if not files:
        print(f"No task files found under: {root}")
        return 0

    errors = [error for path in files for error in validate_task(path, root)]
    if errors:
        print("Kanban task validation failed:")
        print(*errors, sep="\n")
        return 1

    print(f"Kanban task validation passed: {len(files)} task file(s) checked under {root}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
