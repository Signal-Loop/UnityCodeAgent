from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from .artifacts import EvalLogger
from .env import load_dotenv_files
from .models import EvalConfig, MockRule, ProviderConfig, Scenario, TelemetryConfig
from .paths import DEFAULT_ENDPOINT_MANIFEST, ROOT, SKILLS_ROOT, get_managed_service_url, get_managed_working_directory
from .utils import get_value, load_toml


def load_eval_config(skill_name: str, logger: EvalLogger | None = None) -> EvalConfig:
    env_files = load_dotenv_files(skill_name, logger)
    config_path = SKILLS_ROOT / skill_name / "evals" / "config.toml"
    data = load_toml(config_path)
    provider_data = data["provider"]
    service_data = data.get("service", {})
    session_data = data.get("session", {})
    telemetry = parse_telemetry_config(data.get("telemetry", {}))

    config = EvalConfig(
        skill_name=skill_name,
        service_url=resolve_service_url(),
        provider=ProviderConfig(
            model=provider_data["model"],
            type=provider_data.get("type"),
            base_url=provider_data.get("base_url"),
            api_key_env=provider_data.get("api_key_env"),
            wire_api=provider_data.get("wire_api"),
        ),
        telemetry=telemetry,
        working_directory=resolve_working_directory(session_data),
        skill_directories=tuple(session_data.get("skill_directories", [".agents/skills"])),
        disabled_skills=tuple(session_data.get("disabled_skills", [])),
        request_timeout_seconds=float(service_data.get("request_timeout_seconds", 30)),
        scenario_timeout_seconds=float(service_data.get("scenario_timeout_seconds", 180)),
        idle_timeout_seconds=float(service_data.get("idle_timeout_seconds", 45)),
        preflight_timeout_seconds=float(service_data.get("preflight_timeout_seconds", 10)),
        tool_definitions=tuple(data.get("tools", {}).get("definitions", [])),
        env_files_loaded=tuple(str(path.relative_to(ROOT)) for path in env_files),
    )
    if logger:
        logger.log(
            "config_loaded",
            service_url=config.service_url,
            provider_model=config.provider.model,
            api_key_env=config.provider.api_key_env,
            api_key_present=bool(config.provider.api_key),
            telemetry_enabled=config.telemetry.enabled,
            telemetry_otlp_endpoint=config.telemetry.otlp_endpoint,
        )
    return config


def parse_telemetry_config(data: dict[str, Any] | None) -> TelemetryConfig:
    data = data or {}
    return TelemetryConfig(
        enabled=bool(data.get("enabled", True)),
        capture_content=bool(data.get("capture_content", True)),
        otlp_endpoint=data.get("otlp_endpoint"),
    )


def load_scenarios(skill_name: str) -> list[Scenario]:
    path = SKILLS_ROOT / skill_name / "evals" / "scenarios.toml"
    data = load_toml(path)
    scenarios: list[Scenario] = []
    for item in data.get("scenario", []):
        mock_rules = tuple(
            MockRule(
                tool_name=rule.get("tool_name", item.get("tool_name", "")),
                argument_name=rule.get("argument_name"),
                contains=tuple(rule.get("contains", [])),
                result_is_error=bool(rule.get("result_is_error", False)),
                result_text=rule.get("result_text", ""),
                marks_success=bool(rule.get("marks_success", False)),
                once=bool(rule.get("once", False)),
            )
            for rule in item.get("mock_rule", [])
        )
        policy = dict(item.get("policy", {}))
        if "tool_name" not in policy:
            policy["tool_name"] = item.get("tool_name")
        scenarios.append(
            Scenario(
                id=item["id"],
                prompt=item["prompt"],
                tool_name=item.get("tool_name", ""),
                mock_rules=mock_rules,
                policy=policy,
                fallback_result_is_error=bool(item.get("fallback_result_is_error", True)),
                fallback_result_text=item.get(
                    "fallback_result_text",
                    "The eval harness rejected this tool call because no configured mock rule matched it.",
                ),
            )
        )
    return scenarios


def resolve_service_url() -> str:
    if get_managed_service_url():
        return get_managed_service_url()

    manifest_path = DEFAULT_ENDPOINT_MANIFEST
    if not manifest_path.exists():
        raise FileNotFoundError(
            f"Endpoint manifest was not found at {manifest_path}. "
            "Managed eval runs require the service to publish this file."
        )

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    port = get_value(manifest, "Port", "port")
    if not port:
        raise ValueError(f"Endpoint manifest at {manifest_path} did not contain a port.")

    return f"http://127.0.0.1:{port}"


def resolve_working_directory(session_data: dict[str, Any]) -> Path:
    if get_managed_working_directory():
        return get_managed_working_directory()
    return (ROOT / session_data.get("working_directory", ".")).resolve()


