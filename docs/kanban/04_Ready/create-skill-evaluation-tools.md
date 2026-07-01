# Create skill evaluation tools

- goal: Produce a researched implementation plan for reusable DeepEval-based skill evaluation tools that run against the real local Copilot service endpoints, keep skill-specific eval data under each skill, and use service/SDK telemetry evidence to diagnose missing-assembly behavior.
- updated: 2026-06-30
- steps:
    - [x] Research current service endpoint and telemetry behavior
    - [x] Research DeepEval agent evaluation approach
    - [x] Plan reusable Python eval tooling and skill-local eval configuration
    - [ ] Implement scoped change
    - [ ] Run focused verification

Currently, the `executing-csharp-scripts-in-unity-editor` skill is failing to properly handle missing assemblies. Create python tools that can be used to evaluate and improve skills.

- use DeepEval framework
- Use DeepEval skill and do internet research how to best approach it, specifically skills evaluation with deepeval
- use python scripts obeying 'project-python' skill
- solution is generic - not for single skill, but can be resused to other skills or tools descriptions.
- skill specific information needed for evals (like eval text) should be located in skill directory in `evals` subfolder. Eval configuration should also be located there
- python scripts should be in /Python/evals folder
- other temporary artifacts should be in /evals folder
- model/provider used for tests should be configurable
- evals shoul use existing Copilot service endpoints, so real agent is tested


`executing-csharp-scripts-in-unity-editor` failure details:
When there are no additional assemblies added in settings, and for example script uses Image, this error is returned: `error CS0234: The type or namespace name 'UI' does not exist in the namespace 'UnityEngine' (are you missing an assembly reference?)`. To solve this problem, agent uses reflection or load assembly dynamically. This works, but missing assembluy should be added to additional assemmblies in settings instead. This is described in skill, but agent does not follow it.

Goal: Create DeepEval based evals that can be used to improve `executing-csharp-scripts-in-unity-editor` missing assembly behaviour.

example tests:
Create gameobject and add `Image` to it
Create gameobject and add `Rigidbody2d` to it

Research:
- The service contract already exposes the real evaluation entry point over loopback HTTP: create/open sessions with `/api/sessions/create` and `/api/sessions/open`, send prompts with `/api/sessions/send`, complete Unity tool calls with `/api/tools/results`, and observe results through `/events` SSE. The eval tools should use those endpoints instead of directly importing service internals.
- `ServiceBootstrap` launches the service with telemetry arguments from `UnityCodeAgentSettings`. `TelemetryMode.File` passes `--EnableTelemetry=true` and optional `--TelemetryFilePath`, without `--OtlpEndpoint`; `TelemetryMode.OtlpEndpoint` passes `--OtlpEndpoint` and disables file path arguments.
- Service-side OpenTelemetry is configured in `TelemetryServiceCollectionExtensions`. It exports service ASP.NET/Core spans and metrics to an OTLP HTTP endpoint only when `OtlpEndpoint` or `OTEL_EXPORTER_OTLP_ENDPOINT` is set. It does not write service OpenTelemetry spans to a file.
- SDK/CLI telemetry is configured in `CliTelemetryConfigFactory`. When telemetry is enabled and no OTLP endpoint is configured, it creates a GitHub Copilot SDK `TelemetryConfig` with `ExporterType = "file"`, `SourceName = "UnityCodeCopilot.Cli"`, and `FilePath = .unityCodeAgent/service/logs/telemetry.jsonl` unless `TelemetryFilePath` is supplied.
- The local Copilot SDK source confirms `TelemetryConfig` maps `FilePath` to `COPILOT_OTEL_FILE_EXPORTER_PATH`, `ExporterType` to `COPILOT_OTEL_EXPORTER_TYPE`, and `CaptureContent` to `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`. Its telemetry E2E test reads JSONL entries with fields like `type`, `traceId`, `spanId`, `parentSpanId`, `status`, `instrumentationScope.name`, and `attributes`.
- DeepEval docs recommend evaluating agents both end-to-end and with component-level trace evidence. `TaskCompletionMetric` and `StepEfficiencyMetric` are trace-based metrics; for this repo, the initial durable suite should combine DeepEval judge metrics over the final transcript with deterministic telemetry assertions over SDK JSONL spans because the service already emits SDK tool/LLM spans to file.
- DeepEval `ConversationSimulator` and multi-turn metrics are useful for broad chat behavior, but the immediate missing-assembly failure can be a deterministic single-scenario agent eval: one prompt, real service session, observed SSE transcript, parsed telemetry spans, and a task-specific pass/fail rubric.

Plan:
- Add generic Python scripts under `Python/evals/`, using PEP 723 inline dependencies and `uv run`:
  - `run_skill_eval.py`: load a skill-local eval config, resolve the service endpoint manifest or explicit base URL, run each scenario against the real service endpoints, collect SSE events until `SessionIdle` or timeout, parse telemetry JSONL if configured, and emit result artifacts under `/evals`.
  - `skill_eval_lib.py` should not be a separate module because project Python rules prefer single-file scripts; keep shared helper code inside `run_skill_eval.py` until duplication becomes concrete.
- Add skill-local eval artifacts under `Packages/com.signal-loop.unitycodeagent/Editor/Skills~/executing-csharp-scripts-in-unity-editor/evals/`:
  - `missing-assemblies.yaml` with scenario prompts for `Image` and `Rigidbody2D`, expected assembly additions, timeout/model/provider options, telemetry path override, and success criteria.
  - Optional prompt/rubric markdown only if the YAML becomes hard to read; keep all skill-specific eval text in this `evals` folder.
- Use `/evals/` at the repo root only for generated run artifacts: transcripts, parsed telemetry summaries, DeepEval result JSON, and per-scenario logs. Avoid committing large or transient result files unless a later task explicitly asks.
- The runner should be configurable through CLI flags and config keys for model/provider, base URL, manifest path, skill eval path, telemetry JSONL path, output directory, timeout, and whether to require telemetry.
- DeepEval integration:
  - Use `deepeval` as an inline dependency in the runner.
  - Convert each scenario result into an `LLMTestCase` with `input`, `actual_output`, and expected rubric text.
  - Start with 3 high-signal checks: a task-specific `GEval` for "missing assembly resolution", `TaskCompletionMetric` where the available DeepEval version supports non-traced cases, and deterministic Python assertions over events/telemetry for required behavior.
  - If DeepEval trace-only metrics cannot consume the existing SDK JSONL directly, do not fake traces. Keep trace-derived assertions deterministic and leave a clear TODO for a later OpenTelemetry-to-DeepEval trace bridge.
- Service/SSE behavior:
  - Create a new session per scenario with the configured provider/model, skill directories, disabled skills, and working directory.
  - Subscribe to `/events`, send the scenario prompt, respond to Unity tool invocation requests only if the eval runner can safely call or proxy the required Unity tool path. If Unity-side tool execution is not available from Python, fail with a clear prerequisite rather than silently mocking the tool.
  - Record assistant, tool, error, skill, diagnostic, and session-idle events for the scenario.
- Telemetry behavior:
  - Prefer `TelemetryMode.File` or explicit `TelemetryFilePath` so the runner can parse SDK JSONL locally.
  - Parse spans by `traceId`/time window and inspect `gen_ai.operation.name`, `gen_ai.tool.name`, `gen_ai.tool.call.arguments`, `gen_ai.tool.call.result`, and error/status fields.
  - For the missing-assembly scenarios, assert that the run shows the intended settings-edit path instead of reflection/dynamic assembly loading, and that the final result is not the original `CS0234` failure.
- Focus the first implementation on the `executing-csharp-scripts-in-unity-editor` missing-assembly cases while keeping the runner and config schema generic enough for other skills.

Verification:
- Run script-level smoke checks with `uv run Python/evals/run_skill_eval.py --help` and, if YAML is used, `uv run --with pyyaml Python/evals/run_skill_eval.py --validate-config Packages/com.signal-loop.unitycodeagent/Editor/Skills~/executing-csharp-scripts-in-unity-editor/evals/missing-assemblies.yaml`.
- Run the narrow service tests only if service contracts or DTO assumptions are changed. If no service code changes are made, skip `dotnet test` and record why.
- If Unity and the real service are available, run one eval scenario against the real endpoint with a temporary output path under `/evals/` and verify it captures transcript plus telemetry JSONL summary.
