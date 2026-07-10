from __future__ import annotations

import os
from pathlib import Path

from .artifacts import EvalLogger
from .paths import ROOT, SKILLS_ROOT


def load_dotenv_files(skill_name: str | None = None, logger: EvalLogger | None = None) -> list[Path]:
    paths = [ROOT / ".env"]
    if skill_name:
        paths.append(SKILLS_ROOT / skill_name / "evals" / ".env")

    loaded: list[Path] = []
    for path in paths:
        if not path.exists():
            continue
        loaded.append(path)
        for line in path.read_text(encoding="utf-8").splitlines():
            key, value = parse_dotenv_line(line)
            if key:
                os.environ[key] = value
        if logger:
            logger.log("dotenv_loaded", path=str(path.resolve()))
    return loaded


def parse_dotenv_line(line: str) -> tuple[str | None, str]:
    stripped = line.strip()
    if not stripped or stripped.startswith("#") or "=" not in stripped:
        return None, ""
    key, value = stripped.split("=", 1)
    key = key.strip()
    value = value.strip()
    if not key or key.startswith("#"):
        return None, ""
    if len(value) >= 2 and value[0] == value[-1] and value[0] in ("'", '"'):
        value = value[1:-1]
    return key, value
