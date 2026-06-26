# Unity Code Agent

Unity Code Agent is an AI agent for the Unity Editor. With access to Unity Editor API and project codebase it can perform various tasks from creating or modifying scenes, prefabs, ScriptableObjects, assets to implementing game logic by modyfying codebase.
Unity Code Agent is based on Github Copilot SDK and supports both BYOK (Bring Your Own Key) OpenAI-compatible endpoints and GitHub Copilot subscription (free and paid).

## Features

- Delegate tasks to an agent inside the Unity Editor.
- Use any OpenAI-compatible provider or GitHub Copilot subscription.
- Use build-in tools and skills to interact with the Unity Editor, project codebase, and assets.
- Add custom tools, skills and MCP servers to extend the agent's capabilities.

## Requirements

- Unity 6.3 or newer. Older versions might work, but are not officially supported.
- .NET 8 SDK.
- One model provider:
  - GitHub Copilot (free or paid subscription), or
  - An OpenAI-compatible provider.

## Installation

Install from Unity Package Manager:

1. Open `Window > Package Manager`.
2. Select `+ > Add package from git URL...`.
3. Enter:

```text
https://github.com/Signal-Loop/UnityCodeAgent.git?path=Packages/com.signal-loop.unitycodeagent
```

## Quickstart

Unity Code Agent supports two authentication/provider methods: BYOK and Github copilot (free and paid):

### BYOK

1. Open `Tools > UnityCodeAgent > Open Settings`.
2. In `BYOK`, set `BaseUrl` to the full HTTPS provider base URL and enter `ApiKey`. Ex.: `https://openrouter.ai/api/v1` and `sk-...`.
3. In `Model`, click `Refresh`. On first run or after update, Agent Service code is compiled and started in the background. This may take a few seconds.
4. Select a model from the dropdown.
5. Open `Tools > UnityCodeAgent > Open Chat`.
6. Try a prompt such as `List scene objects`.

If no BYOK base URL is set, UnityCodeAgent uses the default GitHub Copilot authentication flow instead.

### GitHub Copilot

1. Sign in to GitHub Copilot on the machine before using the Unity package. Refer to https://github.com/github/copilot-sdk/blob/main/docs/auth/authenticate.md for details.
2. Open `Tools > UnityCodeAgent > Open Settings`.
3. Leave `BYOK > BaseUrl` empty.
4. In `Model`, click `Refresh`. On first run or after update, Agent Service code is compiled and started in the background. This may take a few seconds.
5. Select a model from the dropdown.
6. Open `Tools > UnityCodeAgent > Open Chat`.
7. Try a prompt such as `List scene objects`.

After changing authentication, provider settings, or model settings, use `Tools > UnityCodeAgent > Restart Agent Service` before refreshing models if the old service process is still running.

## Usage

Open the chat window with `Tools > UnityCodeAgent > Open Chat`.

- Enter a prompt and click `Send`, or press `Ctrl+Enter`.
- Click `Sessions` to show previous sessions, then click a session entry to open it.
- Click `Settings` in the chat window, or use `Tools > UnityCodeAgent > Open Settings`, to update provider, model, skills, tools, logging, service, and telemetry settings.
- Use `Tools > UnityCodeAgent > Open MCP Config` to create or edit `.unityCodeAgent/client/mcp.json`.

The agent can execute Unity Editor API actions through tools. Use it only in trusted projects and review generated code, scene changes, asset changes, and configuration changes before committing them.


## Built-In Tools

#### execute_csharp_script_in_unity_editor

Perform any task by executing generated C# scripts in Unity Editor context. Full access to UnityEngine, UnityEditor APIs, and reflection. Automatically captures logs, errors, and return values.

#### read_unity_console_logs

Read Unity Editor Console logs with configurable entry limits (1-1000, default 200)

#### run_unity_tests

Run Unity tests via TestRunnerApi. Supports EditMode, PlayMode, or both. Can run all tests or filter by fully qualified test names.

#### enter_play_mode

Enter Unity Play Mode, pause time and return immediately after triggering the transition. Intended to be used before gameplay automation tools.

#### play_unity_game

Temporarily unpause time, simulate configured Input System actions, collect logs, and pause again when finished.

#### get_unity_game_view_window_screenshot

Capture the current Unity Game View as an image without routing screenshot capture through gameplay input calls.

#### exit_play_mode

Exit Unity Play Mode, unpause time, and return immediately after triggering the transition.

#### get_unity_info

Returns information about the current Unity Editor project and the UnityCodeMcpServer settings.

## Security considerations

Unity Code Agent executes LLM-generated C# code (including reflection code) with the same privileges as the Unity Editor process.  
You are responsible for securing your environment and for any changes or data loss caused by executed scripts.

## License

MIT
