# Ensure logs roll over
- status: Completed
- order: 800
- goal: Add bounded size-based log rotation for Unity and service logs, verified under concurrent read/write expectations without blocking the Unity UI.
  - updated: 2026-06-22
  - steps:
      - [x] Add a shared or parallel size-based rotation helper for active log path, maximum bytes, and retained suffix count.
      - [x] Apply rotation before appending Unity file logs and include an ISO timestamp in Unity file log lines.
      - [x] Apply the same bounded rotation behavior before appending service file logs.
      - [x] Add focused Unity EditMode coverage for Unity logger timestamping and rollover file retention.
      - [x] Add focused `CopilotService.Tests` coverage for service logger rollover and retained file naming.
    ```md
    Research:
    - Unity client logger is `Packages/com.signal-loop.unitycodeagent/Editor/Logging/UnityCodeAgentLogger.cs`.
    - Unity file logs append to `.unityCodeAgent/client/logs/unity.log` using `FileStream(..., FileShare.ReadWrite)` under a static `GlobalFileSync` lock.
    - Unity logger currently formats console and file output identically and has a TODO to include timestamps in file logs.
    - Service logger is `Packages/com.signal-loop.unitycodeagent/Editor/CopilotService~/Settings/UnityCodeCopilotServiceLogger.cs`.
    - Service file logs append to `.unityCodeAgent/service/logs/service.log` using `File.AppendAllText` under an instance lock.
    - Service lines already include `DateTimeOffset.UtcNow:O`; Unity lines do not.
    - Service options currently expose `LogToFile` and `MinLogLevel`, but no rotation settings.
    - Existing service tests live in `CopilotService.Tests`; Unity editor tests for launch/log settings live under `Assets/Tests/Editor/Service`.

    Plan:
    - Keep the public settings surface small for the first pass: use internal constants unless product settings are explicitly requested.
    - Prefer a small log rotation helper that can be used from both logger implementations if assembly boundaries allow it; otherwise duplicate only the minimal algorithm to avoid widening package dependencies.
    - Rotate while holding each logger's existing write lock so rename/delete decisions cannot race with writes from the same process.
    - Use a bounded naming scheme such as `unity.log.1`, `unity.log.2` and `service.log.1`, `service.log.2`; delete the oldest file when retention is exceeded.
    - Check active file size before append; if the next line would exceed the limit, shift retained files and start a new active file.
    - Preserve `FileShare.ReadWrite` for Unity writes so external readers and tools can inspect active logs while Unity is running.
    - Add a test-only constructor or narrowly scoped overload if needed to inject a temporary log path and small rotation threshold without touching global project settings.
    - Add Unity EditMode tests that write enough lines to force multiple rotations, verify retained file count, verify active log remains writable/readable, and verify file lines include timestamps.
    - Add service unit tests that instantiate `UnityCodeCopilotServiceLogger` with temporary `ProjectPaths` and small rotation settings/overload, then assert active and retained files are bounded.

    Verification:
    - Passed: Unity EditMode tests `SignalLoop.UnityCodeAgent.Service.UnityCodeAgentLoggerTests.FileLogging_IncludesTimestampAndRollsOverWithRetention` and `SignalLoop.UnityCodeAgent.Service.UnityCodeAgentLoggerTests.FileLogging_AllowsConcurrentReadWhileWriting`.
    - Passed: `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj --filter UnityCodeCopilotServiceLoggerTests --artifacts-path .artifacts/tests/logs-rollover`.

    Completion:
    - Unity and service loggers now rotate active logs before appending when the next line would exceed the bounded size.
    - Both loggers retain a small suffix chain (`.1`, `.2`, `.3` by default) and preserve read sharing while writing.
    - Unity file logs now include an ISO-8601 timestamp while Unity console output keeps the previous non-timestamped format.
    ```

