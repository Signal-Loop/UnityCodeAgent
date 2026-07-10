# UnityCodeAgent evals

This folder contains reusable DeepEval harness code and committed pytest evals for agent, skill, and tool behavior.

## Project Setup

The eval harness is managed as its own Python project from this `evals/` directory.

```powershell
cd evals
uv sync
uv lock
```

Use `uv run --env-file .env ...` for checks that need the repository root `.env`. Unit checks can use the startup config path and do not require a published endpoint manifest. Live checks still need the managed service to publish `.unityCodeAgent/service/runtime/endpoint.json`. On Windows, keep `PYTHONUTF8=1` in `.env` so DeepEval/Rich output uses UTF-8 from interpreter startup.

## Module Layout

Harness code lives under `unitycodeagent_evals/`:

- `paths.py`: repository paths and managed-service context.
- `models.py`: shared dataclasses for config, scenarios, tool calls, and scenario runs.
- `env.py`: `.env` parsing/loading.
- `config.py`: TOML loading, startup/live service URL resolution, and scenario loading.
- `scenario_selection.py`: skill/scenario discovery and filter handling.
- `artifacts.py`: `EvalLogger` and JSONL/summary artifact writing.
- `client.py`: Agent Service HTTP and wait-for-SSE helpers.
- `sse.py`: live SSE trace capture and tool invocation parsing.
- `mock_tools.py`: mock tool response routing.
- `runner.py`: scenario orchestration and diagnostics.
- `managed_service.py`: no-Unity service process lifecycle.

## What Runs

Live evals start a managed no-Unity UnityCodeAgent service and use the real service endpoints:

- `POST /api/sessions/create`
- `POST /api/sessions/send`
- `GET /events`
- `POST /api/tools/results`
- `POST /api/sessions/abort`

The harness intercepts `ToolInvocationRequest` SSE events and posts mocked tool results back to the service, so Unity project files and settings are not modified.

Live runs write diagnostics under a shared eval root in `.artifacts/<run_id>/`, with scenario logs under `.artifacts/<run_id>/unitycodeagent/<scenario_run_id>/` and managed-service logs under `.artifacts/<run_id>/managed-service/<service_run_id>/`. Each subfolder still contains the same files as before, including `events.jsonl`, `summary.json`, stdout/stderr, the endpoint manifest, and the default telemetry file. These files are local artifacts and are ignored by git.

Unit evals use `load_managed_service_startup_config(...)` to load the same committed TOML and environment inputs without requiring a live endpoint manifest. Live evals use the resolved service URL from `resolve_service_url()` and therefore require the managed service to publish the endpoint manifest first.

Provider keys are loaded from the repository root `.env` first, then from an optional skill-local `<skill>/evals/.env`. Secret values are never printed; diagnostics only report whether the configured key is present.

## `config.toml` Reference

The eval harness reads `Packages/com.signal-loop.unitycodeagent/Editor/Skills~/unitycodeagent/evals/config.toml`.
The file is split into four main sections:

- `[service]` controls how the harness finds and talks to the service.
- `[provider]` defines the model provider and API key source.
- `[telemetry]` controls service telemetry for managed live runs.
- `[session]` controls which skill folders are mounted into the managed project.
- `[[tools.definitions]]` registers tool schemas used by scenarios.

### `service`

The managed live eval service binds to an ephemeral loopback port and publishes the chosen port in `.unityCodeAgent/service/runtime/endpoint.json`. The live harness requires that manifest to exist and will fail if it cannot read it. Startup-only unit checks bypass this requirement by passing `http://127.0.0.1:0` through `load_managed_service_startup_config(...)`.

| Key | Type | Meaning |
| --- | --- | --- |
| `request_timeout_seconds` | number | Timeout used for single HTTP calls to the service. |
| `scenario_timeout_seconds` | number | Timeout used when a full scenario waits for SSE progress and final output. |
| `idle_timeout_seconds` | number | Maximum time the harness waits without progress before it treats the scenario as stalled. |
| `preflight_timeout_seconds` | number | Short timeout used by the live preflight check before longer scenarios run. |

Example:

```toml
[service]
request_timeout_seconds = 30
scenario_timeout_seconds = 180
idle_timeout_seconds = 45
preflight_timeout_seconds = 30
```

### `provider`

| Key | Type | Meaning |
| --- | --- | --- |
| `model` | string | Model identifier passed to the provider. |
| `type` | string, optional | Provider type forwarded to the service contract. |
| `base_url` | string, optional | Base URL for the provider API. |
| `api_key_env` | string, optional | Name of the environment variable that contains the API key. The harness resolves the value at runtime. |
| `wire_api` | string, optional | Provider wire API mode forwarded to the service. |

`api_key_env` does not store the secret itself. The value is read from the process environment after `.env` files are loaded.

Example:

```toml
[provider]
model = "xiaomi/mimo-v2.5"
type = "openai"
base_url = "https://openrouter.ai/api/v1"
api_key_env = "OPENROUTER_API_KEY"
wire_api = "chat"
```

### `telemetry`

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `enabled` | bool | `true` | Enables or disables telemetry entirely. If `false`, the managed service creates no telemetry exporter. |
| `capture_content` | bool | `true` | Allows the telemetry exporter to include prompt and response content when supported. |
| `otlp_endpoint` | string, optional | empty | OTLP HTTP endpoint to use instead of file telemetry. Must be an absolute `http://` or `https://` URL. |

This is the part you want when adding a telemetry endpoint.

Use this shape:

```toml
[telemetry]
enabled = true
capture_content = true
otlp_endpoint = "http://127.0.0.1:4318"
```

Behavior:

- If `otlp_endpoint` is set, the managed service starts the SDK telemetry exporter in OTLP HTTP mode.
- If `otlp_endpoint` is empty or omitted, the managed service falls back to file telemetry and writes `telemetry.jsonl`.
- If `enabled = false`, telemetry is disabled before the exporter is created.
- `otlp_endpoint` is validated by the service and must be an absolute `http` or `https` URL.
- The service also accepts `OTEL_EXPORTER_OTLP_ENDPOINT` as a fallback when no explicit endpoint is passed.

### `session`

| Key | Type | Meaning |
| --- | --- | --- |
| `working_directory` | string | Root directory used by the harness when it starts the managed service. |
| `skill_directories` | array[string] | Skill directories mounted into the managed project. |
| `disabled_skills` | array[string] | Skills excluded from the managed run. |

### `tools.definitions`

Each entry under `[[tools.definitions]]` becomes a tool definition available to the harness.

| Key | Type | Meaning |
| --- | --- | --- |
| `Name` | string | Tool name used by scenarios and the service contract. |
| `Description` | string | Human-readable description of when to use the tool. |
| `InputSchemaJson` | string | JSON schema for the tool input payload. |

The sample config currently registers these tools:

- `execute_csharp_script_in_unity_editor`
- `read_unity_console_logs`
- `run_unity_tests`

Example:

```toml
[[tools.definitions]]
Name = "read_unity_console_logs"
Description = "Reads recent Unity Editor Console logs."
InputSchemaJson = '{"type":"object","properties":{"max_entries":{"type":"integer","minimum":1,"maximum":1000}}}'
```

## `scenarios.toml` Reference

The eval harness reads `Packages/com.signal-loop.unitycodeagent/Editor/Skills~/unitycodeagent/evals/scenarios.toml`.
Each `[[scenario]]` entry describes one live or mocked scenario for the skill.

### `scenario`

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `id` | string | required | Stable scenario identifier used in test output and metrics. |
| `prompt` | string | required | User prompt sent to the agent. |
| `tool_name` | string | empty | Primary tool the scenario expects the agent to use. |
| `max_tool_calls` | integer | `10` | Maximum captured tool calls before the harness stops the scenario as failed to protect against tool-call loops. |
| `fallback_result_is_error` | bool | `true` | Result flag used when the harness rejects an unmatched tool call. |
| `fallback_result_text` | string | harness default text | Message returned when no mock rule matches the tool call. |

The sample scenarios in this skill use `execute_csharp_script_in_unity_editor` and are written to validate recovery behavior when the first script fails because an assembly is missing.

Minimal example:

```toml
[[scenario]]
id = "example_missing_assembly"
prompt = "Add a Rigidbody2D component to a GameObject."
tool_name = "execute_csharp_script_in_unity_editor"
max_tool_calls = 10
fallback_result_text = "No mock rule matched this tool call."
```

### `scenario.mock_rule`

`[[scenario.mock_rule]]` entries define the tool responses the harness should return when the agent calls a tool.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `tool_name` | string | scenario `tool_name` | Tool name this rule applies to. |
| `argument_name` | string, optional | none | Argument field to inspect when matching text. |
| `contains` | array[string] | empty | All strings must appear in the target argument value for the rule to match. |
| `result_is_error` | bool | `false` | Whether the mocked tool result is treated as an error. |
| `result_text` | string | empty | Mock response text returned to the agent. |
| `once` | bool | `false` | Consumes the rule after the first match. |

Matching behavior:

- Rules are evaluated in the order they appear in the file.
- If `argument_name` is set, the harness checks that argument value.
- If `argument_name` is omitted, the harness matches against the full JSON-serialized tool call payload.
- Every string in `contains` must be present for the rule to match.
- `once = true` is useful for simulating a one-off failure that disappears after the agent retries.

Example:

```toml
[[scenario.mock_rule]]
tool_name = "execute_csharp_script_in_unity_editor"
argument_name = "script"
contains = ["Rigidbody2D"]
result_is_error = true
once = true
result_text = "error CS0246: Rigidbody2D could not be found"
```

### `scenario.policy`

`[scenario.policy]` defines the expected tool sequence for the eval metric. The metric scores captured `tool_calls` against `[[scenario.policy.expected_tool]]` entries.

| Key | Type | Meaning |
| --- | --- | --- |
| `threshold` | number | Minimum score required to pass. Defaults to `1.0`. |
| `should_consider_ordering` | bool | When `true`, expected calls are scored by ordered longest common subsequence. Defaults to `false`. |
| `should_exact_match` | bool | When `true`, actual and expected calls must have identical length and order. This takes precedence over ordering. Defaults to `false`. |
| `success_reason` | string | Human-readable reason recorded when the recovery succeeds. |

Each `[[scenario.policy.expected_tool]]` entry supports:

| Key | Type | Meaning |
| --- | --- | --- |
| `tool_name` | string | Tool expected in the policy check. |
| `argument_name` | string, optional | Argument field checked for matching text. Defaults to `script`. |
| `arguments_contain` | array[string] | Text that must appear in the selected argument. |
| `arguments_forbid` | array[string] | Text that must not appear in the selected argument. |
| `result_is_error` | bool, optional | Expected tool result error flag. |
| `required` | bool | If `true`, the scenario fails when no matching actual call exists, regardless of threshold. |

The current scenarios are all recovery scenarios:

- The first call intentionally fails because the needed Unity assembly is missing.
- The agent is expected to recover by adding the missing assembly.
- The follow-up call must avoid reflection-based workarounds such as `Assembly.Load`, `GetType(`, or `System.Reflection`.
- The scenario stops as failed if captured tool calls reach `max_tool_calls` before the service becomes idle.

Example:

```toml
[scenario.policy]
threshold = 1.0
should_consider_ordering = true
should_exact_match = false
success_reason = "The agent recovered by adding the missing assembly."

[[scenario.policy.expected_tool]]
tool_name = "execute_csharp_script_in_unity_editor"
argument_name = "script"
arguments_contain = ["Rigidbody2D"]
result_is_error = true

[[scenario.policy.expected_tool]]
tool_name = "execute_csharp_script_in_unity_editor"
argument_name = "script"
arguments_contain = ["AddToolAssembly", "UnityEngine.Physics2DModule"]
arguments_forbid = ["Assembly.Load", "GetType(", "System.Reflection"]
result_is_error = false
required = true
```

## Run Checks

Run fast checks before live scenarios. The unit DeepEval run uses the startup config path and is safe without a published endpoint manifest:

```powershell
cd evals
uv run --env-file .env python -m compileall conftest.py metrics.py test_eval_harness_unit.py test_live_preflight.py test_skill_scenarios.py unitycodeagent_evals
uv run ruff check .
uv run --env-file .env deepeval test run test_eval_harness_unit.py --identifier "eval-harness-generic"
```

Run the live preflight before full scenarios. The suite starts `UnityCodeCopilot.Service` with `--NoUnity=true`, waits for `/health`, and stops it through `POST /api/service/stop` after the run:

```powershell
uv run --env-file .env deepeval test run test_live_preflight.py --identifier "eval-live-preflight" -- --live
```

Collect the current suite without live execution. The scenarios are collected, then skipped because `--live` is omitted:

```powershell
uv run --env-file .env deepeval test run test_skill_scenarios.py --identifier "all-scenarios"
```

Run every configured live scenario with provider credentials:

```powershell
uv run --env-file .env deepeval test run test_skill_scenarios.py --identifier "all-live" -- --live
```

Run every live scenario for one skill:

```powershell
uv run --env-file .env deepeval test run test_skill_scenarios.py --identifier "unitycodeagent-live" -- --live --filter unitycodeagent
```

Run every live scenario for multiple skills:

```powershell
uv run --env-file .env deepeval test run test_skill_scenarios.py --identifier "selected-skills" -- --live --filter unitycodeagent,other_skill
```

Run one exact live scenario:

```powershell
uv run --env-file .env deepeval test run test_skill_scenarios.py --identifier "image-scenario" -- --live --filter unitycodeagent.image_missing_ui_assembly
```

Run multiple exact live scenarios:

```powershell
uv run --env-file .env deepeval test run test_skill_scenarios.py --identifier "selected-scenarios" -- --live --filter unitycodeagent.image_missing_ui_assembly,unitycodeagent.rigidbody2d_missing_physics2d_assembly
```

The `--filter` and `--live` arguments are pytest-side options, so pass them after DeepEval's `--` separator. The `--identifier` argument is a DeepEval run label used for output/reporting; it does not select tests.
