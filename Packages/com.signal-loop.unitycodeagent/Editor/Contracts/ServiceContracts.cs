using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

#nullable enable

namespace SignalLoop.UnityCodeAgent.Contracts
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum AgentEventType
    {
        Unknown = 0,
        UserMessage,
        Session,
        AssistantMessage,
        AssistantDelta,
        Tool,
        Service,
        Input,
        Skill,
        Subagent,
        System,
        Mcp,
        Resource,
        Diagnostics,
        Error,
        Reasoning,
        ReasoningDelta,
        SessionIdle,
        SessionStatusChanged,
        ToolInvocationRequest
    }

    public sealed record CreateAgentSessionRequestDto(string SessionId, ProviderConfigDto Provider, bool Streaming, InfiniteSessionsDto InfiniteSessions, string WorkingDirectory, IReadOnlyList<string>? SkillDirectories = null, IReadOnlyList<string>? DisabledSkills = null, IReadOnlyList<AgentToolDefinitionDto>? Tools = null)
    {
        [JsonIgnore]
        public string Model => Provider?.Model ?? string.Empty;
    }

    public sealed record OpenAgentSessionRequestDto(string SessionId, ProviderConfigDto Provider, bool Streaming, InfiniteSessionsDto InfiniteSessions, string WorkingDirectory, IReadOnlyList<string>? SkillDirectories = null, IReadOnlyList<string>? DisabledSkills = null, IReadOnlyList<AgentToolDefinitionDto>? Tools = null)
    {
        [JsonIgnore]
        public string Model => Provider?.Model ?? string.Empty;
    }

    public sealed record ListAgentModelsRequestDto(ProviderConfigDto? Provider = null);

    public sealed record SendAgentPromptRequestDto(string SessionId, string Prompt);

    public sealed record AbortAgentPromptRequestDto(string SessionId);

    public sealed record AgentToolDefinitionDto(string Name, string Description, string InputSchemaJson);

    public sealed record AgentToolInvocationRequestDto(string CallId, string SessionId, string ToolName, string ArgumentsJson);

    public sealed record AgentToolInvocationResultDto(
        string CallId,
        string SessionId,
        string ToolName,
        bool IsError,
        string TextResult,
        IReadOnlyList<AgentToolBinaryResultDto>? BinaryResults = null,
        string? Error = null);

    public sealed record AgentToolBinaryResultDto(string Data, string MimeType, string Type, string? Description = null);

    public sealed record InfiniteSessionsDto(bool Enabled = false, double BackgroundCompactionThreshold = 0, double BufferExhaustionThreshold = 0);

    public static class AgentSessionRequestSignature
    {
        public static string Create(
            ProviderConfigDto? provider,
            string? workingDirectory,
            IReadOnlyList<string>? skillDirectories,
            IReadOnlyList<string>? disabledSkills,
            IReadOnlyList<AgentToolDefinitionDto>? tools)
        {
            var builder = new StringBuilder();
            AppendPart(builder, "provider", provider?.Signature ?? string.Empty);
            AppendPart(builder, "workingDirectory", workingDirectory ?? string.Empty);
            AppendStringList(builder, "skill", skillDirectories);
            AppendStringList(builder, "disabledSkill", disabledSkills);
            AppendToolList(builder, tools);
            return builder.ToString();
        }

        public static string Create(CreateAgentSessionRequestDto request)
            => request == null
                ? throw new ArgumentNullException(nameof(request))
                : Create(request.Provider, request.WorkingDirectory, request.SkillDirectories, request.DisabledSkills, request.Tools);

        public static string Create(OpenAgentSessionRequestDto request)
            => request == null
                ? throw new ArgumentNullException(nameof(request))
                : Create(request.Provider, request.WorkingDirectory, request.SkillDirectories, request.DisabledSkills, request.Tools);

        private static void AppendStringList(StringBuilder builder, string key, IReadOnlyList<string>? values)
        {
            if (values == null)
            {
                return;
            }

            foreach (var value in values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .OrderBy(value => value, StringComparer.Ordinal))
            {
                AppendPart(builder, key, value);
            }
        }

        private static void AppendToolList(StringBuilder builder, IReadOnlyList<AgentToolDefinitionDto>? tools)
        {
            if (tools == null)
            {
                return;
            }

            foreach (var tool in tools
                .Where(tool => tool != null)
                .OrderBy(tool => tool.Name ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(tool => tool.Description ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(tool => tool.InputSchemaJson ?? string.Empty, StringComparer.Ordinal))
            {
                AppendPart(builder, "tool.name", tool.Name ?? string.Empty);
                AppendPart(builder, "tool.description", tool.Description ?? string.Empty);
                AppendPart(builder, "tool.schema", tool.InputSchemaJson ?? string.Empty);
            }
        }

        private static void AppendPart(StringBuilder builder, string key, string value)
        {
            builder.Append(key);
            builder.Append('=');
            builder.Append((value ?? string.Empty).Length);
            builder.Append(':');
            builder.Append(value ?? string.Empty);
            builder.Append('|');
        }
    }

    public sealed record ProviderConfigDto(string? Model, string? Type = null, string? BaseUrl = null, string? ApiKey = null, string? WireApi = null, string? ModelName = null)
    {
        [JsonIgnore]
        public bool HasByok => !string.IsNullOrWhiteSpace(BaseUrl);

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                var modelLabel = CreateModelLabel(Model, ModelName);
                if (string.IsNullOrWhiteSpace(modelLabel))
                {
                    modelLabel = "No model selected";
                }

                return HasByok
                    ? $"{modelLabel} via {NormalizeBaseUrl(BaseUrl)}"
                    : modelLabel;
            }
        }

        [JsonIgnore]
        public string Signature => CreateSignature(Model, Type, BaseUrl, ApiKey, WireApi);

        public override string ToString()
            => $"ProviderConfigDto {{ Model = {Model}, BaseUrl = {BaseUrl}, ApiKey = [REDACTED] }}";

        public static ProviderConfigDto Empty { get; } = new ProviderConfigDto(null, null, null, null, null, null);

        public static ProviderConfigDto Create(ModelInfoDto? model, string? type, string? baseUrl, string? apiKey, string? wireApi)
        {
            var rawApiKey = apiKey ?? string.Empty;
            var trimmedApiKey = string.IsNullOrWhiteSpace(rawApiKey) ? null : rawApiKey.Trim();
            return new ProviderConfigDto(
                model?.Id,
                NormalizeOptional(type),
                NormalizeBaseUrl(baseUrl),
                trimmedApiKey,
                NormalizeOptional(wireApi),
                model?.Name);
        }

        public static string CreateSignature(string? model, string? type, string? baseUrl, string? apiKey, string? wireApi)
            => string.Join(
                "\u001f",
                model ?? string.Empty,
                NormalizeOptional(type) ?? string.Empty,
                NormalizeBaseUrl(baseUrl) ?? string.Empty,
                HashApiKeyForSignature(apiKey),
                NormalizeOptional(wireApi) ?? string.Empty);

        public static string? NormalizeBaseUrl(string? baseUrl)
        {
            var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        private static string CreateModelLabel(string? model, string? modelName)
        {
            var rawModel = model ?? string.Empty;
            var rawModelName = modelName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawModel))
            {
                return string.IsNullOrWhiteSpace(rawModelName) ? string.Empty : rawModelName.Trim();
            }

            var trimmedModel = rawModel.Trim();
            if (string.IsNullOrWhiteSpace(rawModelName) || string.Equals(rawModelName, rawModel, StringComparison.Ordinal))
            {
                return trimmedModel;
            }

            return $"{rawModelName.Trim()} ({trimmedModel})";
        }

        private static string? NormalizeOptional(string? value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        private static string HashApiKeyForSignature(string? apiKey)
        {
            var rawApiKey = apiKey ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawApiKey))
            {
                return string.Empty;
            }

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawApiKey.Trim()));
            return Convert.ToBase64String(hash);
        }
    }

    [Serializable]
    public sealed record ModelInfoDto
    {
        [JsonProperty]
        public string Id;

        [JsonProperty]
        public string Name;

        public ModelInfoDto(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString()
            => string.IsNullOrWhiteSpace(Id) || string.Equals(Name, Id, StringComparison.Ordinal)
                        ? Name
                        : $"{Name} ({Id})";
    }

    public sealed record SessionSummaryDto(
        string SessionId,
        DateTimeOffset StartTime = default,
        DateTimeOffset ModifiedTime = default,
        string? Summary = null,
        bool IsRemote = false,
        string? Cwd = null,
        string? GitRoot = null,
        string? Repository = null,
        string? Branch = null);

    public sealed record AgentSessionResponseDto(string SessionId, string Status, IReadOnlyList<AgentServiceEventEnvelope> Messages);

    public static class AgentServiceErrorCodes
    {
        public const string OperationFailed = "operation_failed";
        public const string SessionUnavailable = "session_unavailable";
    }

    public sealed record AgentServiceErrorResponse(string Message, string? Code = null);

    public sealed record AgentServiceEventEnvelope(
        long SequenceNumber,
        string SessionId,
        DateTimeOffset TimestampUtc,
        string Content,
        string StreamKey,
        AgentEventType Type,
        string SourceJson,
        bool IsSubAgentEvent);
}
