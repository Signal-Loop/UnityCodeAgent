from __future__ import annotations

import time
import uuid
from typing import Any, Protocol

from .artifacts import EvalLogger
from .client import AgentServiceClient as DefaultAgentServiceClient
from .mock_tools import MockToolRouter as DefaultMockToolRouter
from .models import EvalConfig, Scenario, ScenarioRun, ToolCall
from .sse import SseTraceCapture as DefaultSseTraceCapture
from .utils import get_value


class ScenarioCapture(Protocol):
    events: list[dict[str, Any]]
    tool_calls: list[ToolCall]


def run_scenario(config: EvalConfig, scenario: Scenario, logger: EvalLogger | None = None) -> ScenarioRun:
    session_id = f"eval-{scenario.id}-{uuid.uuid4().hex[:8]}"
    client = DefaultAgentServiceClient(config, logger)
    router = DefaultMockToolRouter(scenario, logger)

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

    capture = DefaultSseTraceCapture(client, session_id, on_tool_call, logger)
    reason = "Timed out before the scenario reached a terminal service state."
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
            if len(capture.tool_calls) >= scenario.max_tool_calls:
                reason = f"Stopped after reaching the scenario max tool call limit of {scenario.max_tool_calls}."
                run = ScenarioRun(scenario, session_id, capture.events, capture.tool_calls, True, reason, {})
                run.diagnostics = build_diagnostics(config, logger, capture, scenario, reason)
                if logger:
                    logger.log("scenario_max_tool_calls", scenario_id=scenario.id, session_id=session_id, diagnostics=run.diagnostics)
                    logger.record_scenario(scenario.id, run.diagnostics)
                return run
            if any(get_value(event, "Type", "type") == "SessionIdle" for event in capture.events):
                reason = "Session became idle."
                run = ScenarioRun(scenario, session_id, capture.events, capture.tool_calls, False, reason, {})
                run.diagnostics = build_diagnostics(config, logger, capture, scenario, reason)
                if logger:
                    logger.log("scenario_session_idle", scenario_id=scenario.id, session_id=session_id, diagnostics=run.diagnostics)
                    logger.record_scenario(scenario.id, run.diagnostics)
                return run
            if time.monotonic() - last_progress_at > config.idle_timeout_seconds:
                reason = f"No matching SSE or tool-call progress for {config.idle_timeout_seconds} seconds."
                run = ScenarioRun(scenario, session_id, capture.events, capture.tool_calls, True, reason, {})
                run.diagnostics = build_diagnostics(config, logger, capture, scenario, reason)
                if logger:
                    logger.log("scenario_idle_timeout", scenario_id=scenario.id, session_id=session_id, diagnostics=run.diagnostics)
                    logger.record_scenario(scenario.id, run.diagnostics)
                return run
            time.sleep(0.25)
        run = ScenarioRun(scenario, session_id, capture.events, capture.tool_calls, True, reason, {})
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
    capture: ScenarioCapture,
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
                "source_event_type": DefaultAgentServiceClient.get_source_event_type(event),
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
