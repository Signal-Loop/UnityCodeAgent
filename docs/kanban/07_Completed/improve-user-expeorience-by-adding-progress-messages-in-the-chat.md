# Improve user expeorience by adding progress messages in the chat

- goal: Implement ephemeral progress chat messages that replace or clear correctly, verified by UI E2E coverage and focused helper tests.
  - steps:
      - [ ] Add Progress to AgentEventType.
      - [ ] Use Progress to report ephemeral status and loading messages.
      - [ ] Replace or remove trailing progress messages correctly.
      - [ ] Create new template for progress message based on tool message.
      - [ ] Show progress message instead of Service message in stream recovery paths.
      - [ ] Create delayed waiting-response progress helper.
      - [ ] Implement focused tests and E2E verification.
    ```md
    When showing UI, progress messages are appended in ChatEditorWindow as follows:
    - existing messages are checked if last message is progress message
    - if it is, it is replaced with the new message, if not, the new message is added to the end of the list
    - if next message is added, and progress message is last, it is removed, so that progress messages are not mixed with regular messages and are always up to date

    Show progress message instead of Service message in PublishStreamRecoveryEvents in AgentsService.
    Add Progress message when service start is finished in StreamEventsAsync in AgentsService.

    Create progress message feature that spawns progress messages when waiting for response from service:
    - after 1 second after last message is displayed start showing `Thinking...`, `Analyzing...`, `Waiting for response...` random messages
    - update them after 1 second
    - create separate helper for this feature and use it in ChatEditorWindow, which should manage this

    Use single responsibility - create separate helpers for separate features.
    Keep it small and simple, do not overengineer, do not create complex system for progress messages, keep it simple and easy to maintain.

    Current problem:
    There is no user feedback for long running operations, like starting/restarting service, waiting for response, etc.

    Expected behaviour:
    User sees status messages when long operation is started or there are no messages for longer time. ChatShowAgentEventUpdate is used to publish these messages.
    ```

