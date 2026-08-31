# Improve service lifecycle management and session recovery
- status: Completed
- order: 1300
- goal: Recover active sessions across local service restarts, verified by restart/reopen behavior without surfacing stale unavailable-session errors.
  - steps:
      - [x] Research current behavior when the service is stopped during an active session and a new prompt is sent.
      - [x] Create plan for improving session management to handle service restarts gracefully, including automatic service restart and session recovery.
      - [x] Plan is accepted by user.
      - [x] Implement improvements to session management and service lifecycle handling based on the plan.
    ```md
    Current behavior:
    When the service is stopped during a session, an error is thrown and displayed.
    When the next prompt is sent, an error indicating the session is unavailable is thrown and displayed.

    Expected behaviour:
    Unity client manages gracefully service lifecycle and restarts the service, short messages about it can be displayed in chat, reopens the current session, then continues.
    ```

