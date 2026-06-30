# Fix Copilot .NET session list working-directory filter

- goal: Create a GitHub Copilot SDK PR that makes the .NET public session-list filter match the Node SDK behavior, verified by focused SDK tests for `CopilotClient.ListSessionsAsync(new SessionListFilter { WorkingDirectory = ... })` sending a wire `cwd` filter and returning only matching sessions.
- updated: 2026-06-30
- steps:
    - [ ] Confirm current .NET public `ListSessionsAsync` request serialization
    - [ ] Confirm there is no existing PR with thi fix
    - [ ] Align .NET public filter mapping with Node SDK `workingDirectory -> cwd`
    - [ ] Add focused .NET SDK tests for public `ListSessionsAsync` working-directory filtering
    - [ ] Open PR against the Copilot SDK repository

Context:
- UnityCodeAgent manual verification showed `/api/sessions` did not filter to Unity project sessions when the service passed `new SessionListFilter { WorkingDirectory = projectRoot }` into the .NET SDK public `CopilotClient.ListSessionsAsync`.
- In `C:\Users\tbory\source\Workspaces\copilot-sdk\nodejs\src\client.ts`, Node explicitly maps public `workingDirectory` to the wire filter field `cwd` before calling `session.list`.
- In `C:\Users\tbory\source\Workspaces\copilot-sdk\dotnet\src\Client.cs`, .NET public `ListSessionsAsync` currently passes `new ListSessionsRequest(filter)` directly to `session.list`.
- In `C:\Users\tbory\source\Workspaces\copilot-sdk\dotnet\src\Types.cs`, public `.NET SessionListFilter.WorkingDirectory` has no `[JsonPropertyName("cwd")]`, so with the SDK's web JSON options it serializes as `workingDirectory`, not `cwd`.
- The generated RPC filter in `C:\Users\tbory\source\Workspaces\copilot-sdk\dotnet\src\Generated\Rpc.cs` uses `Cwd` with `[JsonPropertyName("cwd")]`, and existing .NET E2E coverage verifies generated `client.Rpc.Sessions.ListAsync(... new RpcSessionListFilter { Cwd = ... })`, not the public `client.ListSessionsAsync(new SessionListFilter { WorkingDirectory = ... })` path.

Expected PR shape:
- Preserve the public .NET API property name `WorkingDirectory`.
- Add an explicit mapping or serialization attribute so public `WorkingDirectory` is sent to the runtime as `cwd`, matching Node SDK behavior.
- Add/adjust tests so public .NET `ListSessionsAsync` working-directory filtering fails before the fix and passes after it.
- Keep behavior for `GitRoot`, `Repository`, and `Branch` unchanged.
