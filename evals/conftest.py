from __future__ import annotations

import re

from deepeval.test_run.test_run import PromptData, global_test_run_manager

from agent_service_eval import SKILLS_ROOT


def pytest_configure(config):
    for path in SKILLS_ROOT.glob("*/evals/scenarios.toml"):
        marker = re.sub(r"\W+", "_", path.parent.parent.name).strip("_") or "skill"
        config.addinivalue_line("markers", f"{marker}: configured skill eval scenarios")


def pytest_sessionfinish(session, exitstatus):
    test_run = global_test_run_manager.get_test_run()
    if test_run is None:
        return
    test_run.hyperparameters = {
        "eval_harness": "UnityCodeAgent service skill scenarios",
        "runner": "deepeval test run",
        "selected_marks": session.config.option.markexpr or "all",
    }
    test_run.prompts = [
        PromptData(
            alias="skill-scenario-prompt",
            version="1",
            text_template="Configured skill scenario prompt loaded from <skill>/evals/scenarios.toml.",
        )
    ]
    global_test_run_manager.save_test_run(global_test_run_manager.temp_file_path)
