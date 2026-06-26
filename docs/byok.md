Implement BYOK in unity client. Currently BYOK is supported only in server side.

Dto is implemented in `ByokProviderDto` in `Assets\Plugins\UnityCodeAgent\Editor\Service\ServiceContracts.cs`.
SDK documentation for BYOK is available at `https://github.com/github/copilot-sdk/blob/main/docs/auth/byok.md`.
Add required ui in `Assets\Plugins\UnityCodeAgent\Editor\Settings\UnityCodeAgentSettings.cs`:
Type: OpenAI | Anthropic, default value: OpenAI, dropdown with these two options
BaseUrl: include full https url validation, no default value, text input,
ApiKey: text input, no default value
WireApi: completions | responses, default value: completions, dropdown with these two options

If no baseUrl is provided, use current data flow to the server and do not include BYOK configuration in the request.

Additionally add Model dropdown and use it instead of hardcoded `gpt-5-mini` in the code. Add refresh button and populate model dropdown with available models from the server when clicked.
To populate model dropdown, create `api/models` endpoint in the server that returns list of available models based on the current BYOK configuration. Implement logic in the client to call this endpoint and update the model dropdown options accordingly when the refresh button is clicked. Use `client.listModels()` method from the SDK to get the list of models from the server. Make sure to handle any errors that may occur during the API call and provide appropriate feedback to the user.
Make it simple, do not add interfaces if not strictly neccessary, follow current convention.