# Ollama is supported
- status: Backlog
- order: 362
- goal: Support local OpenAI-compatible Ollama endpoints over loopback HTTP, verified by fake `/models` responses, while keeping non-loopback BYOK URLs HTTPS-only.

Current BYOK validation requires HTTPS, which blocks common local Ollama URLs like `http://127.0.0.1:11434/v1`.
Current BYOK model listing uses OpenAI-compatible `/models`, which should fit Ollama's OpenAI-compatible endpoint.

Add explicit provider type/wire API settings if needed; do not special-case beyond OpenAI-compatible local provider support.
Allow loopback HTTP for local providers while keeping non-loopback BYOK URLs HTTPS-only.
Add model-list test using a fake OpenAI-compatible `/models` response.

