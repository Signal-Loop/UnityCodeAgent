from __future__ import annotations

import json
from typing import Any, cast

from deepeval.metrics import BaseMetric
from deepeval.test_case import LLMTestCase


def _load_json_object(value: str | None, field_name: str) -> dict[str, Any]:
    if value is None:
        raise ValueError(f"DeepEval test case {field_name} was not set.")
    parsed = json.loads(value)
    if not isinstance(parsed, dict):
        raise ValueError(f"DeepEval test case {field_name} must contain a JSON object.")
    return cast(dict[str, Any], parsed)


class HarnessConfigMetric(BaseMetric):
    def __init__(self, threshold: float = 1.0):
        self.threshold = threshold
        self.score = 0.0
        self.success = False
        self.reason = ""
        self.error = None

    def measure(self, test_case: LLMTestCase) -> float:
        try:
            actual = _load_json_object(test_case.actual_output, "actual_output")
            expected = _load_json_object(test_case.expected_output, "expected_output")
            mismatches = [key for key, expected_value in expected.items() if actual.get(key) != expected_value]
            if mismatches:
                score = 0.0
                self.reason = f"Config values did not match expected fields: {', '.join(mismatches)}"
            else:
                score = 1.0
                self.reason = "Eval harness config and committed scenarios loaded as expected."
            self.score = score
            self.success = score >= self.threshold
            return score
        except Exception as error:
            self.error = str(error)
            raise

    async def a_measure(self, test_case: LLMTestCase) -> float:
        return self.measure(test_case)

    def is_successful(self) -> bool:
        if self.error is not None:
            self.success = False
        else:
            self.success = (self.score or 0.0) >= self.threshold
        return self.success

    @property
    def __name__(self) -> str:  # type: ignore[reportIncompatibleMethodOverride]
        return "Harness Config"


class ToolSequencePolicyMetric(BaseMetric):
    def __init__(self, threshold: float = 1.0):
        self.threshold = threshold
        self.score = 0.0
        self.success = False
        self.reason = ""
        self.error = None

    def measure(self, test_case: LLMTestCase) -> float:
        try:
            payload = _load_json_object(test_case.actual_output, "actual_output")
            expected = _load_json_object(test_case.expected_output, "expected_output")
            score, reason = self._score(payload, expected)
            self.score = score
            self.reason = reason
            self.success = score >= self.threshold
            return score
        except Exception as error:
            self.error = str(error)
            raise

    async def a_measure(self, test_case: LLMTestCase) -> float:
        return self.measure(test_case)

    def is_successful(self) -> bool:
        if self.error is not None:
            self.success = False
        else:
            self.success = (self.score or 0.0) >= self.threshold
        return self.success

    @property
    def __name__(self) -> str:  # type: ignore[reportIncompatibleMethodOverride]
        return "Tool Sequence Policy"

    def _score(self, payload: dict[str, Any], expected: dict[str, Any]) -> tuple[float, str]:
        calls = payload.get("tool_calls", [])
        tool_name = expected["tool_name"]
        matching_calls = [call for call in calls if call.get("tool_name") == tool_name]
        if not matching_calls:
            return 0.0, f"Expected at least one {tool_name} call."

        first_call = matching_calls[0]
        first_script = _argument_text(first_call, expected.get("argument_name", "script"))
        for required in expected.get("first_call_contains", []):
            if required not in first_script:
                return 0.0, f"First {tool_name} call did not contain required text: {required}"

        if "first_call_result_is_error" in expected:
            expected_error = bool(expected["first_call_result_is_error"])
            if first_call.get("result_is_error") is not expected_error:
                return 0.0, f"First {tool_name} call result_is_error was not {expected_error}."

        follow_up_calls = matching_calls[1:]
        follow_up_text = "\n".join(_argument_text(call, expected.get("argument_name", "script")) for call in follow_up_calls)
        for pattern in expected.get("follow_up_forbidden", []):
            if pattern and pattern in follow_up_text:
                return 0.0, f"Forbidden follow-up pattern observed: {pattern}"

        for required in expected.get("follow_up_contains", []):
            if required not in follow_up_text:
                return 0.0, f"Follow-up {tool_name} calls did not contain required text: {required}"

        if expected.get("require_success_observed", True) and payload.get("success_observed") is not True:
            return 0.0, payload.get("reason", "Harness did not observe the configured success condition.")

        return 1.0, expected.get("success_reason", "Tool call sequence matched the configured policy.")


def _argument_text(call: dict[str, Any], argument_name: str) -> str:
    arguments = call.get("arguments", {})
    value = arguments.get(argument_name, "")
    return value if isinstance(value, str) else ""


HARNESS_CONFIG_METRICS: list[BaseMetric] = [HarnessConfigMetric()]
TOOL_SEQUENCE_POLICY_METRICS: list[BaseMetric] = [ToolSequencePolicyMetric()]
