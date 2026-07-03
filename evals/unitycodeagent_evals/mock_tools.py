from __future__ import annotations

from typing import Any

from .artifacts import EvalLogger
from .models import MockRule, Scenario, ToolCall


class MockToolRouter:
    def __init__(self, scenario: Scenario, logger: EvalLogger | None = None):
        self.scenario = scenario
        self.logger = logger
        self.success_observed = False
        self._used_once_rules: set[int] = set()

    def complete(self, call: ToolCall) -> tuple[bool, str]:
        for index, rule in enumerate(self.scenario.mock_rules):
            if rule.once and index in self._used_once_rules:
                continue
            if self._matches(rule, call):
                if rule.once:
                    self._used_once_rules.add(index)
                if rule.marks_success:
                    self.success_observed = True
                self._log(
                    "mock_rule_matched",
                    scenario_id=self.scenario.id,
                    tool_name=call.tool_name,
                    rule_index=index,
                    marks_success=rule.marks_success,
                    result_is_error=rule.result_is_error,
                )
                return rule.result_is_error, rule.result_text

        self._log("mock_rule_missed", scenario_id=self.scenario.id, tool_name=call.tool_name)
        return self.scenario.fallback_result_is_error, self.scenario.fallback_result_text

    def _matches(self, rule: MockRule, call: ToolCall) -> bool:
        if call.tool_name != rule.tool_name:
            return False
        if not rule.contains:
            return True
        source = call.arguments.get(rule.argument_name, "") if rule.argument_name else call.arguments_json
        if not isinstance(source, str):
            return False
        return all(value in source for value in rule.contains)

    def _log(self, event: str, **data: Any) -> None:
        if self.logger:
            self.logger.log(event, **data)


