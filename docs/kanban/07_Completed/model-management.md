# Model management

- goal: Automatically apply provider/model changes to the active chat session, verified by restart behavior and footer model label updates.
  - steps:
      - [ ] Verify current behavior when provider/model is changed during an active session.
      - [ ] Implement automatic session restart and provider/model switch when the provider/model is changed.
      - [ ] Add label with current provider/model in the chat window footer that updates when provider/model is changed.
    ```md
    Currently after provider is changed, the session needs to be restarted to work with the new provider.

    Expected behaviour:
    When provider/model is changed, the current session is restarted automatically and the new provider/model is used for subsequent prompts without requiring manual restart.

    Footer label detail:
    When label is clicked, Settings asset is pinged and opened in inspector.
    ```

