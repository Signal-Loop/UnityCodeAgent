# Add missing lifecycle/progress chat messages

- goal: Cover remaining silent long operations with transient progress messages, verified by focused UI/client tests, while avoiding persisted transcript noise.

Current progress plumbing exists: `AgentEventType.Progress`, `ChatProgressMessages`, progress template, and client progress callback are in place.

Audit remaining long operations that still feel silent:
- model refresh
- session list load
- session open
- service stop/restart
- tool invocation wait
- settings/model validation failures

Prefer progress updates over persisted transcript `Service` messages for transient state.
Add focused UI/client tests only for newly covered flows.

