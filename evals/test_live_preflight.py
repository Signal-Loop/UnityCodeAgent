# /// script
# requires-python = ">=3.12"
# dependencies = [
#   "deepeval>=4.0.0",
#   "httpx>=0.27.0",
#   "pytest>=8.0.0",
# ]
# ///

from __future__ import annotations

import json
import os

import pytest
from deepeval import assert_test
from deepeval.test_case import LLMTestCase

from agent_service_eval import EvalLogger, load_eval_config, run_live_preflight
from metrics import HARNESS_CONFIG_METRICS


SKILL_NAME = "unitycodeagent"
LIVE_EVAL_ENABLED = os.getenv("UNITYCODEAGENT_EVAL_LIVE") == "1"


@pytest.mark.skipif(
    not LIVE_EVAL_ENABLED,
    reason="Set UNITYCODEAGENT_EVAL_LIVE=1 and run the UnityCodeAgent service before running live agent evals.",
)
def test_live_eval_preflight():
    logger = EvalLogger(SKILL_NAME)
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
