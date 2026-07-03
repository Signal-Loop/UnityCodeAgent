from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .utils import get_value


@dataclass(frozen=True)
class ProviderConfig:
    model: str
    type: str | None
    base_url: str | None
    api_key_env: str | None
    wire_api: str | None

    @property
    def api_key(self) -> str | None:
        if not self.api_key_env:
            return None
        return os.getenv(self.api_key_env)

    def to_contract(self) -> dict[str, Any]:
        return {
            "Model": self.model,
            "Type": self.type,
            "BaseUrl": self.base_url,
            "ApiKey": self.api_key,
            "WireApi": self.wire_api,
            "ModelName": self.model,
        }


@dataclass(frozen=True)
class TelemetryConfig:
    enabled: bool = True
    capture_content: bool = True
    otlp_endpoint: str | None = None


@dataclass(frozen=True)
class MockRule:
    tool_name: str
    argument_name: str | None
    contains: tuple[str, ...]
    result_is_error: bool
    result_text: str
    marks_success: bool
    once: bool


@dataclass(frozen=True)
class Scenario:
    id: str
    prompt: str
    tool_name: str
    mock_rules: tuple[MockRule, ...]
    policy: dict[str, Any]
    fallback_result_is_error: bool
    fallback_result_text: str

    def expected_output(self) -> str:
        return json.dumps(self.policy)


@dataclass(frozen=True)
class ScenarioCase:
    skill_name: str
    scenario: Scenario

    @property
    def key(self) -> str:
        return f"{self.skill_name}.{self.scenario.id}"


@dataclass(frozen=True)
class EvalConfig:
    skill_name: str
    service_url: str
    provider: ProviderConfig
    telemetry: TelemetryConfig
    working_directory: Path
    skill_directories: tuple[str, ...]
    disabled_skills: tuple[str, ...]
    request_timeout_seconds: float
    scenario_timeout_seconds: float
    idle_timeout_seconds: float
    preflight_timeout_seconds: float
    tool_definitions: tuple[dict[str, Any], ...]
    env_files_loaded: tuple[str, ...]


@dataclass(frozen=True)
class ToolCall:
    call_id: str
    session_id: str
    tool_name: str
    arguments_json: str
    arguments: dict[str, Any]
    result_is_error: bool | None = None
    result_text: str | None = None

    @property
    def script(self) -> str:
        value = self.arguments.get("script", "")
        return value if isinstance(value, str) else ""


@dataclass
class ScenarioRun:
    scenario: Scenario
    session_id: str
    events: list[dict[str, Any]]
    tool_calls: list[ToolCall]
    success_observed: bool
    reason: str
    diagnostics: dict[str, Any]

    def to_test_case_output(self) -> str:
        return json.dumps(
            {
                "scenario_id": self.scenario.id,
                "session_id": self.session_id,
                "success_observed": self.success_observed,
                "reason": self.reason,
                "diagnostics": self.diagnostics,
                "tool_calls": [
                    {
                        "tool_name": call.tool_name,
                        "arguments": call.arguments,
                        "result_is_error": call.result_is_error,
                        "result_text": call.result_text,
                    }
                    for call in self.tool_calls
                ],
                "event_types": [get_value(event, "Type", "type") for event in self.events],
            },
            indent=2,
        )

