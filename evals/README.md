# UnityCodeAgent evals

This folder contains reusable DeepEval harness code and committed pytest evals for agent, skill, and tool behavior.

Live evals use the real UnityCodeAgent service endpoints:

- `POST /api/sessions/create`
- `POST /api/sessions/send`
- `GET /events`
- `POST /api/tools/results`
- `POST /api/sessions/abort`

The harness intercepts `ToolInvocationRequest` SSE events and posts mocked tool results back to the service, so Unity project files and settings are not modified.

Live runs write diagnostics under `evals/.artifacts/<skill>/<run_id>/`, including `events.jsonl` and `summary.json`. These files are local artifacts and are ignored by git.

Provider keys are loaded from the repository root `.env` first, then from an optional skill-local `<skill>/evals/.env`. Secret values are never printed; diagnostics only report whether the configured key is present.

On Windows, add `PYTHONUTF8=1` to the root `.env` and pass `--env-file .env` to `uv run`. DeepEval/Rich prints Unicode status glyphs, and Python must enter UTF-8 mode before the interpreter starts.

Run fast checks before live scenarios:

```powershell
uv run --env-file .env --with deepeval --with httpx --with pytest python -m compileall evals
uv run --env-file .env --with deepeval --with httpx --with pytest deepeval test run evals/test_eval_harness_unit.py --identifier "eval-harness-generic"
```

Run the live preflight before full scenarios:

```powershell
$env:UNITYCODEAGENT_EVAL_LIVE = "1"
uv run --env-file .env --with deepeval --with httpx --with pytest deepeval test run evals/test_live_preflight.py --identifier "eval-live-preflight"
```

Run the current suite with a running service and provider credentials:

```powershell
$env:UNITYCODEAGENT_EVAL_LIVE = "1"
uv run --env-file .env --with deepeval --with httpx --with pytest deepeval test run evals/test_skill_scenarios.py --mark unitycodeagent --identifier "unitycodeagent-scenarios"
```

The `--mark unitycodeagent` argument selects scenarios for that skill. Omit `--mark` to run every configured skill scenario.
The `--identifier` argument is a DeepEval run label used for output/reporting; it does not select tests.

Override the service URL when the runtime manifest is not present:

```powershell
$env:UNITYCODEAGENT_EVAL_SERVICE_URL = "http://127.0.0.1:7777"
```
