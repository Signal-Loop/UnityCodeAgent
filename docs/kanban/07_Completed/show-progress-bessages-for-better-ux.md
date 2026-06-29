# Show progress bessages for better UX

- goal: Surface startup and recovery progress in chat via progress handlers, verified by E2E tests and targeted Unity editor checks.
  - steps:
      - [ ] Use ShowProgressMessageHandler from ChatEditorWindow.
      - [ ] Pass it as method parameter to service method that need to report progress.
      - [ ] Replace selected persisted service messages with progress messages.
      - [ ] Add useful startup and long-operation progress messages.
      - [ ] When opening chat window, disable buttons instead of the full window.
    ```md
    Use progress messages instead of:
    - `PublishServiceEvent(onEvent, lastSessionId, "Agent service connection restored.");`
    - `"Agent service event stream ended. Restarting service connection."`

    Add messages like Starting agent service, Agent service started and other messages that can be useful for user to understand what is going on, especially when there are long operations or opening chat window.

    Plan thoroughly how to implement those changes, follow best architecture practices and clean code and isolation practices.
    Follow KISS and YAGNI principles, do not overengineer, keep it simple and maintainable.
    Implement tests for new features, use E2E tests to verify features, if proper messages are displayed in the right way.
    Use `execute_csharp...` tool for verification, use mocking service for tests and verification.
    ```

