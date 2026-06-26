# Agent Service Contract Overview

## Purpose

This document explains the direction of the Unity-to-service contract.

The contract is backend-neutral and uses `Agent` terminology on the Unity side and in shared DTOs. The current .NET service in [CopilotService](CopilotService) is the default implementation of that contract, but backend-neutral role-based names are preferred throughout the codebase. `Copilot` should appear only where a type is specifically about GitHub Copilot SDK integration or a Copilot-specific backend adapter.

## Source Of Truth

The intended source of truth is machine-readable contract artifacts:

- OpenAPI for request and response endpoints
- AsyncAPI for the `/events` SSE stream

Hand-written markdown should explain the contract, not redefine it independently.

## Contract Scope

The Agent service contract covers:

- `GET /health`
- `GET /api/sessions`
- `POST /api/models`
- `POST /api/sessions/create`
- `POST /api/sessions/open`
- `POST /api/sessions/send`
- `POST /api/sessions/abort`
- `GET /events`
- all request and response payloads
- status-code behavior
- error payloads
- SSE replay behavior via `Last-Event-ID`

The contract does not cover Unity bootstrap details such as [.unityCodeAgent/service/runtime/endpoint.json](.unityCodeAgent/service/runtime/endpoint.json) or [Packages/com.signal-loop.unitycodeagent/Editor/Service/EndpointManifest.cs](Packages/com.signal-loop.unitycodeagent/Editor/Service/EndpointManifest.cs).

## Current Design Decisions

1. Request DTOs do not include a `Version` field.
2. Contract-facing Unity names use `Agent`, not `Copilot`.
3. Clean role-based names are preferred throughout the implementation, even inside [CopilotService](CopilotService).
4. `Copilot` should be reserved for GitHub Copilot-specific adapters rather than used as a blanket service prefix.
5. The old raw transport seam has been removed. Unity now uses `AgentServiceClient` over `HttpAgentServiceApiClient` for unary HTTP calls and `SseAgentServiceEventStreamClient` for `/events`.
6. The current HTTP plus SSE runtime protocol remains in place.
7. The current .NET service is a Copilot-backed implementation of the Agent contract, not the contract definition itself.

## Behavioral Notes That Must Be Explicit In The Contract

1. `/events` is global to the service instance.
2. Replay uses `Last-Event-ID`.
3. The current service attaches one live session at a time even though it can list many sessions.
4. `create` and `open` return event-derived snapshots.
5. `send` and `abort` signal acceptance through HTTP and completion through `/events`.

## Design References

The current design and implementation plan live in:

- [docs/superpowers/specs/2026-05-29-agent-service-contract-design.md](docs/superpowers/specs/2026-05-29-agent-service-contract-design.md)
- [docs/superpowers/plans/2026-05-29-agent-service-contract.md](docs/superpowers/plans/2026-05-29-agent-service-contract.md)
