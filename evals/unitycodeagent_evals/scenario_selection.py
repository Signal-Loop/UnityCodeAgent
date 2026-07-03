from __future__ import annotations

import re

from .config import load_scenarios
from .models import ScenarioCase
from .paths import SKILLS_ROOT


def discover_configured_skill_names() -> list[str]:
    names = [
        path.parent.parent.name
        for path in SKILLS_ROOT.glob("*/evals/scenarios.toml")
        if (path.parent / "config.toml").exists()
    ]
    return sorted(names)


def parse_scenario_filter_tokens(values: str | list[str] | tuple[str, ...] | None) -> tuple[str, ...]:
    if values is None:
        return ()
    raw_values = (values,) if isinstance(values, str) else tuple(values)
    tokens: list[str] = []
    seen: set[str] = set()
    for value in raw_values:
        for token in re.split(r"[\s,]+", value.strip()):
            if not token or token in seen:
                continue
            seen.add(token)
            tokens.append(token)
    return tuple(tokens)


def discover_scenario_cases(skill_names: list[str] | tuple[str, ...] | None = None) -> list[ScenarioCase]:
    names = list(skill_names) if skill_names is not None else discover_configured_skill_names()
    return [ScenarioCase(skill_name, scenario) for skill_name in names for scenario in load_scenarios(skill_name)]


def filter_scenario_cases(cases: list[ScenarioCase], filter_values: str | list[str] | tuple[str, ...] | None) -> list[ScenarioCase]:
    tokens = parse_scenario_filter_tokens(filter_values)
    if not tokens:
        return cases

    cases_by_skill: dict[str, list[ScenarioCase]] = {}
    cases_by_key: dict[str, ScenarioCase] = {}
    for case in cases:
        cases_by_skill.setdefault(case.skill_name, []).append(case)
        cases_by_key[case.key] = case

    selected: list[ScenarioCase] = []
    selected_keys: set[str] = set()
    unknown_skills: list[str] = []
    unknown_scenarios: list[str] = []

    def add_case(case: ScenarioCase) -> None:
        if case.key in selected_keys:
            return
        selected_keys.add(case.key)
        selected.append(case)

    for token in tokens:
        if "." not in token:
            skill_cases = cases_by_skill.get(token)
            if not skill_cases:
                unknown_skills.append(token)
                continue
            for case in skill_cases:
                add_case(case)
            continue

        skill_name, scenario_id = token.split(".", 1)
        if skill_name not in cases_by_skill:
            unknown_skills.append(skill_name)
            continue
        case = cases_by_key.get(f"{skill_name}.{scenario_id}")
        if case is None:
            unknown_scenarios.append(token)
            continue
        add_case(case)

    if unknown_skills or unknown_scenarios:
        available_skills = ", ".join(sorted(cases_by_skill)) or "(none)"
        available_scenarios = ", ".join(sorted(cases_by_key)) or "(none)"
        errors: list[str] = []
        if unknown_skills:
            errors.append(f"unknown skills: {', '.join(sorted(set(unknown_skills)))}")
        if unknown_scenarios:
            errors.append(f"unknown scenarios: {', '.join(sorted(set(unknown_scenarios)))}")
        raise ValueError(
            "Invalid eval scenario filter; "
            + "; ".join(errors)
            + f". Available skills: {available_skills}. Available scenarios: {available_scenarios}."
        )

    return selected


def select_scenario_cases(filter_values: str | list[str] | tuple[str, ...] | None = None) -> list[ScenarioCase]:
    return filter_scenario_cases(discover_scenario_cases(), filter_values)

