# CreateRequestSignature and CreateSignature code is duplicated in SessionRequestFactory and CopilotSessionManager

- goal: Centralize the deterministic session request signature in shared contracts so Unity-side session request tracking and service-side attached-session reuse use one implementation, verified by focused Unity/service tests, without changing request JSON shape or provider signature semantics.
  - updated: 2026-06-22
  - steps:
      - [x] Add a shared session request signature helper to `Packages/com.signal-loop.unitycodeagent/Editor/Contracts/ServiceContracts.cs`.
      - [x] Replace `SessionRequestFactory.SessionRequestOptions.CreateSignature` with the shared helper.
      - [x] Replace `CopilotSessionManager.CreateRequestSignature` and its local append helpers with the shared helper.
      - [x] Verify whether `ProviderConfigDto.CreateSignature` is still the right provider-level helper; keep it if provider signatures remain a distinct concept.
      - [x] Add or update focused tests for deterministic ordering and service-side reconfiguration behavior.
    ```md
    Research:
    - The named task started in Backlog and was explicitly requested for preparation.
    - `SessionRequestFactory.SessionRequestOptions` computed `Signature` by appending provider signature, working directory, sorted skill directories, sorted disabled skills, and sorted tool identity (`Name`, `Description`, `InputSchemaJson`).
    - `CopilotSessionManager` duplicated the same request signature algorithm to decide whether an already attached runtime session could be reused or must be reopened.
    - `Packages/com.signal-loop.unitycodeagent/Editor/Contracts/ServiceContracts.cs` is compiled into the Unity editor assembly and linked into `UnityCodeCopilot.Service.csproj`, so it is a viable shared location.
    - `ProviderConfigDto.Signature` and `ProviderConfigDto.CreateSignature` are provider-level signatures that hash the API key and normalize base URL/type/wire API. They are still used by the shared request signature algorithm and should remain as the provider-scoped primitive.

    Completion:
    - Added `AgentSessionRequestSignature` to shared contracts with the existing length-prefixed field encoding and normalization rules.
    - `SessionRequestFactory.SessionRequestOptions` now calls the shared helper for Unity-side active-session request tracking.
    - `CopilotSessionManager` now calls the shared helper for service-side attached-session reuse/reconfiguration checks, and its duplicated private request signature helpers were removed.
    - Added `AgentSessionRequestSignatureTests` covering deterministic ordering/whitespace normalization and changes to provider, working directory, disabled skills, and tool schema.

    Verification:
    - Passed: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj --filter "CopilotSessionManagerTests|AgentSessionRequestSignatureTests" --artifacts-path .artifacts/tests/shared-session-signature` (6 passed, 0 failed).
    - Passed: Unity EditMode tests `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests.SkillChange_ReconfiguresActiveSessionBeforeNextPrompt`, `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests.ModelChange_ReconfiguresActiveSessionBeforeNextPromptAndUpdatesLabel`, and `SignalLoop.UnityCodeAgent.Service.ChatEditorWindowClientE2eTests.LiveDebugChange_DoesNotReconfigureActiveSessionBeforeNextPrompt` (3 passed, 0 failed).
    ```

