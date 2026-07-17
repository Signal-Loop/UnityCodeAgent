# Upstream native image results from custom tools
- status: Backlog
- order: 800
- goal: Create an upstream pull request that makes binary images returned by custom tools visible to vision-capable models through the native Copilot tool-result flow, proven by an enabled cross-provider regression test, while preserving text/resource tool results and avoiding application-level steering workarounds.
- updated: 2026-07-09
- steps:
    - [ ] Reproduce the missing image behavior against the latest Copilot SDK and CLI
    - [ ] Trace custom tool image results from SDK serialization into provider request construction
    - [ ] Confirm the owning upstream repository and agree the intended provider-neutral behavior
    - [ ] Implement the smallest native tool-result conversion fix
    - [ ] Enable or add an E2E test proving the model receives the returned image
    - [ ] Verify OpenAI-compatible and Anthropic-compatible message construction
    - [ ] Run focused upstream tests and formatting checks
    - [ ] Prepare and submit an upstream pull request with reproduction evidence and compatibility notes

## Missing feature

The .NET SDK accepts `ToolResultAIContent` with `BinaryResultsForLlm`, and the runtime reports the image in `tool.execution_complete`, but the image is not included in the model-visible continuation of the tool call. The model therefore behaves as if the custom tool returned no image and may hallucinate visual content.

The official `.NET` test `ToolsE2ETests.Can_Return_Binary_Result` remains skipped in SDK 1.0.5, SDK 1.0.6, and current `main` with the reason that binary results are not fully implemented. This task addresses that missing upstream feature rather than UnityCodeAgent's temporary screenshot steering workaround.

## Intended behavior

```text
model tool call
    -> application returns text plus image/png binary result
    -> Copilot runtime preserves the tool-call relationship
    -> provider request contains a model-visible image content block
    -> vision-capable model answers using the returned pixels
```

The implementation must:

- keep image data inside the native tool-result continuation;
- map image results correctly for each supported provider wire format;
- preserve tool call IDs and ordering constraints;
- retain existing text, failure, and non-image resource behavior;
- reject or clearly report unsupported models and media types;
- avoid logging raw base64 payloads.

## Upstream scope

- Primary investigation target: `github/copilot-cli`, where SDK tool results are converted into model-provider messages.
- SDK integration/test target: `github/copilot-sdk`, where the skipped binary-result E2E test should become enabled and passing.
- If the fix requires coordinated changes in both repositories, document the dependency and submit the minimal ordered PR set rather than embedding a fork in UnityCodeAgent.
- Keep UnityCodeAgent production changes outside this task except for a temporary reproduction harness if required.

## Acceptance evidence

- A minimal tool returning a known PNG is described correctly by a vision-capable model.
- The previously skipped binary-result scenario is enabled and passes reliably.
- Focused tests cover at least OpenAI-compatible and Anthropic-compatible request shapes.
- Existing text-only and error tool-result tests pass.
- The upstream PR explains the defect, provider mappings, compatibility risk, and verification performed.

## Related tasks

- Local workaround: `docs/kanban/04_Ready/fix-screenshot-tool-image-analysis.md`
- Release monitoring: `docs/kanban/01_Backlog/check-copilot-sdk-image-tool-results.md`
