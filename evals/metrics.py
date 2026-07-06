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
        self.default_threshold = threshold
        self.threshold = threshold
        self.score = 0.0
        self.success = False
        self.reason = ""
        self.error = None

    def measure(self, test_case: LLMTestCase) -> float:
        try:
            payload = _load_json_object(test_case.actual_output, "actual_output")
            expected = _load_json_object(test_case.expected_output, "expected_output")
            self.threshold = float(expected.get("threshold", self.default_threshold))
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
        calls = _as_dict_list(payload.get("tool_calls", []))
        expected_calls = _as_dict_list(expected.get("expected_tool", []))

        if payload.get("failed") is True:
            return 0.0, payload.get("reason", "Scenario run failed before metric scoring.")

        if not expected_calls:
            if calls:
                return 0.0, "No tool calls were expected, but actual tool calls were observed."
            return 1.0, expected.get("success_reason", "No tool calls were expected or observed.")

        for expected_call in expected_calls:
            if expected_call.get("required") and not any(_matches_expected(expected_call, call) for call in calls):
                return 0.0, f"Missing required tool call: {_expected_call_label(expected_call)}"

        if expected.get("should_exact_match", False):
            score = _score_exact(expected_calls, calls)
        elif expected.get("should_consider_ordering", False):
            score = _score_ordered(expected_calls, calls)
        else:
            score = _score_unordered(expected_calls, calls)

        if score >= self.threshold:
            reason = expected.get("success_reason", "Tool call sequence matched the configured policy.")
        else:
            reason = f"Tool call sequence score {score:.3f} was below threshold {self.threshold:.3f}."
        return score, reason


def _as_dict_list(value: Any) -> list[dict[str, Any]]:
    if not isinstance(value, list):
        return []
    return [item for item in value if isinstance(item, dict)]


def _matches_expected(expected_call: dict[str, Any], actual_call: dict[str, Any]) -> bool:
    if actual_call.get("tool_name") != expected_call.get("tool_name"):
        return False
    if "result_is_error" in expected_call and actual_call.get("result_is_error") is not bool(expected_call["result_is_error"]):
        return False

    argument_name = expected_call.get("argument_name", "script")
    text = _argument_text(actual_call, argument_name if isinstance(argument_name, str) else "script")
    if any(required not in text for required in expected_call.get("arguments_contain", [])):
        return False
    return not any(pattern and pattern in text for pattern in expected_call.get("arguments_forbid", []))


def _score_exact(expected_calls: list[dict[str, Any]], actual_calls: list[dict[str, Any]]) -> float:
    if len(expected_calls) != len(actual_calls):
        return 0.0
    return (
        1.0
        if all(_matches_expected(expected_call, actual_call) for expected_call, actual_call in zip(expected_calls, actual_calls, strict=True))
        else 0.0
    )


def _score_ordered(expected_calls: list[dict[str, Any]], actual_calls: list[dict[str, Any]]) -> float:
    rows = len(expected_calls) + 1
    columns = len(actual_calls) + 1
    lengths = [[0] * columns for _ in range(rows)]
    for expected_index, expected_call in enumerate(expected_calls, start=1):
        for actual_index, actual_call in enumerate(actual_calls, start=1):
            if _matches_expected(expected_call, actual_call):
                lengths[expected_index][actual_index] = lengths[expected_index - 1][actual_index - 1] + 1
            else:
                lengths[expected_index][actual_index] = max(
                    lengths[expected_index - 1][actual_index],
                    lengths[expected_index][actual_index - 1],
                )
    return lengths[-1][-1] / len(expected_calls)


def _score_unordered(expected_calls: list[dict[str, Any]], actual_calls: list[dict[str, Any]]) -> float:
    used_actual_indexes: set[int] = set()
    matches = 0
    for expected_call in expected_calls:
        for index, actual_call in enumerate(actual_calls):
            if index in used_actual_indexes:
                continue
            if _matches_expected(expected_call, actual_call):
                used_actual_indexes.add(index)
                matches += 1
                break
    return matches / len(expected_calls)


def _expected_call_label(expected_call: dict[str, Any]) -> str:
    tool_name = expected_call.get("tool_name", "<missing tool_name>")
    contains = expected_call.get("arguments_contain", [])
    if contains:
        return f"{tool_name} containing {', '.join(str(value) for value in contains)}"
    return str(tool_name)


def _argument_text(call: dict[str, Any], argument_name: str) -> str:
    arguments = call.get("arguments", {})
    value = arguments.get(argument_name, "")
    return value if isinstance(value, str) else ""


HARNESS_CONFIG_METRICS: list[BaseMetric] = [HarnessConfigMetric()]
TOOL_SEQUENCE_POLICY_METRICS: list[BaseMetric] = [ToolSequencePolicyMetric()]
