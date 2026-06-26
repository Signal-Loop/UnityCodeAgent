# Session Event Types

This document lists every `Type` string value defined by `public override string Type => ...` in `SessionEvents.cs`, grouped by category.

## Session
- `session.start` (SessionStartEvent)
- `session.resume` (SessionResumeEvent)
- `session.remote_steerable_changed` (SessionRemoteSteerableChangedEvent)
- `session.error` (SessionErrorEvent)
- `session.idle` (SessionIdleEvent)
- `session.title_changed` (SessionTitleChangedEvent)
- `session.schedule_created` (SessionScheduleCreatedEvent)
- `session.schedule_cancelled` (SessionScheduleCancelledEvent)
- `session.info` (SessionInfoEvent)
- `session.warning` (SessionWarningEvent)
- `session.model_change` (SessionModelChangeEvent)
- `session.mode_changed` (SessionModeChangedEvent)
- `session.plan_changed` (SessionPlanChangedEvent)
- `session.workspace_file_changed` (SessionWorkspaceFileChangedEvent)
- `session.handoff` (SessionHandoffEvent)
- `session.truncation` (SessionTruncationEvent)
- `session.snapshot_rewind` (SessionSnapshotRewindEvent)
- `session.shutdown` (SessionShutdownEvent)
- `session.context_changed` (SessionContextChangedEvent)
- `session.usage_info` (SessionUsageInfoEvent)
- `session.compaction_start` (SessionCompactionStartEvent)
- `session.compaction_complete` (SessionCompactionCompleteEvent)
- `session.task_complete` (SessionTaskCompleteEvent)
- `session.custom_notification` (SessionCustomNotificationEvent)
- `session.tools_updated` (SessionToolsUpdatedEvent)
- `session.background_tasks_changed` (SessionBackgroundTasksChangedEvent)
- `session.skills_loaded` (SessionSkillsLoadedEvent)
- `session.custom_agents_updated` (SessionCustomAgentsUpdatedEvent)
- `session.mcp_servers_loaded` (SessionMcpServersLoadedEvent)
- `session.mcp_server_status_changed` (SessionMcpServerStatusChangedEvent)
- `session.extensions_loaded` (SessionExtensionsLoadedEvent)
- `session.canvas.opened` (SessionCanvasOpenedEvent)
- `session.canvas.registry_changed` (SessionCanvasRegistryChangedEvent)

## Assistant
- `assistant.turn_start` (AssistantTurnStartEvent)
- `assistant.intent` (AssistantIntentEvent)
- `assistant.reasoning` (AssistantReasoningEvent)
- `assistant.message` (AssistantMessageEvent)
- `assistant.message_start` (AssistantMessageStartEvent)
- `assistant.turn_end` (AssistantTurnEndEvent)
- `assistant.usage` (AssistantUsageEvent)

### Streaming
- `assistant.reasoning_delta` (AssistantReasoningDeltaEvent)
- `assistant.streaming_delta` (AssistantStreamingDeltaEvent)
- `assistant.message_delta` (AssistantMessageDeltaEvent)

## Tool
- `tool.user_requested` (ToolUserRequestedEvent)
- `tool.execution_start` (ToolExecutionStartEvent)
- `tool.execution_complete` (ToolExecutionCompleteEvent)
- `external_tool.requested` (ExternalToolRequestedEvent)
- `external_tool.completed` (ExternalToolCompletedEvent)
- `mcp_app.tool_call_complete` (McpAppToolCallCompleteEvent)

### Streaming
- `tool.execution_partial_result` (ToolExecutionPartialResultEvent)
- `tool.execution_progress` (ToolExecutionProgressEvent)

## Service
- `command.queued` (CommandQueuedEvent)
- `command.execute` (CommandExecuteEvent)
- `command.completed` (CommandCompletedEvent)
- `commands.changed` (CommandsChangedEvent)
- `capabilities.changed` (CapabilitiesChangedEvent)
- `auto_mode_switch.requested` (AutoModeSwitchRequestedEvent)
- `auto_mode_switch.completed` (AutoModeSwitchCompletedEvent)
- `exit_plan_mode.requested` (ExitPlanModeRequestedEvent)
- `exit_plan_mode.completed` (ExitPlanModeCompletedEvent)

## Input
- `permission.requested` (PermissionRequestedEvent)
- `permission.completed` (PermissionCompletedEvent)
- `user_input.requested` (UserInputRequestedEvent)
- `user_input.completed` (UserInputCompletedEvent)
- `user.message` (UserMessageEvent)
- `pending_messages.modified` (PendingMessagesModifiedEvent)
- `elicitation.requested` (ElicitationRequestedEvent)
- `elicitation.completed` (ElicitationCompletedEvent)
- `sampling.requested` (SamplingRequestedEvent)
- `sampling.completed` (SamplingCompletedEvent)

## Skill
- `skill.invoked` (SkillInvokedEvent)

## Subagent
- `subagent.started` (SubagentStartedEvent)
- `subagent.completed` (SubagentCompletedEvent)
- `subagent.failed` (SubagentFailedEvent)
- `subagent.selected` (SubagentSelectedEvent)
- `subagent.deselected` (SubagentDeselectedEvent)

## System
- `system.message` (SystemMessageEvent)
- `system.notification` (SystemNotificationEvent)
- `hook.start` (HookStartEvent)
- `hook.end` (HookEndEvent)
- `agent_completed` (SystemMessageMetadata, SystemNotificationAgentCompleted)
- `agent_idle` (SystemNotificationAgentIdle)
- `new_inbox_message` (SystemNotificationNewInboxMessage)
- `instruction_discovered` (SystemNotificationInstructionDiscovered)
- `shell_completed` (SystemNotificationShellCompleted)
- `shell_detached_completed` (SystemNotificationShellDetachedCompleted)

## MCP
- `mcp.oauth_required` (McpOauthRequiredEvent)
- `mcp.oauth_completed` (McpOauthCompletedEvent)

## Resource
- `file` (UserMessageAttachmentFile, UserMessageAttachmentFileLineRange)
- `directory` (UserMessageAttachmentDirectory)
- `selection` (UserMessageAttachmentSelection, UserMessageAttachmentSelectionDetails)
- `github_reference` (UserMessageAttachmentGithubReference)
- `blob` (UserMessageAttachmentBlob)
- `text` (ToolExecutionCompleteContentText, ToolExecutionCompleteError)
- `terminal` (ToolExecutionCompleteContentTerminal)
- `image` (ToolExecutionCompleteContentImage)
- `audio` (ToolExecutionCompleteContentAudio)
- `resource_link` (ToolExecutionCompleteContentResourceLink, ToolExecutionCompleteContentResourceLinkIcon)
- `resource` (ToolExecutionCompleteContentResource)

## Diagnostics
- `model.call_failure` (ModelCallFailureEvent)
- `abort` (AbortEvent)

## Notes

- Source: `SessionEvents.cs`
- Count: 101 event types
- Includes all `public override string Type => ...` values in the generated file
