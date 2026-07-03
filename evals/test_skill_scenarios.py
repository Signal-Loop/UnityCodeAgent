from __future__ import annotations

import pytest
from deepeval import assert_test
from deepeval.test_case import LLMTestCase

from metrics import TOOL_SEQUENCE_POLICY_METRICS
from unitycodeagent_evals import EvalLogger, load_eval_config, run_scenario, select_scenario_cases


def pytest_generate_tests(metafunc):
    if {"skill_name", "scenario"}.issubset(metafunc.fixturenames):
        cases = select_scenario_cases(metafunc.config.getoption("scenario_filter"))
        metafunc.parametrize(
            "skill_name,scenario",
            [(case.skill_name, case.scenario) for case in cases],
            ids=[f"{case.skill_name}:{case.scenario.id}" for case in cases],
        )


@pytest.fixture(autouse=True)
def log_test_progress(request):
    logger = EvalLogger("skill-scenarios", enabled=request.config.getoption("live"))
    logger.log("test_start", test_name=request.node.name)
    yield
    logger.log("test_end", test_name=request.node.name)


def test_configured_skill_scenario(request, skill_name, scenario):
    live_enabled = request.config.getoption("live")
    if not live_enabled:
        pytest.skip("Pass --live to let the eval suite start a managed no-Unity service.")

    logger = EvalLogger(skill_name, enabled=live_enabled)
    config = load_eval_config(skill_name, logger if live_enabled else None)
    run = run_scenario(config, scenario, logger)
    test_case = LLMTestCase(
        input=scenario.prompt,
        actual_output=run.to_test_case_output(),
        expected_output=scenario.expected_output(),
    )

    assert_test(test_case, TOOL_SEQUENCE_POLICY_METRICS)
