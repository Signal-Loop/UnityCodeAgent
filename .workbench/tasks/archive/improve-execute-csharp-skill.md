# Improve execute C# skill assembly guidance
- status: Completed
- order: 1200
- goal: Improve the `executing-csharp-scripts-in-unity-editor` skill so agents prefer adding missing assemblies to `UnityCodeAgentSettings.asset` and reloading settings over reflection workarounds, with old-vs-new skill evals proving the behavior change and no regression to normal Unity editor scripting guidance.
- updated: 2026-06-30
- steps:
    - [x] Snapshot the current skill and create red/green eval prompts for missing assembly behavior.
    - [x] Add a repeatable skill-eval harness with objective assertions and human-review output.
    - [x] Update the packaged skill source and installed workspace copy with settings-first assembly guidance.
    - [x] Run the old-vs-new evals and record quantitative and qualitative results.
    - [ ] Run a fresh full isolated old-vs-new endpoint matrix after provider quota is available.

Research:
- Current issue: `.agents/skills/executing-csharp-scripts-in-unity-editor/SKILL.md` documents MCP-server `AdditionalAssemblyNames` / `UnityCodeMcpServerSettings.asset`, but the agent's script execution settings are actually in `Assets/Plugins/UnityCodeAgent/Editor/UnityCodeAgentSettings.asset` under `AdditionalToolAssemblyNames`. The task wants this corrected and promoted into the primary decision path so missing types are solved by agent settings, not by reflection.
- Distribution path: the package source is `Packages/com.signal-loop.unitycodeagent/Editor/Skills~/executing-csharp-scripts-in-unity-editor/SKILL.md`. The installer copies bundled skills into the configured target, defaulting to `.agents/skills`, so the package source must be updated and the installed copy should be refreshed for local verification.
- Existing project state: `Assets/Plugins/UnityCodeAgent/Editor/UnityCodeAgentSettings.asset` contains `AdditionalToolAssemblyNames: []`, which gives a concrete fixture for an eval where a missing assembly must be added.
- Code research: `UnityCodeAgentSettings.DefaultToolAssemblyNames` includes `System.Core`, `UnityEngine.CoreModule`, `UnityEditor.CoreModule`, `Assembly-CSharp`, and `Assembly-CSharp-Editor`; `UnityCodeAgentSettings.AddToolAssembly(string)` appends additional assemblies and marks settings dirty.
- Skill-creator guidance says improving an existing skill should compare the old version with the edited version, store evals, run baseline and with-skill cases, grade objective assertions, aggregate benchmark results, and generate a review artifact for human inspection.
- External research:
    - Anthropic's agent eval guidance emphasizes starting with small representative eval sets, using real failure modes, and comparing behavior across versions instead of relying only on intuition: https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents
    - OpenAI's eval-skills writeup recommends measuring agent skills with scenario tests that capture pass/fail outcomes, traces, and regressions rather than only final answers: https://developers.openai.com/blog/eval-skills/
    - LangChain's agent evaluation checklist reinforces separating trajectory/trace checks from final-output checks, which matters here because the desired behavior is the path chosen by the agent: settings edit plus reload, not reflection: https://www.langchain.com/blog/agent-evaluation-readiness-checklist
    - Promptfoo's eval/assertion model is useful for a lightweight local harness: prompts plus deterministic assertions can make prompt and skill changes behave like regression tests: https://www.promptfoo.dev/docs/configuration/expected-outputs/
    - Inspect AI's solver model supports fully custom Python execution plans, including multi-turn dialog and agent scaffolds, and its scorer model supports custom trajectory grading. That makes it a better local fit here than a static checker because the harness can drive UnityCodeAgent's `/api/sessions/*`, `/events`, and `/api/tools/results` endpoints directly: https://inspect.aisi.org.uk/solvers.html and https://inspect.aisi.org.uk/scorers.html
    - Promptfoo was considered because its HTTP provider and assertion system can evaluate HTTP-backed flows, but it was not runnable in this workspace because `node.exe` is missing even though `npm` shims are present: https://www.promptfoo.dev/docs/providers/http/

Plan:
- Create an eval workspace as a sibling to the skill directory, for example `.agents/skills/executing-csharp-scripts-in-unity-editor-workspace/`, and snapshot the old skill before editing. Keep reusable eval definitions in a repo-friendly location, such as `.agents/skills/executing-csharp-scripts-in-unity-editor/evals/evals.json`, if the implementation owner wants these tests tracked.
- If a local eval/grading helper script is needed, implement it under `Python/` as a single self-contained script, run it with `uv run`, and declare any third-party framework dependencies with PEP 723 inline metadata. Do not add `pyproject.toml`, `requirements.txt`, lock files, or other Python project configuration for this task.
- Define at least three eval prompts:
    - Missing UI assembly: ask the agent to add a `UnityEngine.UI.Image` to a scene object when the assembly is unavailable. Expected behavior: identify the missing assembly, add the relevant assembly name to `AdditionalToolAssemblyNames`, force settings reload, then use direct `Image` API.
    - Missing 2D physics assembly: ask the agent to add or configure a `UnityEngine.Rigidbody2D` on a scene object when the assembly is unavailable. Expected behavior: add the required 2D physics assembly to agent settings and use normal typed code, not reflection.
    - Normal editor automation control: ask for a scene query or prefab edit that only needs default assemblies. Expected behavior: no settings edit and no extra assembly step.
- For each eval, run old-skill and new-skill agents. The old skill is expected to fail at least one assertion by using reflection or by treating settings as an afterthought; the new skill should pass.
- Grade with objective assertions over saved transcripts and outputs:
    - `uses_settings_first_for_missing_assembly`: passes when the agent edits or instructs editing `UnityCodeAgentSettings.asset` / `AdditionalToolAssemblyNames` before trying direct typed execution again.
    - `does_not_use_reflection_workaround`: passes when the transcript does not use `Type.GetType`, `GetMethod`, `Invoke`, `BindingFlags`, or similar reflection patterns as the solution to missing assemblies.
    - `forces_settings_reload_after_asset_edit`: passes when the reload/import sequence is present after editing settings.
    - `uses_direct_api_after_context_update`: passes when the final intended C# uses typed APIs such as `Image`, `Rigidbody2D`, or direct component access.
    - `does_not_overfit_default_assembly_tasks`: passes when default-assembly tasks do not mutate settings unnecessarily.
- Update both skill copies consistently:
    - Package source: `Packages/com.signal-loop.unitycodeagent/Editor/Skills~/executing-csharp-scripts-in-unity-editor/SKILL.md`
    - Installed copy for this workspace: `.agents/skills/executing-csharp-scripts-in-unity-editor/SKILL.md`
- Skill content changes should be small and targeted:
    - Add a "Missing Assembly Decision Rule" near Core Principles or Usage Workflow.
    - In the workflow, before writing reflection-heavy code for unavailable APIs, check whether the desired type belongs to an unloaded assembly and add that assembly to `AdditionalToolAssemblyNames` in `UnityCodeAgentSettings.asset`.
    - Prefer using `UnityCodeAgentSettings.Instance.AddToolAssembly("AssemblyName")` from an Editor script when available; use file editing only when direct settings-object access is not available.
    - Explicitly state not to use `Assets/Plugins/UnityCodeMcpServer/Editor/UnityCodeMcpServerSettings.asset` for agent script context. That asset belongs to MCP server development and is not the agent's runtime tool assembly setting.
    - Say why: direct typed APIs are safer, clearer, and easier to verify than reflection; reflection should be a last resort for metadata inspection, not the normal fix for missing context.
    - Keep the existing loaded assemblies and reload snippet, but connect them to the main workflow instead of leaving them as a late reference section.
- Generate the skill-review artifact after evals, preferably with the skill-creator `generate_review.py` flow if the necessary scripts are available. If not available in this repo, produce a static markdown report containing prompts, old/new transcript excerpts, assertion results, and pass-rate summary.

Verification:
- Red/green evidence: at least one missing-assembly eval must fail against the old skill and pass against the updated skill.
- Regression evidence: the default-assembly control eval must pass without unnecessary settings edits.
- Trace evidence: saved transcripts must show the new skill choosing `AdditionalToolAssemblyNames` in `UnityCodeAgentSettings.asset` plus settings reload before direct typed API use, and avoiding reflection workaround terms.
- Repository check: verify both skill copies are identical for the edited sections, or document why only the package source was intentionally changed.
- No Unity source code test is required for the planning-only step. Implementation can optionally use a targeted Unity editor script to confirm `UnityCodeAgentSettings.asset` reload mechanics if a live Editor is available.

Planning completion note:
- Planning completed on 2026-06-30 from `02_Started` -> `03_Planning`; ready to move to `04_Ready` for user review.
- Revised on 2026-06-30 after review feedback: use `Image` and `Rigidbody2D` eval cases, and use `UnityCodeAgentSettings.asset` / `AdditionalToolAssemblyNames` rather than MCP server settings.
- Revised on 2026-06-30 after review feedback: Python scripts and frameworks may be used for eval/grading helpers when useful, following this repository's `project-python` conventions.
- No production code or skill content was edited during planning.

Implementation completion note:
- Implemented on 2026-06-30 after user acceptance from `04_Ready` -> `06_InProgress`.
- Snapshotted the old installed skill to `.agents/skills/executing-csharp-scripts-in-unity-editor-workspace/skill-snapshot/`.
- Added eval definitions at `.agents/skills/executing-csharp-scripts-in-unity-editor/evals/evals.json`.
- Added repeatable deterministic grading harness at `Python/grade_execute_csharp_skill_evals.py`.
- Updated both skill copies:
    - `Packages/com.signal-loop.unitycodeagent/Editor/Skills~/executing-csharp-scripts-in-unity-editor/SKILL.md`
    - `.agents/skills/executing-csharp-scripts-in-unity-editor/SKILL.md`
- Skill changes add a Missing Assembly Decision Rule, direct `UnityCodeAgentSettings.Instance.AddToolAssembly(...)` guidance when available, file-edit fallback for `UnityCodeAgentSettings.asset`, reload/import instructions, and explicit warning not to use `UnityCodeMcpServerSettings.asset` / `AdditionalAssemblyNames` for agent script context.
- Static review artifact generated at `.agents/skills/executing-csharp-scripts-in-unity-editor-workspace/iteration-1/skill-review.md` because `generate_review.py` was not present in the available skill-creator installations.

Verification:
- `uv run Python/grade_execute_csharp_skill_evals.py --evals .agents/skills/executing-csharp-scripts-in-unity-editor/evals/evals.json --old-skill .agents/skills/executing-csharp-scripts-in-unity-editor-workspace/skill-snapshot/SKILL.md --new-skill .agents/skills/executing-csharp-scripts-in-unity-editor/SKILL.md --workspace .agents/skills/executing-csharp-scripts-in-unity-editor-workspace` passed and wrote the review report.
- Eval benchmark: new skill 9/9 assertions, old skill 1/9 assertions. The old skill passes only the default-assembly control; both missing-assembly evals fail old and pass new.
- `uv run --with pyyaml C:\Users\tbory\.codex\skills\.system\skill-creator\scripts\quick_validate.py .agents\skills\executing-csharp-scripts-in-unity-editor` passed.
- `uv run --with pyyaml C:\Users\tbory\.codex\skills\.system\skill-creator\scripts\quick_validate.py Packages\com.signal-loop.unitycodeagent\Editor\Skills~\executing-csharp-scripts-in-unity-editor` passed.
- `Compare-Object` over the package and installed `SKILL.md` files returned no differences.

Real endpoint eval replacement note:
- User review rejected static analysis of the skill text. The static `iteration-1` result above is superseded and should not be used as acceptance evidence.
- Researched current eval frameworks for this scenario. Promptfoo has convenient HTTP-provider and assertion support, but this workstation currently has `npm` without `node.exe`, so it was not runnable here. Chose Inspect AI because it is Python-native, runs cleanly with this repo's `uv` convention, supports task/solver/scorer composition, and can drive the UnityCodeAgent HTTP/SSE endpoints directly.
- Replaced `Python/grade_execute_csharp_skill_evals.py` with an Inspect AI endpoint harness. It creates sessions through `/api/sessions/create`, sends prompts through `/api/sessions/send`, listens on `/events`, and answers `ToolInvocationRequest` events through `/api/tools/results`.
- The harness now uses isolated old/new skill roots under `.agents/eval-skill-roots/` so the new-skill run cannot accidentally load the old snapshot from inside `.agents/skills`.
- The harness snapshots and restores `Assets/Plugins/UnityCodeAgent/Editor/UnityCodeAgentSettings.asset` around endpoint runs because the live agent can use built-in file tools during evals.
- The Unity tool simulation is stateful: missing `UnityEngine.UI` / `Rigidbody2D` calls keep returning compile errors until the trajectory actually reloads `UnityCodeAgentSettings.asset`, preventing false success from a blind second retry.
- The scorer grades actual trajectory evidence only: assistant messages, real tool calls, built-in file/edit results, external Unity tool requests, and Unity tool result payloads. Skill-loading context and static skill text are ignored.
- Added `--rescore-existing` so saved endpoint traces can be regraded without issuing new model requests.

Real endpoint verification:
- Valid focused red/green endpoint run: `.agents/skills/executing-csharp-scripts-in-unity-editor-workspace/iteration-6-stateful-ui/skill-review.md`.
- Command used for live run: `uv run Python\grade_execute_csharp_skill_evals.py --iteration iteration-6-stateful-ui --limit 2 --timeout-seconds 150 --max-turns 3`.
- Command used after scorer correction without new model calls: `uv run Python\grade_execute_csharp_skill_evals.py --iteration iteration-6-stateful-ui --limit 2 --rescore-existing`.
- Result: new skill 4/4 assertions on `missing-ui-assembly`; old skill 1/4 assertions on the same endpoint scenario.
- Trace evidence: the new skill loaded from `.agents\eval-skill-roots\executing-csharp-scripts-in-unity-editor\new\...`, called `UnityCodeAgentSettings.Instance.AddToolAssembly("UnityEngine.UI")`, forced `AssetDatabase.ImportAsset(...)`, then continued with direct `using UnityEngine.UI;` / `AddComponent<Image>()` usage. The old skill used the MCP server settings path and failed the settings/reload assertions.
- Regression/control evidence from the earlier saved full endpoint run was rescored at `.agents/skills/executing-csharp-scripts-in-unity-editor-workspace/iteration-2/skill-review.md`, but that run is not acceptance-grade for new-vs-old comparison because the original new-skill configuration loaded from the whole `.agents/skills` tree before isolated roots were added.
- Fresh full-matrix endpoint verification is currently blocked by provider quota. The latest attempted run returned `You have exceeded your monthly quota`, so the task remains in `06_InProgress` pending a quota reset or alternate provider/model for the remaining matrix.
- Local validation after real endpoint harness changes:
    - `uv run --with pyyaml C:\Users\tbory\.codex\skills\.system\skill-creator\scripts\quick_validate.py .agents\skills\executing-csharp-scripts-in-unity-editor` passed.
    - `uv run --with pyyaml C:\Users\tbory\.codex\skills\.system\skill-creator\scripts\quick_validate.py Packages\com.signal-loop.unitycodeagent\Editor\Skills~\executing-csharp-scripts-in-unity-editor` passed.
    - `Compare-Object` over the package and installed `SKILL.md` files returned no differences.
