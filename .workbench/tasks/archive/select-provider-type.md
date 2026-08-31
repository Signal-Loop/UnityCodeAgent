# Select Provider Type
- status: Completed
- order: 1900
- goal: Add an explicit Copilot/BYOK provider selection in Unity Code Agent settings, defaulting to Copilot, verified by focused settings/model-selection tests while preserving current Copilot and BYOK request behavior.
- updated: 2026-06-29
- steps:
    - [x] Add explicit provider selection to settings
    - [x] Show BYOK credentials only when BYOK is selected
    - [x] Keep model cache invalidation tied to effective provider identity
    - [x] Add focused settings tests
    - [x] Run focused Unity EditMode tests

User request:
UnityCodeAgentSettings should have drop down that allows user explicitly select between BYOK and Copilot, with Copilot as Default.
When BYOK is selected, BaseUrl and APIKey is visible othervise not.
Verify if surrent Settings properties are valid and still needed for this scenario.
PLan and implementation should be simple and prefer simplification of existing code than addong abstractions

Research:
- `UnityCodeAgentSettings` currently infers BYOK from `ByokBaseUrl`; an empty base URL means default GitHub Copilot.
- `ProviderConfigDto.HasByok` is also inferred from `BaseUrl`, and service code routes model listing, auth checks, and SDK provider config from that flag.
- The service can stay unchanged if Unity sends `BaseUrl` and `ApiKey` only when the selected provider is BYOK.
- Existing `ByokBaseUrl` and `ByokApiKey` remain valid settings for the BYOK provider. They should be hidden/inactive in the inspector for Copilot, not removed.
- `AvailableModelsBaseUrl` is really an effective provider identity cache. It currently stores normalized BYOK base URL or empty string for Copilot. It can continue to work if `GetCurrentBaseUrlKey()` returns empty for Copilot even when stale BYOK fields are present.
- The settings inspector currently clears model selection only when the base URL key changes. Provider selection must also trigger clearing, because switching Copilot <-> BYOK changes the model catalog even if `ByokBaseUrl` is empty or unchanged.

Plan:
- Add a small serialized enum such as `UnityCodeAgentProviderType` with values `Copilot = 0` and `Byok = 1` to `UnityCodeAgentSettings`, defaulting to `Copilot`.
- Update `TryCreateProviderConfig` to:
  - use `null` `BaseUrl` and `ApiKey` for Copilot, regardless of stored BYOK fields;
  - validate `ByokBaseUrl` only when BYOK is selected;
  - pass BYOK base URL/API key only when BYOK is selected.
- Update `GetCurrentBaseUrlKey()` to return empty for Copilot and the normalized BYOK URL for BYOK, preserving current model provenance behavior without adding a new cache field.
- Update `UnityCodeAgentSettingsEditor` to draw a provider dropdown before model selection, show `BaseUrl` and `ApiKey` only for BYOK, and clear cached models/selection when the effective provider key changes.
- Keep `ProviderConfigDto`, OpenAPI/AsyncAPI, and service-side routing unchanged unless tests expose a gap; the existing contract already represents both modes.
- Add or adjust `UnityCodeAgentSettingsModelSelectionTests` for:
  - Copilot selected with a stale BYOK base URL still creates a non-BYOK provider;
  - BYOK selected with invalid or missing base URL fails validation as appropriate;
  - switching provider invalidates an existing selected model;
  - BYOK selected still sends normalized base URL and API key behavior as today.

Verification:
- Run the focused Unity EditMode test class for settings/model selection.
- If Unity test tooling is unavailable in the turn, at minimum run a compile-oriented check where available and record the residual risk in the implementation task.

Implementation notes:
- Added `UnityCodeAgentProviderType` with `Copilot` default and `Byok` opt-in on `UnityCodeAgentSettings`.
- `TryCreateProviderConfig` now sends no BYOK `BaseUrl` or `ApiKey` for Copilot, even if stale BYOK fields are stored, and validates `ByokBaseUrl` only when BYOK is selected.
- `GetCurrentBaseUrlKey()` now uses the effective provider identity: empty for Copilot, normalized BYOK base URL for BYOK.
- The custom settings inspector now draws a provider dropdown and only shows BYOK `BaseUrl`/`ApiKey` fields when BYOK is selected.
- Existing BYOK settings properties remain valid and are still used when BYOK is selected; service DTOs and contracts did not need changes.

Completed verification:
- `run_unity_tests` EditMode `SignalLoop.UnityCodeAgent.Service.UnityCodeAgentSettingsModelSelectionTests`: passed, 9 tests.
- `run_unity_tests` EditMode affected `ChatEditorWindowClientE2eTests` provider/model invalidation cases: passed, 5 tests.
