from __future__ import annotations

import json
import time
import uuid
from typing import Any

import pytest
from deepeval import assert_test
from deepeval.test_case import LLMTestCase

from metrics import HARNESS_CONFIG_METRICS
from unitycodeagent_evals import AgentServiceClient, EvalConfig, EvalLogger, load_eval_config

SKILL_NAME = "unitycodeagent"


@pytest.fixture(autouse=True)
def log_test_progress(request, shared_eval_run_root):
    logger = EvalLogger(
        "live-preflight",
        enabled=request.config.getoption("live"),
        artifact_root=shared_eval_run_root,
    )
    logger.log("test_start", test_name=request.node.name)
    yield
    logger.log("test_end", test_name=request.node.name)


def run_live_preflight(config: EvalConfig, logger: EvalLogger | None = None) -> dict[str, Any]:
    session_id = f"eval-preflight-{uuid.uuid4().hex[:8]}"
    client = AgentServiceClient(config, logger, timeout_seconds=config.preflight_timeout_seconds)
    started = time.monotonic()

    if logger:
        logger.log("preflight_start", session_id=session_id, service_url=config.service_url)

    try:
        client.create_session(session_id)
        wait_result = client.send_prompt_and_wait_for_idle(
            session_id,
            "ping",
            timeout_seconds=config.preflight_timeout_seconds,
            allow_timeout_after_assistant_response=True,
        )
        result = {
            "success": True,
            "service_url": config.service_url,
            "elapsed_seconds": round(time.monotonic() - started, 3),
            "api_key_present": bool(config.provider.api_key),
            "event_count": len(wait_result.events),
            "event_types": wait_result.event_types,
            "matched_event_type": wait_result.matched_event_type,
            "assistant_message_count": len(wait_result.assistant_output_events),
        }
        if logger:
            logger.log("preflight_success", **result)
            logger.write_summary()
        return result
    finally:
        try:
            client.abort(session_id)
        except Exception as error:
            if logger:
                logger.log("preflight_abort_failed", session_id=session_id, error=repr(error))
        client.close()


def test_live_eval_preflight(request, shared_eval_run_root):
    if not request.config.getoption("live"):
        pytest.skip("Pass --live to let the eval suite start a managed no-Unity service.")

    logger = EvalLogger("live-preflight", artifact_root=shared_eval_run_root)
    config = load_eval_config(SKILL_NAME, logger)
    result = run_live_preflight(config, logger)
    expected = {
        "success": True,
        "service_url": config.service_url,
        "api_key_present": True,
    }

    test_case = LLMTestCase(
        input="Verify live eval service preflight before running long scenarios.",
        actual_output=json.dumps(
            {
                "success": result["success"],
                "service_url": result["service_url"],
                "api_key_present": result["api_key_present"],
            }
        ),
        expected_output=json.dumps(expected),
    )

    assert_test(test_case, HARNESS_CONFIG_METRICS)
