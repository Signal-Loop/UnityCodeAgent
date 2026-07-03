from __future__ import annotations

import json
import queue
import threading
import time
from dataclasses import dataclass
from typing import Any

import httpx

from .artifacts import EvalLogger
from .models import EvalConfig, ToolCall
from .utils import get_value


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

