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
import threading
import pytest
from pathlib import Path
from types import SimpleNamespace

import agent_service_eval as harness
import httpx
from deepeval import assert_test
from deepeval.test_case import LLMTestCase

from agent_service_eval import (
    AgentServiceClient,
    EvalLogger,
    ManagedAgentService,
    MockRule,
    ROOT,
    Scenario,
    ScenarioCase,
    TelemetryConfig,
    build_diagnostics,
    filter_scenario_cases,
    load_eval_config,
    load_scenarios,
    parse_scenario_filter_tokens,
    parse_telemetry_config,
    run_scenario,
)
from metrics import HARNESS_CONFIG_METRICS, TOOL_SEQUENCE_POLICY_METRICS


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
            mock_rules=(
                MockRule(
                    tool_name="execute_csharp_script_in_unity_editor",
                    argument_name="script",
                    contains=("success",),
                    result_is_error=False,
                    result_text="ok",
                    marks_success=True,
                    once=False,
                ),
            ),
            policy={"tool_name": "execute_csharp_script_in_unity_editor"},
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
    config = load_eval_config(SKILL_NAME, UNIT_LOGGER)
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
        config = load_eval_config(SKILL_NAME, UNIT_LOGGER)
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
        config = load_eval_config(SKILL_NAME, UNIT_LOGGER)
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
                yield f"data: {json.dumps(payload)}\n\n".encode("utf-8")

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/events":
            return httpx.Response(200, stream=PromptGatedSseStream())
        if request.url.path == "/api/sessions/send":
            prompt_sent.set()
            return httpx.Response(202)
        return httpx.Response(404)

    config = load_eval_config(SKILL_NAME, UNIT_LOGGER)
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
            yield f"data: {json.dumps(payload)}\n\n".encode("utf-8")

    def handler(request: httpx.Request) -> httpx.Response:
        if request.url.path == "/events":
            return httpx.Response(200, stream=AssistantOnlySseStream())
        if request.url.path == "/api/sessions/send":
            prompt_sent.set()
            return httpx.Response(202)
        return httpx.Response(404)

    config = load_eval_config(SKILL_NAME, UNIT_LOGGER)
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
    config = load_eval_config(SKILL_NAME, UNIT_LOGGER)

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

    monkeypatch.setattr(harness, "AgentServiceClient", FakeClient)
    monkeypatch.setattr(harness, "SseTraceCapture", FakeCapture)

    run = run_scenario(config, scenario, UNIT_LOGGER)

    assert run.success_observed is False
    assert run.reason == "Session became idle before the configured success mock rule was observed."
    assert run.diagnostics["session_event_types_tail"] == ["SessionIdle"]


def test_build_diagnostics_includes_source_event_type_and_content_length():
    config = load_eval_config(SKILL_NAME, UNIT_LOGGER)
    scenario = make_scenario_case(SKILL_NAME, "diagnostics").scenario
    capture = SimpleNamespace(
        events=[
            {
                "SessionId": "diagnostics-session",
                "Type": "AssistantMessage",
                "Content": "hello",
                "SourceJson": json.dumps({"type": "assistant.message"}),
            }
        ],
        tool_calls=[],
    )

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


def test_tool_sequence_policy_metric_accepts_configured_recovery_sequence():
    UNIT_LOGGER.log("scenario_metric_test_start", skill_name=SKILL_NAME)
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
