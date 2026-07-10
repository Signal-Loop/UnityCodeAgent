# Unity Code Agent

Unity Code Agent is an AI agent for the Unity Editor. With access to Unity Editor API and project codebase it can:
- create or modify scenes, prefabs, ScriptableObjects or assets,
- change cofiguration,
- implement game logic.

Unity Code Agent is based on Github Copilot SDK and supports both BYOK (Bring Your Own Key) OpenAI-compatible endpoints and GitHub Copilot subscription (free and paid).

## Table of Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quickstart](#quickstart)
  - [BYOK](#byok)
  - [GitHub Copilot](#github-copilot)
- [Usage](#usage)
- [Built-In Tools](#built-in-tools)
- [Agent skills](#agent-skills)
  - [Installing skills](#installing-skills)
  - [Included skills](#included-skills)
- [Custom Tools](#custom-tools)
  - [Synchronous tool](#synchronous-tool)
  - [Asynchronous tool](#asynchronous-tool)
- [Adding MCP servers](#adding-mcp-servers)
- [Script execution context](#script-execution-context)
- [Security considerations](#security-considerations)
- [License](#license)

## Features

- Chat with an agent inside the Unity Editor to delegate tasks.
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

**execute_csharp_script_in_unity_editor**: Perform any task by executing generated C# scripts in Unity Editor context. Full access to UnityEngine, UnityEditor APIs, and reflection. Automatically captures logs, errors, and return values.

**read_unity_console_logs**: Read Unity Editor Console logs with configurable entry limits (1-1000, default 200).

**run_unity_tests**: Run Unity tests via TestRunnerApi. Supports EditMode, PlayMode, or both. Can run all tests or filter by fully qualified test names.

**enter_play_mode**: Enter Unity Play Mode, pause time and return immediately after triggering the transition. Intended to be used before gameplay automation tools.

**play_unity_game**: Temporarily unpause time, simulate configured Input System actions, collect logs, and pause again when finished.

**get_unity_game_view_window_screenshot**: Capture the current Unity Game View as an image without routing screenshot capture through gameplay input calls.

**exit_play_mode**: Exit Unity Play Mode, unpause time, and return immediately after triggering the transition.

**get_unity_info**: Returns information about the current Unity Editor project and the UnityCodeAgent settings.

## Agent skills

Unity Code Agent ships a set of **AI agent skill files** (Markdown documents that teach your agent how to use the server's tools effectively). These skills are installed automatically into the configured target directory whenever the package is installed or updated.

### Installing skills

1. Open the server settings: **Tools/UnityCodeAgent/Open Settings**.
2. Scroll to the **Skills** section.
3. Choose the install directory from the dropdown:

- `GitHub` targets `.github/skills/`
- `Claude` targets `.claude/skills/`
- `Agents` targets `.agents/skills/`
- `Custom` shows a folder picker so you can select any directory

4. The inspector shows the currently selected target directory label so you can verify exactly where skills will be copied.
5. Package install and update runs copy the skills automatically.

Only new or changed `.md` files are copied. Files that are already up to date (matching content hash) are skipped.

### Included skills

| Skill                                      | Description                                                                                                                                                                                                                                                                          |
| ------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `executing-csharp-scripts-in-unity-editor` | Teaches the agent when and how to use `execute_csharp_script_in_unity_editor`, `read_unity_console_logs`, and `run_unity_tests` together as a reliable pipeline. Covers forbidden patterns, debugging loops, and common scripting patterns.                                          |
| `unity-game-player`                        | Teaches the agent how to play and test Unity games in a closed loop using `enter_play_mode`, `play_unity_game`, `execute_csharp_script_in_unity_editor`, `read_unity_console_logs`, and `exit_play_mode`. Covers scene discovery, math-based action timing, and adaptive re-sensing. |

## Custom Tools

Add Tools, Prompts, Resources, or Async Tools by implementing the relevant interfaces (ITool, IToolAsync, IPrompt, IResource) anywhere in your codebase. Unity Code Agent will automatically detect and register them.

### Synchronous tool

```csharp
using Newtonsoft.Json.Linq;
using SignalLoop.UnityCodeAgent.Tools.Interfaces;
using SignalLoop.UnityCodeAgent.Tools.Protocol;

public class EchoTool : IToolSync
{
    public string Name => "echo";

    public string Description => "Echoes the input text back to the caller";

    public JToken InputSchema => JsonHelper.ParseElement(@"{
            ""type"": ""object"",
            ""properties"": {
                ""text"": {
                    ""type"": ""string"",
                    ""description"": ""The text to echo""
                }
            },
            ""required"": [""text""]
        }");

    public ToolsCallResult Execute(JToken arguments)
    {
        var text = arguments.GetStringOrDefault("text", "");

        return ToolsCallResult.TextResult($"Echo: {text}");
    }
}
```

### Asynchronous tool

```csharp
using Newtonsoft.Json.Linq;
using SignalLoop.UnityCodeAgent.Tools.Interfaces;
using SignalLoop.UnityCodeAgent.Tools.Protocol;
using System.Threading.Tasks;

public class DelayedEchoTool : IToolAsync
{
    public string Name => "delayed_echo";

    public string Description => "Echoes the input text after a specified delay (demonstrates async tool)";

    public JToken InputSchema => JsonHelper.ParseElement(@"{
            ""type"": ""object"",
            ""properties"": {
                ""text"": {
                    ""type"": ""string"",
                    ""description"": ""The text to echo""
                },
                ""delayMs"": {
                    ""type"": ""integer"",
                    ""description"": ""Delay in milliseconds before echoing"",
                    ""default"": 1000
                }
            },
            ""required"": [""text""]
        }");

    public async Task<ToolsCallResult> ExecuteAsync(JToken arguments)
    {
        var text = arguments.GetStringOrDefault("text", "");
        var delayMs = arguments.GetIntOrDefault("delayMs", 1000);

        await Task.Delay(delayMs);

        return ToolsCallResult.TextResult($"Delayed Echo (after {delayMs}ms): {text}");
    }
}
```

## Adding MCP servers

Use `Tools > UnityCodeAgent > Open MCP Config` to create or edit the project-local MCP config at `.unityCodeAgent/client/mcp.json`. The service loads this file and passes the configured servers to the GitHub Copilot SDK.

Local stdio servers use `command` and `args`. Remote servers use `type` `http` or `sse` with `url`. Optional fields are `env`, `cwd`, `headers`, `tools`, and `timeout`; when `tools` is omitted, all tools are enabled.

```json
{
  "mcpServers": {
    "unity-code-mcp": {
      "type": "stdio",
      "command": "uv",
      "args": [
        "--directory",
        "Assets/Plugins/UnityCodeMcpServer/Editor/STDIO~",
        "run",
        "unity-code-mcp-stdio"
      ],
      "cwd": ".",
      "tools": [
        "*"
      ],
      "timeout": 60
    },
    "remote-tools": {
      "type": "http",
      "url": "https://example.com/mcp",
      "headers": {
        "Authorization": "Bearer token"
      }
    }
  }
}
```

Restart the agent service after changing MCP server configuration so new sessions use the updated server list.

## Script execution context

By default, script execution context includes following assemblies:

- Assembly-CSharp
- Assembly-CSharp-Editor
- System.Core
- UnityEngine.CoreModule
- UnityEditor.CoreModule

Unity Code MCP Server settings (Assets/Plugins/UnityCodeAgent/Editor/Resources/UnityCodeAgentSettings.asset) allow configuring additional assemblies to include in the script execution context. This is useful if your project has assemblies that your generated scripts need to reference.

To add additional assemblies use settings 'Additional Assemblies' section.

## Security considerations

Unity Code Agent executes LLM-generated C# code, including reflection code, with the same privileges as the Unity Editor process. Use it at your own risk. To the fullest extent permitted by law, Signal Loop disclaims liability for any changes, damage, or data loss resulting from its use. You are responsible for securing your environment.

## License

MIT
