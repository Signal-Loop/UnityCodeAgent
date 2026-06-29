# Service logs are JSONL and replayable in tests

- goal: Service events can be replayed using service log, which is JSONL, verified by parser/replay tests and at least one fixture from real event envelopes.

Current service file log is plain text `service.log`; SDK telemetry can write `telemetry.jsonl`.

Add stable JSONL schema.
Add parser/replay helper in tests and one fixture generated from real event envelopes.
Parser has editor window UI that can be opened from menu.
Parser UI refreshes on demand and lists session names based from log. selected session service events can be replayed.
Button 'Copy to file' copies selected session events to a separate JSONL file for later replay. Ensure that this file is not deleted when rolling logs.
Add focused tests for parser/replay and one fixture from real event envelopes.

