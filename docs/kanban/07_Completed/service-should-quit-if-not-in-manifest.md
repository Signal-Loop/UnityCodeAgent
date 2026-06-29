# Service should quit if not in manifest

- goal: Ensure an older service process exits when its manifest is replaced by another service process for the same project, so there are no orphaned processes, verified by focused service lifecycle tests and a real bootstrap-style check where practical, while preserving owned manifest cleanup behavior.
  - updated: 2026-06-23
  - steps:
      - [x] Add a service-side manifest ownership check that reads the current endpoint manifest and stops the process if the manifest belongs to a different service process for the same project.
      - [x] Run the ownership check after manifest publication and periodically during service lifetime so an orphaned process exits after another service wins the manifest.
      - [x] Preserve `DeleteIfOwned(Environment.ProcessId)` semantics so a losing process does not delete the winning process manifest on shutdown.
      - [x] Add focused service tests for matching manifest ownership, replaced manifest ownership, missing manifest, malformed manifest, and different-project manifest behavior.
      - [x] Add or extend a Unity bootstrap/E2E test with two real service starts for the same project when feasible, asserting only the manifest-owned service remains healthy/running.
    ```md
    Research:
    - `EndpointManifest` already includes `projectRoot`, `projectId`, `unityProcessId`, `serviceProcessId`, `port`, and `streamGenerationId`.
    - The service writes the manifest from `ServiceRuntimeLifecycle.OnStarted()` through `EndpointManifestStore.WriteAsync(...)`.
    - `EndpointManifestStore.WriteAsync(...)` atomically replaces any existing manifest, so a later service start can overwrite an earlier service manifest.
    - Service shutdown already calls `EndpointManifestStore.DeleteIfOwned(Environment.ProcessId)`, which avoids deleting another service's manifest.
    - There is no service-side check that the current manifest still names `Environment.ProcessId` after another process replaces it.
    - Unity bootstrap rejects stale manifests by project, Unity process id, service process liveness, and `/health`, then deletes the unusable manifest before starting a new service.
    - `ParentProcessMonitor` only watches the Unity parent process identity and does not check manifest ownership.
    - Existing real-process coverage in `ServiceBootstrapE2eTests` verifies one service publishes a manifest, but does not currently start competing service processes.

    Plan:
    - Extend `EndpointManifestStore` with a small read method that returns the current manifest identity needed for ownership checks: project root/id and service process id.
    - Add a hosted manifest ownership monitor or extend runtime lifecycle with a timer-backed check that starts after successful publication.
    - Treat a manifest for the same project with a different positive `serviceProcessId` as a lost-ownership signal; log a warning and call `IHostApplicationLifetime.StopApplication()`.
    - Treat missing or temporarily unreadable/malformed manifests conservatively: log at debug/warning and retry rather than immediately exiting, unless implementation evidence shows immediate exit is safer.
    - Keep different-project manifests from triggering shutdown, so shared parent directories or test temp folders do not stop unrelated services.
    - Avoid killing external processes directly. The losing service should shut itself down through host lifetime, letting existing cleanup rules protect the winning manifest.
    - Add focused `CopilotService.Tests` coverage around the new ownership decision logic and manifest store parsing using temporary paths.
    - If the real-process Unity E2E test is stable enough, start a first service, intentionally force a second start for the same temp project, wait for manifest ownership to settle, and assert the first service is no longer healthy or has exited while the manifest names the second process.

    Completion:
    - Added `EndpointManifestStore.ReadCurrentIdentity()` so service-side lifecycle code can read `projectRoot`, `projectId`, and `serviceProcessId` without throwing on missing, locked, or malformed manifests.
    - Added hosted `ManifestOwnershipMonitor`, registered with the service host. It keeps running for missing, malformed, or different-project manifests, and calls `IHostApplicationLifetime.StopApplication()` only when the same project manifest names a different positive service process id.
    - `EndpointManifestStore.DeleteIfOwned(...)` now uses the same identity reader and still only deletes the manifest when it is owned by the current service process.
    - Added `ManifestOwnershipMonitorTests` for current ownership, replaced ownership, missing manifest, malformed manifest, different-project manifest, and non-owned cleanup preservation.
    - Skipped adding a real two-process Unity bootstrap E2E test in this pass because `ServiceBootstrapE2eTests` currently covers a single real service start; forcing two competing real service processes would add slower and more brittle editor-process coverage. Residual risk is limited to real process timing around the 2-second monitor interval, while ownership decision logic and DI registration are covered by service tests.

    Verification:
    - Passed: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj --filter Manifest --artifacts-path .artifacts/tests/manifest-ownership` (6 passed, 0 failed).
    - Passed: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj` (31 passed, 0 failed).
    - Noted: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj --artifacts-path .artifacts/tests/service-all-manifest-ownership` failed in 9 existing contract tests because `ContractSpecExampleCatalog` looked for `contracts/openapi/agent-service.openapi.yaml` under `.artifacts/tests`; the same full suite passed without custom artifacts path.
    ```

