# Add skills/mcp to settings UI
- status: Completed
- order: 200
- goal: Expose skill folders, skill toggles, and MCP config access in settings UI, verified by settings behavior and file-opening interactions.
  - steps:
      - [x] Add Settings button on the right of sessions button in chat window UI.
      - [x] Add configurable skill folders with `.agents/skills` as default.
      - [x] List skill names from configured folders with include/exclude toggles.
      - [x] Add MCP configuration path to settings and open it in a text editor when clicked.
    ```md
    Settings button is always visible.
    When clicked, it pings settings asset and opens it in inspector.

    Skills folder paths are project relative.
    Skills from configured folders are used as context for the session.
    Reference: https://github.com/github/copilot-sdk/blob/main/docs/features/skills.md

    Skill names from the configured folders are listed in the settings.
    They have on/off toggle to include/exclude them from the context, and when clicked, skill file is opened in a text editor.
    MCP path is not editable.

    Expected behaviour:
    Settings contain Skills, MCP, user can add/remove skill folders, toggle skills, and open mcp configuration file.
    ```

