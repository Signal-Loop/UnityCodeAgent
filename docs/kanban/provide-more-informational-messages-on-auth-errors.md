# Provide more informational messages on auth errors
- status: Ready
- order: 100
- goal: Show provider-specific authentication failure guidance for BYOK and GitHub Copilot paths in settings, chat errors, and chat progress messages, verified by focused service/client/UI tests, without exposing API keys or changing successful model/session behavior.
- updated: 2026-06-24
- steps:
    - [ ] Add provider-aware auth status/error handling in the service auth paths.
    - [ ] Return BYOK-specific guidance when custom provider auth fails.
    - [ ] Return GitHub Copilot-specific guidance when default Copilot auth fails.
    - [ ] Surface auth failures in the chat window as progress messages as well as normal errors.
    - [ ] Preserve raw exception details in service logs while keeping user-facing messages safe.
    - [ ] Add focused tests for BYOK and GitHub Copilot auth error responses.

Original request:
- Provide more informational messages on auth errors.
- Different messages when BYOK is enabled and failed and when Copilot is used and failed.

Research:
- `UnityCodeAgentSettings.TryCreateProviderConfig` treats a non-empty `ByokBaseUrl` as BYOK; otherwise the service uses the default GitHub Copilot authentication flow.
- Model refresh calls `AgentService.GetModelsAsync`, which posts `/api/models` with `ListAgentModelsRequestDto.Provider`; the settings inspector displays the thrown exception message directly in a help box.
- `HttpAgentServiceApiClient.EnsureSuccessAsync` already preserves `AgentServiceErrorResponse.Message`, so improving service responses will flow through to Unity without a UI rewrite.
- `ServiceEndpoints.CreateErrorResult` currently returns the innermost exception message for every expected request failure, with generic `operation_failed` for non-session failures.
- BYOK model listing uses `ByokOpenAiProvider.ListModelsAsync`, which calls `{BaseUrl}/models` with the configured bearer token and currently throws the raw provider body or reason phrase on non-success.
- Default GitHub Copilot model/session operations go through `CopilotClientHost` and the GitHub Copilot SDK. The SDK exposes `GetAuthStatusAsync`, and invalid auth in SDK tests can surface as exception text containing `401 Unauthorized`.
- Copilot SDK source analysis:
  - `CopilotClient.GetAuthStatusAsync` calls `auth.getStatus` and returns `GetAuthStatusResponse` with `IsAuthenticated`, `AuthType`, `Host`, `Login`, and `StatusMessage`.
  - SDK `ClientE2ETests.Should_List_Models_When_Authenticated` checks `GetAuthStatusAsync` before `ListModelsAsync`; the test skips model listing when `IsAuthenticated` is false, confirming `models.list` is auth-gated.
  - SDK `CopilotClient.ListModelsAsync` calls `models.list` only for the default Copilot path. BYOK model listing uses `OnListModels`, so BYOK does not benefit from `auth.getStatus`.
  - SDK `CreateSessionAsync` and `ResumeSessionAsync` call `session.create` / `session.resume` and pass `GitHubToken` through session config when supplied. UnityCodeAgent currently does not supply a GitHub token, so the default path depends on the logged-in user / gh CLI auth.
  - SDK startup passes explicit `GitHubToken` through `COPILOT_SDK_AUTH_TOKEN` and adds `--no-auto-login` when `UseLoggedInUser` is false. UnityCodeAgent does not set either option, so the reliable default guidance is to refresh GitHub/gh CLI login rather than edit Unity BYOK settings.
  - SDK JSON-RPC errors become internal `RemoteRpcException` instances, then `CopilotClient.InvokeRpcAsync` wraps them as `IOException` with message `Communication error with Copilot CLI: {remote message}`. The internal exception has public `ErrorCode` and `ErrorData`, but because the type is internal, production code should prefer explicit `GetAuthStatusAsync` checks over reflection-based classification.
  - SDK `PerSessionAuthE2ETests.ShouldFailWithInvalidToken` asserts invalid auth can surface as `401 Unauthorized`, so a fallback classifier should still recognize `401`, `403`, `Unauthorized`, `Forbidden`, and authentication wording in SDK exception messages.
- Service endpoint contract tests can fake `IAgentModelCatalog` failures in-process; Unity HTTP client tests already prove contract-shaped error messages surface unchanged.
- Chat progress source analysis:
  - `AgentService` accepts a `showProgressMessage` callback and already emits operation progress such as `Loading agent models...`, `Creating chat session...`, `Sending prompt...`, and service reconnect messages.
  - `ChatEditorWindowClient` constructs `AgentService(ShowProgressMessage)` and maps that callback to `ChatShowProgressMessageUpdate`.
  - `ChatEditorWindow` maps `ChatShowProgressMessageUpdate` to `ChatProgressMessages.ShowProgressMessage`, which renders a transient progress row using `ChatMessageTemplateProgress.uxml`.
  - `ChatShowErrorUpdate` currently appends a persistent error bubble. Submit/open/init/session-refresh failure paths enqueue error updates, but they do not explicitly enqueue a progress update containing the final user-facing failure text.
  - `ChatProgressMessages` removes trailing progress when visible transcript messages arrive or when busy state changes to false, so an auth-failure progress update must be ordered deliberately or handled by a dedicated helper to avoid being immediately removed.

Plan:
- Prefer explicit checks over broad string classification:
  - Add a small `CopilotClientHost.GetAuthStatusAsync` wrapper around the SDK `GetAuthStatusAsync`.
  - For default Copilot model listing, check auth status before `ListRuntimeModelsAsync`; if unauthenticated, throw a purpose-built user-facing exception/message.
  - For default Copilot create/open session paths, check auth status before `CreateSessionAsync` / `ResumeSessionAsync` so chat/session failures get the same clear guidance.
  - Do not call SDK auth status for BYOK model listing, because BYOK uses `OnListModels` and provider auth is independent from GitHub auth.
- Add a very small provider-aware fallback formatter near `ServiceEndpoints` or a focused API helper for failures that occur after preflight. Inputs should include the caught exception, endpoint operation context, and whether the request provider has BYOK enabled.
- Keep `AgentServiceErrorResponse` shape stable unless a concrete need appears. Prefer clearer `Message` text and existing `operation_failed` code to avoid contract churn.
- For BYOK model refresh, use the actual HTTP status code in `ByokOpenAiProvider.EnsureProviderResponseSuccess`: return BYOK auth guidance for 401/403 and leave non-auth provider failures specific to the raw status/body. Do not include the API key or full headers in any user-facing message.
- For BYOK session failures, use the fallback formatter only when the exception contains clear auth/status signals (`401`, `403`, `Unauthorized`, `Forbidden`, `invalid_api_key`, `invalid x-api-key`, `authentication`). Do not label network failures or malformed provider responses as auth errors.
- For default GitHub Copilot failures, use `GetAuthStatusAsync` unauthenticated status as the primary signal. The fallback formatter should recognize only clear SDK-auth signals (`401`, `403`, `Unauthorized`, `Forbidden`, `not authenticated`, `authentication`) and otherwise return the original service error message.
  - Suggested default Copilot message: `GitHub Copilot authentication failed. Run gh auth login or refresh your GitHub sign-in, then restart or refresh UnityCodeAgent.`
  - Suggested BYOK message: `BYOK provider authentication failed. Check the BaseUrl and ApiKey in UnityCodeAgent settings, then refresh models again.`
- Add a small Unity-side helper in `ChatEditorWindowClient` for auth failures, for example `EnqueueAuthFailureUpdates(message, stackTrace)`, so all chat operation failures use the same update ordering.
- When the caught exception is an auth failure, enqueue both:
  - a `ChatShowErrorUpdate` so the failure remains in the transcript as an error;
  - a `ChatShowProgressMessageUpdate` with the same user-facing auth guidance so the chat window also shows the auth state through the progress surface.
- Order the auth progress update after `ChatSetBusyStateUpdate(false)`, or adjust `ChatProgressMessages` only if needed, so the progress text is not removed by normal busy-state cleanup in the same update batch.
- Apply the chat-progress behavior to prompt submission, opening/reconfiguring sessions, and initialization paths that can hit Copilot/BYOK auth errors. Keep model-refresh-only settings failures in the settings help box unless the chat window operation also observes the same auth failure.
- Keep non-auth failures unchanged: they should continue to produce normal error bubbles without duplicating every generic exception as progress.
- Leave logs unchanged or more detailed than the user response: log the original exception and properties, but never log API keys.
- Keep implementation small and direct. Do not use reflection against SDK internal exception types unless explicit auth-status preflight and clear fallback message checks prove insufficient. Do not add new UI controls, new DTO fields, or broad error-code taxonomy unless tests prove the message-only approach is insufficient.

Verification:
- Add or extend `CopilotService.Tests/AgentServiceEndpointContractTests.cs` with fake model/session services that throw auth-like exceptions and assert distinct BYOK vs GitHub Copilot messages.
- Add focused tests around the new auth-status/preflight helper with a fake runtime host or fake auth-status provider so default Copilot unauthenticated status produces the Copilot guidance without needing a live SDK runtime.
- Add `ChatEditorWindowClientE2eTests` coverage where a fake API client throws an auth-shaped `AgentServiceApiException`; assert the result includes both `ChatShowErrorUpdate` and `ChatShowProgressMessageUpdate` with the provider-specific auth message.
- Add or extend `ChatEditorWindowUiE2eTests` only if update ordering needs visual confirmation; assert the auth progress text remains visible after the failed operation and is still removed/replaced by the next real transcript message.
- Add a narrow `ByokOpenAiProvider` unit test if the implementation changes provider response handling directly; otherwise keep tests at the endpoint formatting boundary.
- Run `dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj`.
- Run Unity EditMode tests only if Unity-side client/message handling changes; otherwise existing `HttpAgentServiceApiClientTests` already cover message propagation.

