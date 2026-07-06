from __future__ import annotations

import pytest
from deepeval.test_run.test_run import PromptData, global_test_run_manager

from unitycodeagent_evals import (
    EvalLogger,
    ManagedAgentService,
    create_shared_eval_run_root,
    load_managed_service_startup_config,
    select_scenario_cases,
)


def pytest_addoption(parser):
    parser.addoption(
        "--filter",
        dest="scenario_filter",
        action="append",
        default=[],
        help=(
            "Skill scenario filter. Use skill_name for all skill scenarios or "
            "skill_name.scenario_id for one scenario. May be repeated or comma/whitespace-separated."
        ),
    )
    parser.addoption(
        "--live",
        action="store_true",
        default=False,
        help="Run live evals against a managed no-Unity UnityCodeAgent service.",
    )

def pytest_sessionfinish(session, exitstatus):
    test_run = global_test_run_manager.get_test_run()
    if test_run is None:
        return
    test_run.hyperparameters = {
        "eval_harness": "UnityCodeAgent service skill scenarios",
        "runner": "deepeval test run",
        "scenario_filter": session.config.getoption("scenario_filter") or "all",
        "live": session.config.getoption("live"),
        "managed_no_unity_service": session.config.getoption("live"),
    }
    test_run.prompts = [
        PromptData(
            alias="skill-scenario-prompt",
            version="1",
            text_template="Configured skill scenario prompt loaded from <skill>/evals/scenarios.toml.",
        )
    ]
    global_test_run_manager.save_test_run(global_test_run_manager.temp_file_path)


@pytest.fixture(scope="session")
def shared_eval_run_root(request):
    if not request.config.getoption("live"):
        return None
    return create_shared_eval_run_root()


@pytest.fixture(scope="session", autouse=True)
def managed_no_unity_service(request, shared_eval_run_root):
    if not request.config.getoption("live"):
        yield
        return

    scenario_cases = select_scenario_cases(request.config.getoption("scenario_filter"))
    if not scenario_cases:
        yield
        return

    logger = EvalLogger("managed-service", artifact_root=shared_eval_run_root)
    config = load_managed_service_startup_config(scenario_cases[0].skill_name, logger)
    service = ManagedAgentService(config, logger)
    service.start()
    try:
        yield
    finally:
        service.stop()
