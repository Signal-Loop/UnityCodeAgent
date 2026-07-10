from __future__ import annotations

import json
import queue
import threading
from collections.abc import Callable
from typing import Any

from .artifacts import EvalLogger
from .client import AgentServiceClient
from .models import ToolCall
from .utils import get_value


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


