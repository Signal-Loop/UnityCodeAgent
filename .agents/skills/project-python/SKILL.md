---
name: project-python
description: "Use when creating, editing, or running Python scripts in the UnityCodeAgent repository. Enforces this project's Python scripting conventions: scripts live under /Python, run with uv, stay single-file, use inline metadata dependencies, and avoid pyproject.toml or other Python project configuration files."
---

# Project Python

## Rules

- Put repository Python scripts in `Python/`.
- Keep each script self-contained in one `.py` file.
- Run scripts with `uv run`, for example: `uv run Python/example.py`.
- Declare third-party dependencies in PEP 723 inline script metadata at the top of the script.
- Do not add Python project configuration files such as `pyproject.toml`, `requirements.txt`, `setup.py`, `setup.cfg`, or lock files unless the user explicitly asks.
- Prefer standard library code when practical; add inline dependencies only when they materially simplify the task.

Those rules can be ignored if the user explicitly asks for a different approach.

## Script Template

```python
# /// script
# requires-python = ">=3.12"
# dependencies = [
#   "package-name>=1.0",
# ]
# ///

def main() -> int:
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```
