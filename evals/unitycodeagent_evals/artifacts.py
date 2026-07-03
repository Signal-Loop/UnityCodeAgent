from __future__ import annotations

import json
import time
import uuid
from datetime import datetime
from typing import Any

from .paths import ARTIFACT_ROOT


class EvalLogger:
    def __init__(self, skill_name: str, run_id: str | None = None, enabled: bool = True):
        self.skill_name = skill_name
        self.run_id = run_id or time.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:8]
        self.enabled = enabled
        self.artifact_dir = ARTIFACT_ROOT / skill_name / self.run_id
        self.events_path = self.artifact_dir / "events.jsonl"
        self.summary_path = self.artifact_dir / "summary.json"
        self.started_at = time.monotonic()
        self.last_milestone = "created"
        self.summary: dict[str, Any] = {
            "skill": skill_name,
            "run_id": self.run_id,
            "scenarios": {},
        }
        if enabled:
            self.artifact_dir.mkdir(parents=True, exist_ok=True)

    def log(self, event: str, **data: Any) -> None:
        self.last_milestone = event
        timestamp = datetime.now().astimezone().isoformat(timespec="milliseconds")
        payload = {
            "event": event,
            "timestamp": timestamp,
            "elapsed_seconds": round(time.monotonic() - self.started_at, 3),
            **data,
        }
        if self.enabled:
            if event == "test_end":
                print("", flush=True)
            print(
                f"{timestamp} [eval:{self.skill_name}] +{payload['elapsed_seconds']:.3f}s {event} "
                f"{json.dumps(data, sort_keys=True)}",
                flush=True,
            )
            with self.events_path.open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(payload, ensure_ascii=False) + "\n")

    def record_scenario(self, scenario_id: str, data: dict[str, Any]) -> None:
        self.summary["scenarios"][scenario_id] = data
        self.write_summary()

    def write_summary(self) -> None:
        self.summary["last_milestone"] = self.last_milestone
        self.summary["elapsed_seconds"] = round(time.monotonic() - self.started_at, 3)
        if self.enabled:
            self.summary_path.write_text(json.dumps(self.summary, indent=2), encoding="utf-8")


