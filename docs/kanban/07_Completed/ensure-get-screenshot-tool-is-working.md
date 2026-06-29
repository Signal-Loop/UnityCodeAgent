# Ensure Unity screenshot tool works in agent chat

- goal: Fix the Unity Game View screenshot tool so an agent chat can use the captured screenshot, verified by focused tests, while preserving existing image binary result behavior for SDK versions that support it.
- updated: 2026-06-29
- steps:
    - [x] Persist captured screenshots at a workspace-readable path and return that path in tool text output
    - [x] Add screenshot artifact retention: max age 3 days, max count 50 screenshots, max total size 100 MB
    - [x] Keep the binary image content result attached for SDK compatibility
    - [x] Add focused coverage for screenshot tool output shape and/or binary tool bridging
    - [x] Run the focused Unity/service verification that covers the touched surface

Original note:
get unity screenshot tool is not working in agent chat. find root cause and fix

Research:
- The Unity tool is `Packages/com.signal-loop.unitycodeagent/Editor/Tools/CustomTools/GetUnityGameViewWindowScreenshotTool.cs`.
- The Unity registry converts image content into `AgentToolBinaryResultDto` with type `image`; the service bridge converts that into `GitHub.Copilot.ToolBinaryResult` with `ToolBinaryResultType("image")`.
- Local SDK source at `C:\Users\tbory\source\Workspaces\copilot-sdk\dotnet` confirms the .NET SDK exposes `ToolResultObject.BinaryResultsForLlm`, but its own `ToolsE2ETests.Can_Return_Binary_Result` is skipped with the note that binary results behave as if no content was returned.
- The installed SDK package is `GitHub.Copilot.SDK` 1.0.4. The nearby source checkout is detached at tag `v1.0.0`; neither source path shows a working .NET binary-image E2E.
- The current Unity screenshot tool returns only image content. Because the SDK/runtime path does not reliably surface image-only custom tool results to the model, the agent can receive an effectively empty result even if Unity captured the PNG successfully.

Plan:
- Update the screenshot tool to save the final PNG under a stable project-local artifact folder, such as `.unityCodeAgent/screenshots/`, instead of only using a temporary file that is deleted after capture.
- Manage `.unityCodeAgent/screenshots/` as a bounded cache: prune screenshots older than 3 days, keep at most 50 screenshots, and keep total retained screenshot bytes at or below 100 MB. Run cleanup opportunistically during screenshot capture and never delete the artifact returned by the current tool call.
- Return mixed tool content: a short text result with the saved screenshot path and a note that the PNG is attached, plus the existing image content. This keeps future SDK binary support intact while giving current agents a path they can inspect with filesystem/image tooling.
- Keep cleanup scoped: delete only transient capture files; retain the final screenshot artifact because the returned text path depends on it.
- Add focused Unity EditMode coverage using the tool's injectable screenshot request/path hooks to write a known PNG, then assert the result includes non-empty text, non-empty image data, MIME type `image/png`, and a readable saved artifact path. Add retention coverage proving old/excess screenshots are pruned while the current returned artifact remains.
- If the constructor needs an additional injectable artifact path for testing, add it as an overload or optional internal hook without changing the default public tool name/schema.

Verification:
- Run the focused Unity EditMode test for the screenshot tool if Unity test execution is available.
- If service bridge behavior is touched, run `dotnet test CopilotService.Tests\UnityCodeCopilot.Service.Tests.csproj --artifacts-path .artifacts\copilot-service-tests -p:UseAppHost=false` after ensuring `.artifacts\contracts\...` contains the contract specs required by this repository.

Completion notes:
- Implemented screenshot artifact persistence under `.unityCodeAgent/screenshots/` with age, count, and total-size pruning while preserving the current returned artifact.
- The tool now returns text content containing the saved screenshot path plus the existing PNG image content.
- Added focused EditMode coverage in `Assets/Tests/Editor/Service/ScreenshotToolArtifactTests.cs`.
- Test root cause: direct references to `GetUnityGameViewWindowScreenshotTool` resolved to the separate UnityCodeMcpServer tool type. The tests now resolve the UnityCodeAgent tool type from `UnityAgentToolRegistry`'s assembly explicitly.
- Verification passed: `run_unity_tests` EditMode for `SignalLoop.UnityCodeAgent.Service.ScreenshotToolArtifactResultTests.CaptureResult_IncludesSavedPathTextAndImageContent` and `SignalLoop.UnityCodeAgent.Service.ScreenshotToolArtifactResultTests.CaptureResult_PrunesOldAndExcessArtifactsWithoutDeletingCurrentArtifact` passed 2/2.
