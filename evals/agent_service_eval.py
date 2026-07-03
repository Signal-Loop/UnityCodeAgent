from __future__ import annotations

import json
import os
import queue
import re
import shutil
import subprocess
import threading
import time
import tomllib
import uuid
from datetime import datetime
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

import httpx


ROOT = Path(__file__).resolve().parents[1]
SKILLS_ROOT = ROOT / "Packages" / "com.signal-loop.unitycodeagent" / "Editor" / "Skills~"
DEFAULT_ENDPOINT_MANIFEST = ROOT / ".unityCodeAgent" / "service" / "runtime" / "endpoint.json"
ARTIFACT_ROOT = ROOT / "evals" / ".artifacts"
SERVICE_PROJECT_PATH = (
    ROOT
    / "Packages"
    / "com.signal-loop.unitycodeagent"
    / "Editor"
    / "CopilotService~"
    / "UnityCodeCopilot.Service.csproj"
)

_managed_service_url: str | None = None
_managed_working_directory: Path | None = None


def set_managed_service_context(service_url: str | None, working_directory: Path | None) -> None:
    global _managed_service_url, _managed_working_directory
    _managed_service_url = service_url.rstrip("/") if service_url else None
    _managed_working_directory = working_directory.resolve() if working_directory else None


def load_toml(path: Path) -> dict[str, Any]:
    with path.open("rb") as stream:
        return tomllib.load(stream)


def get_value(data: dict[str, Any], *names: str, default: Any = None) -> Any:
    for name in names:
        if name in data:
            return data[name]
        pascal = name[:1].upper() + name[1:]
        if pascal in data:
            return data[pascal]
    return default


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


@dataclass(frozen=True)
class SessionEventWaitResult:
    session_id: str
    events: tuple[dict[str, Any], ...]
    matched_event_type: str | None
    elapsed_seconds: float

    @property
    def event_types(self) -> tuple[str | None, ...]:
        return tuple(get_value(event, "Type", "type") for event in self.events)

    @property
    def assistant_messages(self) -> tuple[dict[str, Any], ...]:
        return tuple(event for event in self.events if get_value(event, "Type", "type") == "AssistantMessage")

    @property
    def assistant_output_events(self) -> tuple[dict[str, Any], ...]:
        return tuple(event for event in self.events if AgentServiceClient.is_assistant_output_event(event))


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


def load_eval_config(skill_name: str, logger: EvalLogger | None = None) -> EvalConfig:
    env_files = load_dotenv_files(skill_name, logger)
    config_path = SKILLS_ROOT / skill_name / "evals" / "config.toml"
    data = load_toml(config_path)
    provider_data = data["provider"]
    service_data = data.get("service", {})
    session_data = data.get("session", {})
    telemetry = parse_telemetry_config(data.get("telemetry", {}))

    config = EvalConfig(
        skill_name=skill_name,
        service_url=resolve_service_url(),
        provider=ProviderConfig(
            model=provider_data["model"],
            type=provider_data.get("type"),
            base_url=provider_data.get("base_url"),
            api_key_env=provider_data.get("api_key_env"),
            wire_api=provider_data.get("wire_api"),
        ),
        telemetry=telemetry,
        working_directory=resolve_working_directory(session_data),
        skill_directories=tuple(session_data.get("skill_directories", [".agents/skills"])),
        disabled_skills=tuple(session_data.get("disabled_skills", [])),
        request_timeout_seconds=float(service_data.get("request_timeout_seconds", 30)),
        scenario_timeout_seconds=float(service_data.get("scenario_timeout_seconds", 180)),
        idle_timeout_seconds=float(service_data.get("idle_timeout_seconds", 45)),
        preflight_timeout_seconds=float(service_data.get("preflight_timeout_seconds", 10)),
        tool_definitions=tuple(data.get("tools", {}).get("definitions", [])),
        env_files_loaded=tuple(str(path.relative_to(ROOT)) for path in env_files),
    )
    if logger:
        logger.log(
            "config_loaded",
            service_url=config.service_url,
            provider_model=config.provider.model,
            api_key_env=config.provider.api_key_env,
            api_key_present=bool(config.provider.api_key),
            telemetry_enabled=config.telemetry.enabled,
            telemetry_otlp_endpoint=config.telemetry.otlp_endpoint,
        )
    return config


def parse_telemetry_config(data: dict[str, Any] | None) -> TelemetryConfig:
    data = data or {}
    return TelemetryConfig(
        enabled=bool(data.get("enabled", True)),
        capture_content=bool(data.get("capture_content", True)),
        otlp_endpoint=data.get("otlp_endpoint"),
    )


def load_scenarios(skill_name: str) -> list[Scenario]:
    path = SKILLS_ROOT / skill_name / "evals" / "scenarios.toml"
    data = load_toml(path)
    scenarios: list[Scenario] = []
    for item in data.get("scenario", []):
        mock_rules = tuple(
            MockRule(
                tool_name=rule.get("tool_name", item.get("tool_name", "")),
                argument_name=rule.get("argument_name"),
                contains=tuple(rule.get("contains", [])),
                result_is_error=bool(rule.get("result_is_error", False)),
                result_text=rule.get("result_text", ""),
                marks_success=bool(rule.get("marks_success", False)),
                once=bool(rule.get("once", False)),
            )
            for rule in item.get("mock_rule", [])
        )
        policy = dict(item.get("policy", {}))
        if "tool_name" not in policy:
            policy["tool_name"] = item.get("tool_name")
        scenarios.append(
            Scenario(
                id=item["id"],
                prompt=item["prompt"],
                tool_name=item.get("tool_name", ""),
                mock_rules=mock_rules,
                policy=policy,
                fallback_result_is_error=bool(item.get("fallback_result_is_error", True)),
                fallback_result_text=item.get(
                    "fallback_result_text",
                    "The eval harness rejected this tool call because no configured mock rule matched it.",
                ),
            )
        )
    return scenarios


def resolve_service_url() -> str:
    if _managed_service_url:
        return _managed_service_url

    manifest_path = DEFAULT_ENDPOINT_MANIFEST
    if not manifest_path.exists():
        raise FileNotFoundError(
            f"Endpoint manifest was not found at {manifest_path}. "
            "Managed eval runs require the service to publish this file."
        )

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    port = get_value(manifest, "Port", "port")
    if not port:
        raise ValueError(f"Endpoint manifest at {manifest_path} did not contain a port.")

    return f"http://127.0.0.1:{port}"


def resolve_working_directory(session_data: dict[str, Any]) -> Path:
    if _managed_working_directory:
        return _managed_working_directory
    return (ROOT / session_data.get("working_directory", ".")).resolve()


def discover_configured_skill_names() -> list[str]:
    names = [
        path.parent.parent.name
        for path in SKILLS_ROOT.glob("*/evals/scenarios.toml")
        if (path.parent / "config.toml").exists()
    ]
    return sorted(names)


def parse_scenario_filter_tokens(values: str | list[str] | tuple[str, ...] | None) -> tuple[str, ...]:
    if values is None:
        return ()
    raw_values = (values,) if isinstance(values, str) else tuple(values)
    tokens: list[str] = []
    seen: set[str] = set()
    for value in raw_values:
        for token in re.split(r"[\s,]+", value.strip()):
            if not token or token in seen:
                continue
            seen.add(token)
            tokens.append(token)
    return tuple(tokens)


def discover_scenario_cases(skill_names: list[str] | tuple[str, ...] | None = None) -> list[ScenarioCase]:
    names = list(skill_names) if skill_names is not None else discover_configured_skill_names()
    return [ScenarioCase(skill_name, scenario) for skill_name in names for scenario in load_scenarios(skill_name)]


def filter_scenario_cases(cases: list[ScenarioCase], filter_values: str | list[str] | tuple[str, ...] | None) -> list[ScenarioCase]:
    tokens = parse_scenario_filter_tokens(filter_values)
    if not tokens:
        return cases

    cases_by_skill: dict[str, list[ScenarioCase]] = {}
    cases_by_key: dict[str, ScenarioCase] = {}
    for case in cases:
        cases_by_skill.setdefault(case.skill_name, []).append(case)
        cases_by_key[case.key] = case

    selected: list[ScenarioCase] = []
    selected_keys: set[str] = set()
    unknown_skills: list[str] = []
    unknown_scenarios: list[str] = []

    def add_case(case: ScenarioCase) -> None:
        if case.key in selected_keys:
            return
        selected_keys.add(case.key)
        selected.append(case)

    for token in tokens:
        if "." not in token:
            skill_cases = cases_by_skill.get(token)
            if not skill_cases:
                unknown_skills.append(token)
                continue
            for case in skill_cases:
                add_case(case)
            continue

        skill_name, scenario_id = token.split(".", 1)
        if skill_name not in cases_by_skill:
            unknown_skills.append(skill_name)
            continue
        case = cases_by_key.get(f"{skill_name}.{scenario_id}")
        if case is None:
            unknown_scenarios.append(token)
            continue
        add_case(case)

    if unknown_skills or unknown_scenarios:
        available_skills = ", ".join(sorted(cases_by_skill)) or "(none)"
        available_scenarios = ", ".join(sorted(cases_by_key)) or "(none)"
        errors: list[str] = []
        if unknown_skills:
            errors.append(f"unknown skills: {', '.join(sorted(set(unknown_skills)))}")
        if unknown_scenarios:
            errors.append(f"unknown scenarios: {', '.join(sorted(set(unknown_scenarios)))}")
        raise ValueError(
            "Invalid eval scenario filter; "
            + "; ".join(errors)
            + f". Available skills: {available_skills}. Available scenarios: {available_scenarios}."
        )

    return selected


def select_scenario_cases(filter_values: str | list[str] | tuple[str, ...] | None = None) -> list[ScenarioCase]:
    return filter_scenario_cases(discover_scenario_cases(), filter_values)

#TODO: should be moved to separate module
class AgentServiceClient:
    def __init__(
        self,
        config: EvalConfig,
        logger: EvalLogger | None = None,
        timeout_seconds: float | None = None,
        http: httpx.Client | None = None,
    ):
        self.config = config
        self.logger = logger
        timeout = timeout_seconds or config.request_timeout_seconds
        read_timeout = timeout_seconds or config.scenario_timeout_seconds
        self.http = http or httpx.Client(base_url=config.service_url, timeout=httpx.Timeout(timeout, read=read_timeout))
        self._owns_http = http is None

    def close(self) -> None:
        if self._owns_http:
            self.http.close()

    def create_session(self, session_id: str) -> None:
        self._log("http_create_session_start", session_id=session_id)
        response = self.http.post(
            "/api/sessions/create",
            json={
                "SessionId": session_id,
                "Provider": self.config.provider.to_contract(),
                "Streaming": True,
                "InfiniteSessions": {
                    "Enabled": False,
                    "BackgroundCompactionThreshold": 0.25,
                    "BufferExhaustionThreshold": 0.75,
                },
                "WorkingDirectory": str(self.config.working_directory),
                "SkillDirectories": list(self.config.skill_directories),
                "DisabledSkills": list(self.config.disabled_skills),
                "Tools": list(self.config.tool_definitions),
            },
        )
        self._log("http_create_session_end", session_id=session_id, status_code=response.status_code)
        response.raise_for_status()

    def send_prompt(self, session_id: str, prompt: str) -> None:
        self._log("http_send_prompt_start", session_id=session_id, prompt=prompt)
        response = self.http.post("/api/sessions/send", json={"SessionId": session_id, "Prompt": prompt})
        #TODO: response should be logged
        self._log("http_send_prompt_end", session_id=session_id, status_code=response.status_code)
        response.raise_for_status()

    def send_prompt_and_wait_for_idle(
        self,
        session_id: str,
        prompt: str,
        timeout_seconds: float | None = None,
        require_assistant_message: bool = True,
        allow_timeout_after_assistant_response: bool = False,
    ) -> SessionEventWaitResult:
        return self.send_prompt_and_wait_for_events(
            session_id,
            prompt,
            stop_event_types=("SessionIdle",),
            require_assistant_output=require_assistant_message,
            timeout_seconds=timeout_seconds,
            allow_timeout_after_required_events=allow_timeout_after_assistant_response,
        )

    def send_prompt_and_wait_for_events(
        self,
        session_id: str,
        prompt: str,
        stop_event_types: tuple[str, ...],
        required_event_types: tuple[str, ...] = (),
        require_assistant_output: bool = False,
        timeout_seconds: float | None = None,
        allow_timeout_after_required_events: bool = False,
    ) -> SessionEventWaitResult:
        timeout = timeout_seconds or self.config.scenario_timeout_seconds
        started = time.monotonic()
        deadline = started + timeout
        events: list[dict[str, Any]] = []
        observed_required: set[str] = set()
        assistant_output_seen = False
        errors: queue.Queue[BaseException] = queue.Queue()
        matched_event_types: queue.Queue[str] = queue.Queue(maxsize=1)
        connected = threading.Event()
        completed = threading.Event()

        def required_events_observed() -> bool:
            return set(required_event_types).issubset(observed_required) and (
                not require_assistant_output or assistant_output_seen
            )

        def read_events() -> None:
            nonlocal assistant_output_seen
            try:
                self._log("sse_connect_start", session_id=session_id)
                with self.http.stream("GET", "/events", headers={"Accept": "text/event-stream"}) as response:
                    self._log("sse_connect_end", session_id=session_id, status_code=response.status_code)
                    response.raise_for_status()
                    connected.set()
                    for line in response.iter_lines():
                        if completed.is_set():
                            break
                        if not line or line.startswith(":") or not line.startswith("data:"):
                            continue
                        event = json.loads(line.removeprefix("data:").strip())
                        if get_value(event, "SessionId", "sessionId") != session_id:
                            continue
                        events.append(event)
                        event_type = get_value(event, "Type", "type")
                        source_event_type = self.get_source_event_type(event)
                        content = get_value(event, "Content", "content", default="") or ""
                        self._log(
                            "sse_session_event",
                            session_id=session_id,
                            event_type=event_type,
                            source_event_type=source_event_type,
                            content_length=len(content) if isinstance(content, str) else None,
                        )
                        if event_type in required_event_types:
                            observed_required.add(event_type)
                        if self.is_assistant_output_event(event):
                            assistant_output_seen = True
                        if event_type in stop_event_types and required_events_observed():
                            matched_event_types.put(str(event_type))
                            completed.set()
                            break
            except BaseException as error:
                if not completed.is_set():
                    errors.put(error)
                    connected.set()

        thread = threading.Thread(target=read_events, name=f"sse-wait-{session_id}", daemon=True)
        thread.start()

        if not connected.wait(timeout=timeout):
            completed.set()
            raise TimeoutError(f"SSE stream did not connect within {timeout} seconds.")
        self._raise_sse_error(errors)

        self.send_prompt(session_id, prompt)

        while time.monotonic() < deadline:
            self._raise_sse_error(errors)
            if completed.wait(timeout=0.25):
                break

        completed.set()
        thread.join(timeout=5)
        self._raise_sse_error(errors)

        try:
            matched_event_type = matched_event_types.get_nowait()
        except queue.Empty:
            matched_event_type = None

        if matched_event_type is None:
            if allow_timeout_after_required_events and required_events_observed():
                return SessionEventWaitResult(
                    session_id=session_id,
                    events=tuple(events),
                    matched_event_type=None,
                    elapsed_seconds=round(time.monotonic() - started, 3),
                )
            event_types = [event_type for event_type in (get_value(event, "Type", "type") for event in events) if event_type]
            raise TimeoutError(
                f"Timed out after {timeout} seconds waiting for {', '.join(stop_event_types)} "
                f"for session {session_id}. Observed events: {event_types}."
            )

        return SessionEventWaitResult(
            session_id=session_id,
            events=tuple(events),
            matched_event_type=matched_event_type,
            elapsed_seconds=round(time.monotonic() - started, 3),
        )

    def abort(self, session_id: str) -> None:
        self._log("http_abort_start", session_id=session_id)
        response = self.http.post("/api/sessions/abort", json={"SessionId": session_id})
        self._log("http_abort_end", session_id=session_id, status_code=response.status_code)
        if response.status_code not in (202, 404):
            response.raise_for_status()

    def stop_service(self) -> None:
        self._log("http_service_stop_start")
        response = self.http.post("/api/service/stop")
        self._log("http_service_stop_end", status_code=response.status_code)
        response.raise_for_status()

    def post_tool_result(self, call: ToolCall, is_error: bool, text: str) -> None:
        self._log("http_tool_result_start", session_id=call.session_id, call_id=call.call_id, tool_name=call.tool_name)
        response = self.http.post(
            "/api/tools/results",
            json={
                "CallId": call.call_id,
                "SessionId": call.session_id,
                "ToolName": call.tool_name,
                "IsError": is_error,
                "TextResult": text,
                "BinaryResults": None,
                "Error": text if is_error else None,
            },
        )
        self._log(
            "http_tool_result_end",
            session_id=call.session_id,
            call_id=call.call_id,
            tool_name=call.tool_name,
            status_code=response.status_code,
        )
        response.raise_for_status()

    def _log(self, event: str, **data: Any) -> None:
        if self.logger:
            self.logger.log(event, **data)

    @staticmethod
    def _raise_sse_error(errors: queue.Queue[BaseException]) -> None:
        try:
            error = errors.get_nowait()
        except queue.Empty:
            return
        raise error

    @staticmethod
    def get_source_event_type(event: dict[str, Any]) -> str | None:
        source_json = get_value(event, "SourceJson", "sourceJson")
        if not isinstance(source_json, str) or not source_json:
            return None
        try:
            source = json.loads(source_json)
        except json.JSONDecodeError:
            return None
        source_type = get_value(source, "Type", "type")
        return source_type if isinstance(source_type, str) else None

    @staticmethod
    def is_assistant_output_event(event: dict[str, Any]) -> bool:
        event_type = get_value(event, "Type", "type")
        content = get_value(event, "Content", "content", default="") or ""
        has_content = isinstance(content, str) and bool(content.strip())
        if event_type in ("AssistantMessage", "AssistantDelta"):
            return has_content

        source_event_type = AgentServiceClient.get_source_event_type(event)
        return (
            source_event_type
            in ("assistant.message", "assistant.message_delta", "assistant.reasoning", "assistant.reasoning_delta")
            and has_content
        )


class MockToolRouter:
    def __init__(self, scenario: Scenario, logger: EvalLogger | None = None):
        self.scenario = scenario
        self.logger = logger
        self.success_observed = False
        self._used_once_rules: set[int] = set()

    def complete(self, call: ToolCall) -> tuple[bool, str]:
        for index, rule in enumerate(self.scenario.mock_rules):
            if rule.once and index in self._used_once_rules:
                continue
            if self._matches(rule, call):
                if rule.once:
                    self._used_once_rules.add(index)
                if rule.marks_success:
                    self.success_observed = True
                self._log(
                    "mock_rule_matched",
                    scenario_id=self.scenario.id,
                    tool_name=call.tool_name,
                    rule_index=index,
                    marks_success=rule.marks_success,
                    result_is_error=rule.result_is_error,
                )
                return rule.result_is_error, rule.result_text

        self._log("mock_rule_missed", scenario_id=self.scenario.id, tool_name=call.tool_name)
        return self.scenario.fallback_result_is_error, self.scenario.fallback_result_text

    def _matches(self, rule: MockRule, call: ToolCall) -> bool:
        if call.tool_name != rule.tool_name:
            return False
        if not rule.contains:
            return True
        source = call.arguments.get(rule.argument_name, "") if rule.argument_name else call.arguments_json
        if not isinstance(source, str):
            return False
        return all(value in source for value in rule.contains)

    def _log(self, event: str, **data: Any) -> None:
        if self.logger:
            self.logger.log(event, **data)


class SseTraceCapture:
    def __init__(
        self,
        client: AgentServiceClient,
        session_id: str,
        on_tool_call: Callable[[ToolCall], ToolCall],
        logger: EvalLogger | None = None,
    ):
        self.client = client
        self.session_id = session_id
        self.on_tool_call = on_tool_call
        self.logger = logger
        self.events: list[dict[str, Any]] = []
        self.tool_calls: list[ToolCall] = []
        self.errors: queue.Queue[BaseException] = queue.Queue()
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, name=f"sse-{session_id}", daemon=True)

    def start(self) -> None:
        self._log("sse_thread_start", session_id=self.session_id)
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        self._thread.join(timeout=5)
        self._log("sse_thread_stop", session_id=self.session_id, alive=self._thread.is_alive())

    def raise_if_failed(self) -> None:
        try:
            error = self.errors.get_nowait()
        except queue.Empty:
            return
        raise error

    def _run(self) -> None:
        try:
            self._log("sse_connect_start", session_id=self.session_id)
            with self.client.http.stream("GET", "/events", headers={"Accept": "text/event-stream"}) as response:
                self._log("sse_connect_end", session_id=self.session_id, status_code=response.status_code)
                response.raise_for_status()
                for line in response.iter_lines():
                    if self._stop.is_set():
                        break
                    if not line or line.startswith(":") or not line.startswith("data:"):
                        continue
                    event = json.loads(line.removeprefix("data:").strip())
                    if get_value(event, "SessionId", "sessionId") != self.session_id:
                        continue
                    self.events.append(event)
                    event_type = get_value(event, "Type", "type")
                    source_event_type = AgentServiceClient.get_source_event_type(event)
                    content = get_value(event, "Content", "content", default="") or ""
                    self._log(
                        "sse_session_event",
                        session_id=self.session_id,
                        event_type=event_type,
                        source_event_type=source_event_type,
                        content_length=len(content) if isinstance(content, str) else None,
                    )
                    if event_type == "ToolInvocationRequest":
                        call = self._parse_tool_call(event)
                        self._log(
                            "tool_invocation_request",
                            session_id=self.session_id,
                            call_id=call.call_id,
                            tool_name=call.tool_name,
                        )
                        completed = self.on_tool_call(call)
                        self.tool_calls.append(completed)
        except BaseException as error:
            if not self._stop.is_set():
                self._log("sse_error", session_id=self.session_id, error=repr(error))
                self.errors.put(error)

    def _parse_tool_call(self, event: dict[str, Any]) -> ToolCall:
        source = json.loads(get_value(event, "SourceJson", "sourceJson", default="{}"))
        arguments_json = get_value(source, "ArgumentsJson", "argumentsJson", default="{}") or "{}"
        try:
            arguments = json.loads(arguments_json)
        except json.JSONDecodeError:
            arguments = {}
        return ToolCall(
            call_id=get_value(source, "CallId", "callId"),
            session_id=get_value(source, "SessionId", "sessionId"),
            tool_name=get_value(source, "ToolName", "toolName"),
            arguments_json=arguments_json,
            arguments=arguments,
        )

    def _log(self, event: str, **data: Any) -> None:
        if self.logger:
            self.logger.log(event, **data)


def run_scenario(config: EvalConfig, scenario: Scenario, logger: EvalLogger | None = None) -> ScenarioRun:
    session_id = f"eval-{scenario.id}-{uuid.uuid4().hex[:8]}"
    client = AgentServiceClient(config, logger)
    router = MockToolRouter(scenario, logger)

    def on_tool_call(call: ToolCall) -> ToolCall:
        is_error, text = router.complete(call)
        completed = ToolCall(
            call_id=call.call_id,
            session_id=call.session_id,
            tool_name=call.tool_name,
            arguments_json=call.arguments_json,
            arguments=call.arguments,
            result_is_error=is_error,
            result_text=text,
        )
        client.post_tool_result(completed, is_error, text)
        return completed

    capture = SseTraceCapture(client, session_id, on_tool_call, logger)
    reason = "Timed out before the configured success mock rule was observed."
    try:
        if logger:
            logger.log("scenario_start", scenario_id=scenario.id, session_id=session_id)
        client.create_session(session_id)
        capture.start()
        client.send_prompt(session_id, scenario.prompt)
        deadline = time.monotonic() + config.scenario_timeout_seconds
        last_progress_at = time.monotonic()
        last_event_count = 0
        last_tool_count = 0
        while time.monotonic() < deadline:
            capture.raise_if_failed()
            if len(capture.events) != last_event_count or len(capture.tool_calls) != last_tool_count:
                last_progress_at = time.monotonic()
                last_event_count = len(capture.events)
                last_tool_count = len(capture.tool_calls)
            if router.success_observed:
                reason = "The configured success mock rule was observed."
                run = ScenarioRun(scenario, session_id, capture.events, capture.tool_calls, True, reason, {})
                run.diagnostics = build_diagnostics(config, logger, capture, scenario, reason)
                if logger:
                    logger.log("scenario_success", scenario_id=scenario.id, session_id=session_id)
                    logger.record_scenario(scenario.id, run.diagnostics)
                return run
            if any(get_value(event, "Type", "type") == "SessionIdle" for event in capture.events):
                reason = "Session became idle before the configured success mock rule was observed."
                run = ScenarioRun(scenario, session_id, capture.events, capture.tool_calls, False, reason, {})
                run.diagnostics = build_diagnostics(config, logger, capture, scenario, reason)
                if logger:
                    logger.log("scenario_session_idle", scenario_id=scenario.id, session_id=session_id, diagnostics=run.diagnostics)
                    logger.record_scenario(scenario.id, run.diagnostics)
                return run
            if time.monotonic() - last_progress_at > config.idle_timeout_seconds:
                reason = f"No matching SSE or tool-call progress for {config.idle_timeout_seconds} seconds."
                run = ScenarioRun(scenario, session_id, capture.events, capture.tool_calls, False, reason, {})
                run.diagnostics = build_diagnostics(config, logger, capture, scenario, reason)
                if logger:
                    logger.log("scenario_idle_timeout", scenario_id=scenario.id, session_id=session_id, diagnostics=run.diagnostics)
                    logger.record_scenario(scenario.id, run.diagnostics)
                return run
            time.sleep(0.25)
        run = ScenarioRun(scenario, session_id, capture.events, capture.tool_calls, False, reason, {})
        run.diagnostics = build_diagnostics(config, logger, capture, scenario, reason)
        if logger:
            logger.log("scenario_timeout", scenario_id=scenario.id, session_id=session_id, diagnostics=run.diagnostics)
            logger.record_scenario(scenario.id, run.diagnostics)
        return run
    finally:
        try:
            client.abort(session_id)
        finally:
            capture.stop()
            client.close()


def build_diagnostics(
    config: EvalConfig,
    logger: EvalLogger | None,
    capture: SseTraceCapture,
    scenario: Scenario,
    reason: str,
) -> dict[str, Any]:
    return {
        "reason": reason,
        "last_milestone": logger.last_milestone if logger else None,
        "service_url": config.service_url,
        "scenario_id": scenario.id,
        "session_event_count": len(capture.events),
        "session_event_types_tail": [get_value(event, "Type", "type") for event in capture.events[-20:]],
        "session_events_tail": [
            {
                "event_type": get_value(event, "Type", "type"),
                "source_event_type": AgentServiceClient.get_source_event_type(event),
                "content_length": len(content) if isinstance(content, str) else None,
            }
            for event in capture.events[-20:]
            for content in [get_value(event, "Content", "content", default="") or ""]
        ],
        "tool_calls": [
            {
                "tool_name": call.tool_name,
                "arguments": call.arguments,
                "result_is_error": call.result_is_error,
                "result_text": call.result_text,
            }
            for call in capture.tool_calls
        ],
    }


class ManagedAgentService:
    def __init__(self, config: EvalConfig, logger: EvalLogger):
        self.config = config
        self.logger = logger
        self.project_root = logger.artifact_dir / "project-root"
        self.stdout_path = logger.artifact_dir / "service.stdout.log"
        self.stderr_path = logger.artifact_dir / "service.stderr.log"
        self.endpoint_manifest_path = self.project_root / ".unityCodeAgent" / "service" / "runtime" / "endpoint.json"
        self.process: subprocess.Popen[str] | None = None
        self.service_url: str | None = None
        self._stdout = None
        self._stderr = None

    def start(self) -> str:
        self._prepare_project_root()
        command = self.build_command(self.project_root, self.config.telemetry)
        self.logger.log(
            "managed_service_start",
            command=command,
            command_line=subprocess.list2cmdline(command),
            project_root=str(self.project_root),
            telemetry_enabled=self.config.telemetry.enabled,
            telemetry_capture_content=self.config.telemetry.capture_content,
            telemetry_otlp_endpoint=self.config.telemetry.otlp_endpoint,
        )
        self._stdout = self.stdout_path.open("w", encoding="utf-8")
        self._stderr = self.stderr_path.open("w", encoding="utf-8")
        self.process = subprocess.Popen(
            command,
            cwd=str(SERVICE_PROJECT_PATH.parent),
            stdout=self._stdout,
            stderr=self._stderr,
            text=True,
        )
        self.service_url = self._wait_for_service()
        set_managed_service_context(self.service_url, self.project_root)
        self.logger.log("managed_service_ready", service_url=self.service_url, process_id=self.process.pid)
        return self.service_url

    def stop(self) -> None:
        try:
            self.logger.log(
                "managed_service_stop_start",
                service_url=self.service_url,
                process_id=self.process.pid if self.process else None,
            )
            if self.service_url:
                try:
                    config = EvalConfig(
                        skill_name=self.config.skill_name,
                        service_url=self.service_url,
                        provider=self.config.provider,
                        telemetry=self.config.telemetry,
                        working_directory=self.project_root,
                        skill_directories=self.config.skill_directories,
                        disabled_skills=self.config.disabled_skills,
                        request_timeout_seconds=10,
                        scenario_timeout_seconds=10,
                        idle_timeout_seconds=10,
                        preflight_timeout_seconds=10,
                        tool_definitions=self.config.tool_definitions,
                        env_files_loaded=self.config.env_files_loaded,
                    )
                    client = AgentServiceClient(config, self.logger, timeout_seconds=10)
                    try:
                        client.stop_service()
                    finally:
                        client.close()
                except Exception as error:
                    self.logger.log("managed_service_stop_request_failed", error=repr(error))

            if self.process:
                try:
                    self.process.wait(timeout=20)
                    self.logger.log("managed_service_exited", exit_code=self.process.returncode, process_id=self.process.pid)
                except subprocess.TimeoutExpired:
                    self.logger.log("managed_service_terminate", process_id=self.process.pid)
                    self.process.terminate()
                    try:
                        self.process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        self.logger.log("managed_service_kill", process_id=self.process.pid)
                        self.process.kill()
                        self.process.wait(timeout=5)
                self.logger.log(
                    "managed_service_stop_complete",
                    process_id=self.process.pid,
                    exit_code=self.process.returncode,
                )
        finally:
            set_managed_service_context(None, None)
            if self._stdout:
                self._stdout.close()
            if self._stderr:
                self._stderr.close()

    @staticmethod
    def build_command(project_root: Path, telemetry: TelemetryConfig) -> list[str]:
        artifacts_path = project_root.parent / "service-build"
        command = [
            "dotnet",
            "run",
            "--project",
            str(SERVICE_PROJECT_PATH),
            "--no-launch-profile",
            "--artifacts-path",
            str(artifacts_path),
            "-p:UseAppHost=false",
            "--",
            f"--ProjectRoot={project_root}",
            "--UnityProcessId=0",
            "--NoUnity=true",
            f"--EnableTelemetry={str(telemetry.enabled).lower()}",
            f"--TelemetryCaptureContent={str(telemetry.capture_content).lower()}",
            "--urls",
            "http://127.0.0.1:0",
        ]
        if telemetry.otlp_endpoint:
            command.append(f"--OtlpEndpoint={telemetry.otlp_endpoint}")
        return command

    def _prepare_project_root(self) -> None:
        if self.project_root.exists():
            shutil.rmtree(self.project_root)
        skills_target = self.project_root / ".agents" / "skills"
        skills_target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(
            SKILLS_ROOT,
            skills_target,
            ignore=shutil.ignore_patterns("__pycache__", "*.pyc", ".env"),
        )

    def _wait_for_service(self) -> str:
        assert self.process is not None
        deadline = time.monotonic() + 90
        last_error: str | None = None
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise RuntimeError(
                    f"Managed service exited before it became healthy. exit_code={self.process.returncode}. "
                    f"stdout={self._tail(self.stdout_path)} stderr={self._tail(self.stderr_path)}"
                )

            manifest = self._read_endpoint_manifest()
            port = get_value(manifest, "Port", "port") if manifest else None
            if port:
                service_url = f"http://127.0.0.1:{port}"
                try:
                    with httpx.Client(base_url=service_url, timeout=2) as client:
                        response = client.get("/health")
                    if response.status_code < 500:
                        return service_url
                    last_error = f"health returned HTTP {response.status_code}"
                except Exception as error:
                    last_error = repr(error)
            time.sleep(0.25)

        raise TimeoutError(
            "Managed service did not publish a healthy endpoint within 90 seconds. "
            f"last_error={last_error} stdout={self._tail(self.stdout_path)} stderr={self._tail(self.stderr_path)}"
        )

    def _read_endpoint_manifest(self) -> dict[str, Any] | None:
        if not self.endpoint_manifest_path.exists():
            return None
        try:
            return json.loads(self.endpoint_manifest_path.read_text(encoding="utf-8"))
        except (json.JSONDecodeError, OSError):
            return None

    @staticmethod
    def _tail(path: Path, limit: int = 4000) -> str:
        if not path.exists():
            return ""
        text = path.read_text(encoding="utf-8", errors="replace")
        return text[-limit:].strip()

    @staticmethod
    def _redact_command(command: list[str]) -> list[str]:
        return ["--OtlpEndpoint=[REDACTED]" if item.startswith("--OtlpEndpoint=") else item for item in command]
