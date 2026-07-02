from __future__ import annotations

import json
import os
import queue
import threading
import time
import tomllib
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

import httpx


ROOT = Path(__file__).resolve().parents[1]
SKILLS_ROOT = ROOT / "Packages" / "com.signal-loop.unitycodeagent" / "Editor" / "Skills~"
DEFAULT_ENDPOINT_MANIFEST = ROOT / ".unityCodeAgent" / "service" / "runtime" / "endpoint.json"
ARTIFACT_ROOT = ROOT / "evals" / ".artifacts"


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
            logger.log("dotenv_loaded", path=str(path.relative_to(ROOT)))
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
        payload = {
            "event": event,
            "elapsed_seconds": round(time.monotonic() - self.started_at, 3),
            **data,
        }
        if self.enabled:
            print(f"[eval:{self.skill_name}] {event} {json.dumps(data, sort_keys=True)}", flush=True)
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
class EvalConfig:
    skill_name: str
    service_url: str
    provider: ProviderConfig
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


def load_eval_config(skill_name: str, logger: EvalLogger | None = None) -> EvalConfig:
    env_files = load_dotenv_files(skill_name, logger)
    config_path = SKILLS_ROOT / skill_name / "evals" / "config.toml"
    data = load_toml(config_path)
    provider_data = data["provider"]
    service_data = data.get("service", {})
    session_data = data.get("session", {})

    config = EvalConfig(
        skill_name=skill_name,
        service_url=resolve_service_url(service_data),
        provider=ProviderConfig(
            model=provider_data["model"],
            type=provider_data.get("type"),
            base_url=provider_data.get("base_url"),
            api_key_env=provider_data.get("api_key_env"),
            wire_api=provider_data.get("wire_api"),
        ),
        working_directory=(ROOT / session_data.get("working_directory", ".")).resolve(),
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
        )
    return config


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


def resolve_service_url(service_data: dict[str, Any]) -> str:
    override = os.getenv("UNITYCODEAGENT_EVAL_SERVICE_URL") or service_data.get("url")
    if override:
        return override.rstrip("/")

    manifest_path = Path(service_data.get("endpoint_manifest", DEFAULT_ENDPOINT_MANIFEST))
    if not manifest_path.is_absolute():
        manifest_path = ROOT / manifest_path
    if manifest_path.exists():
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        port = get_value(manifest, "Port", "port")
        if port:
            return f"http://127.0.0.1:{port}"

    port = os.getenv("UNITYCODEAGENT_EVAL_SERVICE_PORT") or service_data.get("port") or 7777
    return f"http://127.0.0.1:{port}"


class AgentServiceClient:
    def __init__(self, config: EvalConfig, logger: EvalLogger | None = None, timeout_seconds: float | None = None):
        self.config = config
        self.logger = logger
        timeout = timeout_seconds or config.request_timeout_seconds
        read_timeout = timeout_seconds or config.scenario_timeout_seconds
        self.http = httpx.Client(
            base_url=config.service_url,
            timeout=httpx.Timeout(timeout, read=read_timeout),
        )

    def close(self) -> None:
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
        self._log("http_send_prompt_start", session_id=session_id)
        response = self.http.post("/api/sessions/send", json={"SessionId": session_id, "Prompt": prompt})
        self._log("http_send_prompt_end", session_id=session_id, status_code=response.status_code)
        response.raise_for_status()

    def abort(self, session_id: str) -> None:
        self._log("http_abort_start", session_id=session_id)
        response = self.http.post("/api/sessions/abort", json={"SessionId": session_id})
        self._log("http_abort_end", session_id=session_id, status_code=response.status_code)
        if response.status_code not in (202, 404):
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
                    self._log("sse_session_event", session_id=self.session_id, event_type=event_type)
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


def run_live_preflight(config: EvalConfig, logger: EvalLogger | None = None) -> dict[str, Any]:
    session_id = f"eval-preflight-{uuid.uuid4().hex[:8]}"
    client = AgentServiceClient(config, logger, timeout_seconds=config.preflight_timeout_seconds)
    started = time.monotonic()
    sse_errors: queue.Queue[BaseException] = queue.Queue()
    sse_status: queue.Queue[int] = queue.Queue()

    def probe_sse() -> None:
        try:
            with client.http.stream("GET", "/events", headers={"Accept": "text/event-stream"}) as response:
                sse_status.put(response.status_code)
        except BaseException as error:
            sse_errors.put(error)

    if logger:
        logger.log("preflight_start", session_id=session_id, service_url=config.service_url)
    thread = threading.Thread(target=probe_sse, name=f"sse-preflight-{session_id}", daemon=True)
    thread.start()
    thread.join(timeout=config.preflight_timeout_seconds)
    if thread.is_alive():
        if logger:
            logger.log("preflight_sse_timeout", session_id=session_id, timeout_seconds=config.preflight_timeout_seconds)
        raise TimeoutError(f"SSE preflight did not complete within {config.preflight_timeout_seconds} seconds.")
    try:
        error = sse_errors.get_nowait()
    except queue.Empty:
        error = None
    if error:
        raise error
    status_code = sse_status.get_nowait()
    if status_code >= 400:
        raise httpx.HTTPStatusError(
            f"SSE preflight returned HTTP {status_code}.",
            request=client.http.build_request("GET", "/events"),
            response=httpx.Response(status_code),
        )
    if logger:
        logger.log("preflight_sse_checked", session_id=session_id, status_code=status_code)

    try:
        client.create_session(session_id)
        client.send_prompt(session_id, "Reply with one short sentence for eval preflight.")
        client.abort(session_id)
        result = {
            "success": True,
            "service_url": config.service_url,
            "elapsed_seconds": round(time.monotonic() - started, 3),
            "api_key_present": bool(config.provider.api_key),
        }
        if logger:
            logger.log("preflight_success", **result)
            logger.write_summary()
        return result
    finally:
        client.close()
