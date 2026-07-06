from __future__ import annotations

import json
import os
import threading
from pathlib import Path
from typing import Any

import httpx
import pytest
from deepeval import assert_test
from deepeval.test_case import LLMTestCase

import unitycodeagent_evals.config as config_harness
import unitycodeagent_evals.runner as runner_harness
from metrics import HARNESS_CONFIG_METRICS, TOOL_SEQUENCE_POLICY_METRICS
from unitycodeagent_evals import (
    ROOT,
    AgentServiceClient,
    EvalLogger,
    ManagedAgentService,
    MockRule,
    Scenario,
    ScenarioCase,
    TelemetryConfig,
    ToolCall,
    build_diagnostics,
    create_shared_eval_run_root,
    filter_scenario_cases,
    load_managed_service_startup_config,
    load_scenarios,
    parse_scenario_filter_tokens,
    parse_telemetry_config,
    run_scenario,
    set_managed_service_context,
)

SKILL_NAME = "unitycodeagent"
UNIT_LOGGER = EvalLogger("eval-harness-unit")


@pytest.fixture(autouse=True)
def log_test_progress(request):
    UNIT_LOGGER.log("test_start", test_name=request.node.name)
    yield
    UNIT_LOGGER.log("test_end", test_name=request.node.name)


def make_scenario_case(skill_name: str, scenario_id: str) -> ScenarioCase:
    return ScenarioCase(
        skill_name=skill_name,
        scenario=Scenario(
            id=scenario_id,
            prompt=f"Prompt for {scenario_id}",
            tool_name="execute_csharp_script_in_unity_editor",
            max_tool_calls=10,
            mock_rules=(
                MockRule(
                    tool_name="execute_csharp_script_in_unity_editor",
                    argument_name="script",
                    contains=("success",),
                    result_is_error=False,
                    result_text="ok",
                    once=False,
                ),
            ),
            policy={
                "threshold": 1.0,
                "expected_tool": [
                    {
                        "tool_name": "execute_csharp_script_in_unity_editor",
                        "argument_name": "script",
                        "arguments_contain": ["success"],
                    }
                ],
            },
            fallback_result_is_error=True,
            fallback_result_text="fallback",
        ),
    )


def sample_scenario_cases() -> list[ScenarioCase]:
    return [
        make_scenario_case("unitycodeagent", "image_missing_ui_assembly"),
        make_scenario_case("unitycodeagent", "rigidbody2d_missing_physics2d_assembly"),
        make_scenario_case("other_skill", "scenario.name.with.dots"),
    ]


def assert_case_keys(cases: list[ScenarioCase], expected: list[str]) -> None:
    assert [case.key for case in cases] == expected


def test_scenario_filter_omitted_selects_all_cases():
    assert_case_keys(
        filter_scenario_cases(sample_scenario_cases(), None),
        [
            "unitycodeagent.image_missing_ui_assembly",
            "unitycodeagent.rigidbody2d_missing_physics2d_assembly",
            "other_skill.scenario.name.with.dots",
        ],
    )


def test_scenario_filter_selects_one_skill():
    assert_case_keys(
        filter_scenario_cases(sample_scenario_cases(), "unitycodeagent"),
        [
            "unitycodeagent.image_missing_ui_assembly",
            "unitycodeagent.rigidbody2d_missing_physics2d_assembly",
        ],
    )


def test_scenario_filter_selects_multiple_skills():
    assert_case_keys(
        filter_scenario_cases(sample_scenario_cases(), "other_skill, unitycodeagent"),
        [
            "other_skill.scenario.name.with.dots",
            "unitycodeagent.image_missing_ui_assembly",
            "unitycodeagent.rigidbody2d_missing_physics2d_assembly",
        ],
    )


def test_scenario_filter_selects_one_exact_scenario():
    assert_case_keys(
        filter_scenario_cases(sample_scenario_cases(), "unitycodeagent.image_missing_ui_assembly"),
        ["unitycodeagent.image_missing_ui_assembly"],
    )


def test_scenario_filter_selects_multiple_exact_scenarios():
    assert_case_keys(
        filter_scenario_cases(
            sample_scenario_cases(),
            [
                "unitycodeagent.image_missing_ui_assembly",
                "other_skill.scenario.name.with.dots",
            ],
        ),
        [
            "unitycodeagent.image_missing_ui_assembly",
            "other_skill.scenario.name.with.dots",
        ],
    )


def test_scenario_filter_selects_mixed_tokens_and_deduplicates():
    assert parse_scenario_filter_tokens(["unitycodeagent, other_skill.scenario.name.with.dots", "unitycodeagent"]) == (
        "unitycodeagent",
        "other_skill.scenario.name.with.dots",
    )
    assert_case_keys(
        filter_scenario_cases(
            sample_scenario_cases(),
            ["unitycodeagent, other_skill.scenario.name.with.dots", "unitycodeagent"],
        ),
        [
            "unitycodeagent.image_missing_ui_assembly",
            "unitycodeagent.rigidbody2d_missing_physics2d_assembly",
            "other_skill.scenario.name.with.dots",
        ],
    )


def test_scenario_filter_reports_unknown_skill():
    with pytest.raises(ValueError, match="unknown skills: missing_skill"):
        filter_scenario_cases(sample_scenario_cases(), "missing_skill")


def test_scenario_filter_reports_unknown_scenario():
    with pytest.raises(ValueError, match="unknown scenarios: unitycodeagent.missing_scenario"):
        filter_scenario_cases(sample_scenario_cases(), "unitycodeagent.missing_scenario")


def test_eval_config_loads_committed_scenarios():
    config = load_managed_service_startup_config(SKILL_NAME, UNIT_LOGGER)
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


def test_load_scenarios_defaults_max_tool_calls_to_ten(tmp_path, monkeypatch):
    scenario_dir = tmp_path / "sample" / "evals"
    scenario_dir.mkdir(parents=True)
    (scenario_dir / "scenarios.toml").write_text(
        """
[[scenario]]
id = "default-max"
prompt = "Prompt"
tool_name = "execute_csharp_script_in_unity_editor"

[scenario.policy]
threshold = 1.0
""",
        encoding="utf-8",
    )
    monkeypatch.setattr(config_harness, "SKILLS_ROOT", tmp_path)

    scenarios = load_scenarios("sample")

    assert scenarios[0].max_tool_calls == 10


def test_load_scenarios_rejects_non_positive_max_tool_calls(tmp_path, monkeypatch):
    scenario_dir = tmp_path / "sample" / "evals"
    scenario_dir.mkdir(parents=True)
    (scenario_dir / "scenarios.toml").write_text(
        """
[[scenario]]
id = "invalid-max"
prompt = "Prompt"
tool_name = "execute_csharp_script_in_unity_editor"
max_tool_calls = 0
""",
        encoding="utf-8",
    )
    monkeypatch.setattr(config_harness, "SKILLS_ROOT", tmp_path)

    with pytest.raises(ValueError, match="max_tool_calls must be a positive integer"):
        load_scenarios("sample")


def test_managed_service_startup_config_does_not_require_endpoint_manifest():
    set_managed_service_context(None, None)

    config = load_managed_service_startup_config(SKILL_NAME, UNIT_LOGGER)

    assert config.service_url == "http://127.0.0.1:0"
    assert config.provider.model
    assert config.telemetry.enabled is True


def test_shared_eval_run_root_groups_scenario_and_service_artifacts(tmp_path):
    shared_root = create_shared_eval_run_root(tmp_path)

    scenario_logger = EvalLogger("unitycodeagent", artifact_root=shared_root)
    managed_logger = EvalLogger("managed-service", artifact_root=shared_root)

    scenario_logger.log("scenario_start", scenario_id="scenario-1")
    scenario_logger.record_scenario("scenario-1", {"status": "ok"})
    managed_logger.log("managed_service_start", service_url="http://127.0.0.1:1234")
    managed_logger.write_summary()

    assert scenario_logger.artifact_dir.parent == shared_root / "unitycodeagent"
    assert managed_logger.artifact_dir.parent == shared_root / "managed-service"
    assert scenario_logger.events_path.exists()
    assert scenario_logger.summary_path.exists()
    assert managed_logger.events_path.exists()
    assert managed_logger.summary_path.exists()


def test_root_dotenv_loads_provider_key_without_shell_export():
    if not (ROOT / ".env").exists():
        pytest.skip("Root .env is local-only; create it to run the provider key smoke test.")
    previous = os.environ.pop("OPENROUTER_API_KEY", None)
    try:
        config = load_managed_service_startup_config(SKILL_NAME, UNIT_LOGGER)
        assert config.env_files_loaded
        assert bool(config.provider.api_key) is True
    finally:
        if previous is not None:
            os.environ["OPENROUTER_API_KEY"] = previous


def test_create_session_sends_provider_loaded_from_config_and_dotenv():
    if not (ROOT / ".env").exists():
        pytest.skip("Root .env is local-only; create it to run the provider payload smoke test.")
    previous = os.environ.pop("OPENROUTER_API_KEY", None)
    captured: list[dict] = []

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/api/sessions/create":
            captured.append(json.loads(request.content.decode("utf-8")))
            return httpx.Response(200, json={"SessionId": "session-provider-test", "Status": "ready", "Messages": []})
        return httpx.Response(202)

    try:
        config = load_managed_service_startup_config(SKILL_NAME, UNIT_LOGGER)
        http = httpx.Client(base_url=config.service_url, transport=httpx.MockTransport(handler))
        client = AgentServiceClient(config, UNIT_LOGGER, http=http)
        try:
            UNIT_LOGGER.log("create_session_request", session_id="session-provider-test")
            client.create_session("session-provider-test")
        finally:
            http.close()

        provider = captured[0]["Provider"]
        assert provider["Model"] == config.provider.model
        assert provider["Type"] == config.provider.type
        assert provider["BaseUrl"] == config.provider.base_url
        assert provider["WireApi"] == config.provider.wire_api
        assert provider["ApiKey"] == config.provider.api_key
        assert provider["ApiKey"]
    finally:
        if previous is not None:
            os.environ["OPENROUTER_API_KEY"] = previous


def test_client_send_prompt_waits_for_assistant_message_and_session_idle():
    session_id = "session-wait-test"
    prompt_sent = threading.Event()

    class PromptGatedSseStream(httpx.SyncByteStream):
        def __iter__(self):
            assert prompt_sent.wait(timeout=2)
            for event_type, content in (
                ("AssistantMessage", "Preflight response."),
                ("SessionIdle", "Session became idle."),
            ):
                payload = {
                    "SessionId": session_id,
                    "Type": event_type,
                    "Content": content,
                }
                yield f"data: {json.dumps(payload)}\n\n".encode()

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/events":
            return httpx.Response(200, stream=PromptGatedSseStream())
        if request.url.path == "/api/sessions/send":
            prompt_sent.set()
            return httpx.Response(202)
        return httpx.Response(404)

    config = load_managed_service_startup_config(SKILL_NAME, UNIT_LOGGER)
    http = httpx.Client(base_url=config.service_url, transport=httpx.MockTransport(handler))
    client = AgentServiceClient(config, UNIT_LOGGER, timeout_seconds=2, http=http)
    try:
        result = client.send_prompt_and_wait_for_idle(session_id, "Say hello.", timeout_seconds=2)
    finally:
        http.close()

    assert result.matched_event_type == "SessionIdle"
    assert result.event_types == ("AssistantMessage", "SessionIdle")
    assert len(result.assistant_messages) == 1
    assert len(result.assistant_output_events) == 1


def test_client_send_prompt_can_return_after_assistant_response_when_idle_times_out():
    session_id = "session-wait-timeout-test"
    prompt_sent = threading.Event()

    class AssistantOnlySseStream(httpx.SyncByteStream):
        def __iter__(self):
            assert prompt_sent.wait(timeout=2)
            payload = {
                "SessionId": session_id,
                "Type": "AssistantMessage",
                "Content": "Preflight response.",
            }
            yield f"data: {json.dumps(payload)}\n\n".encode()

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/events":
            return httpx.Response(200, stream=AssistantOnlySseStream())
        if request.url.path == "/api/sessions/send":
            prompt_sent.set()
            return httpx.Response(202)
        return httpx.Response(404)

    config = load_managed_service_startup_config(SKILL_NAME, UNIT_LOGGER)
    http = httpx.Client(base_url=config.service_url, transport=httpx.MockTransport(handler))
    client = AgentServiceClient(config, UNIT_LOGGER, timeout_seconds=1, http=http)
    try:
        result = client.send_prompt_and_wait_for_idle(
            session_id,
            "Say hello.",
            timeout_seconds=0.1,
            allow_timeout_after_assistant_response=True,
        )
    finally:
        http.close()

    assert result.matched_event_type is None
    assert result.event_types == ("AssistantMessage",)
    assert len(result.assistant_output_events) == 1


def test_run_scenario_returns_when_session_idle_is_observed(monkeypatch):
    scenario = make_scenario_case(SKILL_NAME, "idle-before-success").scenario
    config = load_managed_service_startup_config(SKILL_NAME, UNIT_LOGGER)

    class FakeClient:
        def __init__(self, config, logger):
            self.config = config
            self.logger = logger

        get_source_event_type = staticmethod(AgentServiceClient.get_source_event_type)

        def create_session(self, session_id):
            pass

        def send_prompt(self, session_id, prompt):
            pass

        def abort(self, session_id):
            pass

        def close(self):
            pass

    class FakeCapture:
        def __init__(self, client, session_id, on_tool_call, logger):
            self.client = client
            self.session_id = session_id
            self.on_tool_call = on_tool_call
            self.logger = logger
            self.events = []
            self.tool_calls = []

        def start(self):
            self.events.append(
                {
                    "SessionId": self.session_id,
                    "Type": "SessionIdle",
                    "Content": "Session became idle.",
                    "SourceJson": json.dumps({"type": "session.idle"}),
                }
            )

        def stop(self):
            pass

        def raise_if_failed(self):
            pass

    monkeypatch.setattr(runner_harness, "DefaultAgentServiceClient", FakeClient)
    monkeypatch.setattr(runner_harness, "DefaultSseTraceCapture", FakeCapture)

    run = run_scenario(config, scenario, UNIT_LOGGER)

    assert run.reason == "Session became idle."
    assert run.failed is False
    assert run.diagnostics["session_event_types_tail"] == ["SessionIdle"]


def test_run_scenario_stops_when_max_tool_calls_is_reached(monkeypatch):
    scenario = make_scenario_case(SKILL_NAME, "max-tool-calls").scenario
    scenario = Scenario(
        id=scenario.id,
        prompt=scenario.prompt,
        tool_name=scenario.tool_name,
        max_tool_calls=1,
        mock_rules=scenario.mock_rules,
        policy=scenario.policy,
        fallback_result_is_error=scenario.fallback_result_is_error,
        fallback_result_text=scenario.fallback_result_text,
    )
    config = load_managed_service_startup_config(SKILL_NAME, UNIT_LOGGER)

    class FakeClient:
        def __init__(self, config, logger):
            self.config = config
            self.logger = logger

        get_source_event_type = staticmethod(AgentServiceClient.get_source_event_type)

        def create_session(self, session_id):
            pass

        def send_prompt(self, session_id, prompt):
            pass

        def post_tool_result(self, completed, is_error, text):
            pass

        def abort(self, session_id):
            pass

        def close(self):
            pass

    class FakeCapture:
        def __init__(self, client, session_id, on_tool_call, logger):
            self.client = client
            self.session_id = session_id
            self.on_tool_call = on_tool_call
            self.logger = logger
            self.events = []
            self.tool_calls = []

        def start(self):
            call = ToolCall(
                call_id="call-1",
                session_id=self.session_id,
                tool_name="execute_csharp_script_in_unity_editor",
                arguments_json=json.dumps({"script": "success"}),
                arguments={"script": "success"},
            )
            self.tool_calls.append(self.on_tool_call(call))

        def stop(self):
            pass

        def raise_if_failed(self):
            pass

    monkeypatch.setattr(runner_harness, "DefaultAgentServiceClient", FakeClient)
    monkeypatch.setattr(runner_harness, "DefaultSseTraceCapture", FakeCapture)

    run = run_scenario(config, scenario, UNIT_LOGGER)

    assert run.reason == "Stopped after reaching the scenario max tool call limit of 1."
    assert run.failed is True
    assert len(run.diagnostics["tool_calls"]) == 1


def test_run_scenario_does_not_end_when_mock_rule_matches(monkeypatch):
    scenario = make_scenario_case(SKILL_NAME, "mock-rule-does-not-end").scenario
    config = load_managed_service_startup_config(SKILL_NAME, UNIT_LOGGER)

    class FakeClient:
        def __init__(self, config, logger):
            self.config = config
            self.logger = logger

        get_source_event_type = staticmethod(AgentServiceClient.get_source_event_type)

        def create_session(self, session_id):
            pass

        def send_prompt(self, session_id, prompt):
            pass

        def post_tool_result(self, completed, is_error, text):
            pass

        def abort(self, session_id):
            pass

        def close(self):
            pass

    class FakeCapture:
        def __init__(self, client, session_id, on_tool_call, logger):
            self.client = client
            self.session_id = session_id
            self.on_tool_call = on_tool_call
            self.logger = logger
            self.events = []
            self.tool_calls = []

        def start(self):
            call = ToolCall(
                call_id="call-1",
                session_id=self.session_id,
                tool_name="execute_csharp_script_in_unity_editor",
                arguments_json=json.dumps({"script": "success"}),
                arguments={"script": "success"},
            )
            self.tool_calls.append(self.on_tool_call(call))
            self.events.append(
                {
                    "SessionId": self.session_id,
                    "Type": "SessionIdle",
                    "Content": "Session became idle.",
                    "SourceJson": json.dumps({"type": "session.idle"}),
                }
            )

        def stop(self):
            pass

        def raise_if_failed(self):
            pass

    monkeypatch.setattr(runner_harness, "DefaultAgentServiceClient", FakeClient)
    monkeypatch.setattr(runner_harness, "DefaultSseTraceCapture", FakeCapture)

    run = run_scenario(config, scenario, UNIT_LOGGER)

    assert run.reason == "Session became idle."
    assert run.failed is False
    assert len(run.tool_calls) == 1


def test_build_diagnostics_includes_source_event_type_and_content_length():
    config = load_managed_service_startup_config(SKILL_NAME, UNIT_LOGGER)
    scenario = make_scenario_case(SKILL_NAME, "diagnostics").scenario

    class DiagnosticsCapture:
        events: list[dict[str, Any]] = [
            {
                "SessionId": "diagnostics-session",
                "Type": "AssistantMessage",
                "Content": "hello",
                "SourceJson": json.dumps({"type": "assistant.message"}),
            }
        ]
        tool_calls: list[ToolCall] = []

    capture = DiagnosticsCapture()

    diagnostics = build_diagnostics(config, UNIT_LOGGER, capture, scenario, "diagnostic check")

    assert diagnostics["session_events_tail"] == [
        {
            "event_type": "AssistantMessage",
            "source_event_type": "assistant.message",
            "content_length": 5,
        }
    ]


def test_client_assistant_output_requires_content():
    event = {
        "SessionId": "session-output-test",
        "Type": "Unknown",
        "Content": "",
        "SourceJson": json.dumps({"type": "assistant.turn_start"}),
    }

    assert AgentServiceClient.is_assistant_output_event(event) is False


def test_telemetry_defaults_to_service_file_mode_without_file_path_argument():
    telemetry = TelemetryConfig(enabled=True, capture_content=True, otlp_endpoint=None)

    command = ManagedAgentService.build_command(Path("C:/eval-project"), telemetry)
    assert "--NoUnity=true" in command
    assert "--UnityProcessId=0" in command
    assert "--artifacts-path" in command
    assert str(Path("C:/service-build")) in command
    assert "--EnableTelemetry=true" in command
    assert "--TelemetryCaptureContent=true" in command
    assert not any(argument.startswith("--TelemetryFilePath") for argument in command)


def test_telemetry_otlp_endpoint_maps_to_service_argument_without_file_path_argument():
    telemetry = parse_telemetry_config(
        {
            "otlp_endpoint": "http://127.0.0.1:4318",
            "capture_content": False,
        }
    )

    command = ManagedAgentService.build_command(Path("C:/eval-project"), telemetry)

    assert "--OtlpEndpoint=http://127.0.0.1:4318" in command
    assert "--TelemetryCaptureContent=false" in command
    assert not any(argument.startswith("--TelemetryFilePath") for argument in command)


def assert_tool_policy(actual: dict[str, Any], expected: dict[str, Any], expected_success: bool, expected_score: float) -> None:
    metric = TOOL_SEQUENCE_POLICY_METRICS[0]
    test_case = LLMTestCase(
        input="Check tool sequence policy.",
        actual_output=json.dumps(actual),
        expected_output=json.dumps(expected),
    )

    score = metric.measure(test_case)

    assert score == pytest.approx(expected_score)
    assert metric.is_successful() is expected_success


def test_tool_sequence_policy_metric_accepts_configured_recovery_sequence():
    UNIT_LOGGER.log("scenario_metric_test_start", skill_name=SKILL_NAME)
    scenario = load_scenarios(SKILL_NAME)[0]
    policy = scenario.policy
    first_expected, follow_up_expected = policy["expected_tool"]

    test_case = LLMTestCase(
        input=scenario.prompt,
        actual_output=json.dumps(
            {
                "tool_calls": [
                    {
                        "tool_name": first_expected["tool_name"],
                        "arguments": {"script": f"using UnityEngine.UI;\nvar component = go.AddComponent<{first_expected['arguments_contain'][0]}>();"},
                        "result_is_error": True,
                        "result_text": "mocked missing assembly error",
                    },
                    {
                        "tool_name": follow_up_expected["tool_name"],
                        "arguments": {"script": " ".join(follow_up_expected["arguments_contain"])},
                        "result_is_error": False,
                        "result_text": "mocked recovery success",
                    },
                ],
            }
        ),
        expected_output=scenario.expected_output(),
    )

    assert_test(test_case, TOOL_SEQUENCE_POLICY_METRICS)


def test_tool_sequence_policy_metric_allows_optional_partial_match_above_threshold():
    actual = {
        "tool_calls": [
            {
                "tool_name": "execute_csharp_script_in_unity_editor",
                "arguments": {"script": "AddToolAssembly UnityEngine.UI"},
                "result_is_error": False,
            }
        ]
    }
    expected = {
        "threshold": 0.5,
        "expected_tool": [
            {
                "tool_name": "execute_csharp_script_in_unity_editor",
                "arguments_contain": ["AddToolAssembly"],
            },
            {
                "tool_name": "read_unity_console_logs",
            },
        ],
    }

    assert_tool_policy(actual, expected, True, 0.5)


def test_tool_sequence_policy_metric_missing_required_call_fails_regardless_of_threshold():
    actual = {
        "tool_calls": [
            {
                "tool_name": "execute_csharp_script_in_unity_editor",
                "arguments": {"script": "AddComponent<Image>()"},
                "result_is_error": True,
            }
        ]
    }
    expected = {
        "threshold": 0.1,
        "expected_tool": [
            {
                "tool_name": "execute_csharp_script_in_unity_editor",
                "arguments_contain": ["AddToolAssembly"],
                "required": True,
            }
        ],
    }

    assert_tool_policy(actual, expected, False, 0.0)


def test_tool_sequence_policy_metric_failed_run_fails_even_when_calls_match():
    actual = {
        "failed": True,
        "reason": "Stopped after reaching the scenario max tool call limit of 1.",
        "tool_calls": [
            {
                "tool_name": "execute_csharp_script_in_unity_editor",
                "arguments": {"script": "AddToolAssembly UnityEngine.UI"},
                "result_is_error": False,
            }
        ],
    }
    expected = {
        "threshold": 1.0,
        "expected_tool": [
            {
                "tool_name": "execute_csharp_script_in_unity_editor",
                "arguments_contain": ["AddToolAssembly"],
            }
        ],
    }

    assert_tool_policy(actual, expected, False, 0.0)


def test_tool_sequence_policy_metric_ignores_order_when_ordering_is_false():
    actual = {
        "tool_calls": [
            {"tool_name": "second", "arguments": {"script": "B"}, "result_is_error": False},
            {"tool_name": "first", "arguments": {"script": "A"}, "result_is_error": False},
        ]
    }
    expected = {
        "threshold": 1.0,
        "should_consider_ordering": False,
        "expected_tool": [
            {"tool_name": "first", "arguments_contain": ["A"]},
            {"tool_name": "second", "arguments_contain": ["B"]},
        ],
    }

    assert_tool_policy(actual, expected, True, 1.0)


def test_tool_sequence_policy_metric_penalizes_out_of_order_calls_when_ordering_is_true():
    actual = {
        "tool_calls": [
            {"tool_name": "second", "arguments": {"script": "B"}, "result_is_error": False},
            {"tool_name": "first", "arguments": {"script": "A"}, "result_is_error": False},
        ]
    }
    expected = {
        "threshold": 1.0,
        "should_consider_ordering": True,
        "expected_tool": [
            {"tool_name": "first", "arguments_contain": ["A"]},
            {"tool_name": "second", "arguments_contain": ["B"]},
        ],
    }

    assert_tool_policy(actual, expected, False, 0.5)


def test_tool_sequence_policy_metric_exact_match_rejects_extra_missing_or_reordered_calls():
    expected = {
        "threshold": 1.0,
        "should_consider_ordering": True,
        "should_exact_match": True,
        "expected_tool": [
            {"tool_name": "first", "arguments_contain": ["A"]},
            {"tool_name": "second", "arguments_contain": ["B"]},
        ],
    }

    assert_tool_policy(
        {
            "tool_calls": [
                {"tool_name": "first", "arguments": {"script": "A"}},
                {"tool_name": "second", "arguments": {"script": "B"}},
                {"tool_name": "third", "arguments": {"script": "C"}},
            ]
        },
        expected,
        False,
        0.0,
    )
    assert_tool_policy(
        {
            "tool_calls": [
                {"tool_name": "first", "arguments": {"script": "A"}},
            ]
        },
        expected,
        False,
        0.0,
    )
    assert_tool_policy(
        {
            "tool_calls": [
                {"tool_name": "second", "arguments": {"script": "B"}},
                {"tool_name": "first", "arguments": {"script": "A"}},
            ]
        },
        expected,
        False,
        0.0,
    )
