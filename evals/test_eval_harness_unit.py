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

from agent_service_eval import ROOT, load_eval_config, load_scenarios
from metrics import HARNESS_CONFIG_METRICS, TOOL_SEQUENCE_POLICY_METRICS


SKILL_NAME = "unitycodeagent"


def test_eval_config_loads_committed_scenarios():
    config = load_eval_config(SKILL_NAME)
    scenarios = load_scenarios(SKILL_NAME)

    expected = {
        "provider_model": config.provider.model,
        "provider_base_url": config.provider.base_url,
        "scenario_ids": sorted(scenario.id for scenario in scenarios),
        "tool_count": len(config.tool_definitions),
    }
    test_case = LLMTestCase(
        input="Load eval harness config and committed scenarios.",
        actual_output=json.dumps(expected),
        expected_output=json.dumps(expected),
    )

    assert_test(test_case, HARNESS_CONFIG_METRICS)


def test_root_dotenv_loads_provider_key_without_shell_export():
    if not (ROOT / ".env").exists():
        pytest.skip("Root .env is local-only; create it to run the provider key smoke test.")
    previous = os.environ.pop("OPENROUTER_API_KEY", None)
    try:
        config = load_eval_config(SKILL_NAME)
        assert config.env_files_loaded
        assert bool(config.provider.api_key) is True
    finally:
        if previous is not None:
            os.environ["OPENROUTER_API_KEY"] = previous


def test_tool_sequence_policy_metric_accepts_configured_recovery_sequence():
    scenario = load_scenarios(SKILL_NAME)[0]
    policy = scenario.policy
    tool_name = policy["tool_name"]
    first_contains = policy["first_call_contains"][0]
    follow_up_contains = policy["follow_up_contains"]

    test_case = LLMTestCase(
        input=scenario.prompt,
        actual_output=json.dumps(
            {
                "success_observed": True,
                "tool_calls": [
                    {
                        "tool_name": tool_name,
                        "arguments": {"script": f"using UnityEngine.UI;\nvar component = go.AddComponent<{first_contains}>();"},
                        "result_is_error": True,
                        "result_text": "mocked missing assembly error",
                    },
                    {
                        "tool_name": tool_name,
                        "arguments": {"script": " ".join(follow_up_contains)},
                        "result_is_error": False,
                        "result_text": "mocked recovery success",
                    },
                ],
            }
        ),
        expected_output=scenario.expected_output(),
    )

    assert_test(test_case, TOOL_SEQUENCE_POLICY_METRICS)
