ToolExecutionStartEvent
```
{
  "type": "tool.execution_start",
  "data": {
    "arguments": {
      "intent": "Getting Unity info"
    },
    "toolCallId": "call_zUOtJ0O1CAraD0EEmfX5RswG",
    "toolName": "report_intent",
    "turnId": "0"
  },
  "id": "719bbdda-9694-4f60-a7a6-958d31d785a0",
  "parentId": "775070ef-a6d1-42fe-b86f-44dbfaef88d0",
  "timestamp": "2026-06-01T12:58:39.056+02:00"
}
```
"Getting Unity info"
- check if type is 'tool.execution_start' and toolName is 'report_intent', and if so, use intent field.

ToolExecutionStartEvent
```
{
  "type": "tool.execution_start",
  "data": {
    "arguments": {},
    "mcpServerName": "unity-code-mcp-stdio-over-http",
    "mcpToolName": "get_unity_info",
    "toolCallId": "call_49G0XnEokNlXRQe7qCExEeco",
    "toolName": "unity-code-mcp-stdio-over-http-get_unity_info",
    "turnId": "0"
  },
  "id": "0d3626c6-05be-4558-86c1-fc7854508559",
  "parentId": "719bbdda-9694-4f60-a7a6-958d31d785a0",
  "timestamp": "2026-06-01T12:58:39.057+02:00"
}
```
"Calling 'get_unity_info' tool"
- check if type is 'tool.execution_start' and mcpToolName exists, and if so, use mcpToolName field.

ToolExecutionCompleteEvent
```
{
  "type": "tool.execution_complete",
  "data": {
    "interactionId": "1f3ab7a0-8ed8-4f8f-bd06-5b582431a720",
    "model": "gpt-5-mini",
    "result": {
      "content": "Intent logged",
      "detailedContent": "Getting Unity info"
    },
    "success": true,
    "toolCallId": "call_zUOtJ0O1CAraD0EEmfX5RswG",
    "toolTelemetry": {},
    "turnId": "0"
  },
  "id": "dc61c965-7ab0-428a-aae0-db137ac3f145",
  "parentId": "0398ef41-6e66-461d-9d58-3464c45849eb",
  "timestamp": "2026-06-01T12:58:39.059+02:00"
}
```
"Completed 'Getting Unity info'"
- check if type is 'tool.execution_complete' and detailedContent exists, and if so, use detailedContent field.

ToolExecutionStartEvent
```
{
  "type": "tool.execution_start",
  "data": {
    "arguments": {
      "description": "Get Unity Editor info",
      "prompt": "Environment:
- Repo root: <repo-root>
- Running as GitHub Copilot CLI user request: fetch Unity Editor info via Unity MCP tool.

Goal:
- Use the Unity MCP tool unity-code-mcp-stdio-over-http-get_unity_info to obtain the Unity project_path and settings (the tool returns {project_path, settings}).

Instructions for the agent:
1) Prefer calling the unity-code-mcp-stdio-over-http-get_unity_info tool to fetch the info. Capture its full JSON response.
2) If the MCP tool is unavailable or fails, attempt to read these fallback files (paths relative to repo root):
   - .unityCodeAgent/service/runtime/endpoint.json
   - contracts/openapi/agent-service.openapi.yaml
   - contracts/asyncapi/agent-service-events.asyncapi.yaml
   - Packages/com.signal-loop.unitycodeagent/Editor/Contracts/ServiceContracts.cs
   Return any found contents as part of the result.
3) Return a JSON object with these fields: success (bool), method ("mcp" or "fallback"), project_path (string or null), settings (object or raw string if returned), raw_response (if any), errors (array of messages).
4) If sensitive information appears (tokens, keys), redact values but note which fields were redacted.
5) Keep output minimal and machine-readable (JSON). If any errors occur, include stack traces or tool error text in errors.

Output: Produce only the JSON object as the final agent response.
",
      "agent_type": "task",
      "name": "get-unity-info",
      "mode": "sync"
    },
    "toolCallId": "call_bPoNyQWhbLWoZJiy76vxiuBG",
    "toolName": "task",
    "turnId": "0"
  },
  "id": "9fac9d7d-ec16-45e8-b40e-fc9ab110ee13",
  "parentId": "e34185a9-ed4d-4cc1-b7ae-5155b5a0f135",
  "timestamp": "2026-06-01T13:04:41.55+02:00"
}
```
"Calling task 'Get Unity Editor info'"
- check if type is 'tool.execution_start' and agent_type is 'task', and if so, use description field.