# /// script
# requires-python = ">=3.12"
# dependencies = [
#   "deepeval>=4.0.0",
#   "httpx>=0.27.0",
#   "pytest>=8.0.0",
# ]
# ///

from __future__ import annotations

import os
import re

import pytest
from deepeval import assert_test
from deepeval.test_case import LLMTestCase

from agent_service_eval import SKILLS_ROOT, EvalLogger, load_eval_config, load_scenarios, run_scenario
from metrics import TOOL_SEQUENCE_POLICY_METRICS


LIVE_EVAL_ENABLED = os.getenv("UNITYCODEAGENT_EVAL_LIVE") == "1"


def configured_skill_names() -> list[str]:
    names = [
        path.parent.parent.name
        for path in SKILLS_ROOT.glob("*/evals/scenarios.toml")
        if (path.parent / "config.toml").exists()
    ]
    return sorted(names)


def skill_marker_name(skill_name: str) -> str:
    marker = re.sub(r"\W+", "_", skill_name).strip("_")
    return marker or "skill"


def scenario_cases() -> list[pytest.ParameterSet]:
    cases: list[pytest.ParameterSet] = []
    for skill_name in configured_skill_names():
        for scenario in load_scenarios(skill_name):
            cases.append(
                pytest.param(
                    skill_name,
                    scenario,
                    id=f"{skill_name}:{scenario.id}",
                    marks=getattr(pytest.mark, skill_marker_name(skill_name)),
                )
            )
    return cases


@pytest.mark.skipif(
    not LIVE_EVAL_ENABLED,
    reason="Set UNITYCODEAGENT_EVAL_LIVE=1 and run the UnityCodeAgent service before running live agent evals.",
)
@pytest.mark.parametrize("skill_name,scenario", scenario_cases())
def test_configured_skill_scenario(skill_name, scenario):
    logger = EvalLogger(skill_name, enabled=LIVE_EVAL_ENABLED)
    config = load_eval_config(skill_name, logger if LIVE_EVAL_ENABLED else None)
    run = run_scenario(config, scenario, logger)
    test_case = LLMTestCase(
        input=scenario.prompt,
        actual_output=run.to_test_case_output(),
        expected_output=scenario.expected_output(),
    )

    assert_test(test_case, TOOL_SEQUENCE_POLICY_METRICS)
