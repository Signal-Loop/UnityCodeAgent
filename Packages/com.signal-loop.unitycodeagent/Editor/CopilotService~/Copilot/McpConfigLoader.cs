using System.Text.Json;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Settings;
using UnityCodeCopilot.Service.Telemetry;

namespace UnityCodeCopilot.Service.Copilot;

public sealed class McpConfigLoader
{
    private const string EmptyMcpConfig = "{\n  \"mcpServers\": {}\n}";
    private const string ConfigInstruction = "Update .unityCodeAgent/client/mcp.json to match the GitHub Copilot SDK MCP schema.";

    private readonly ProjectPaths _paths;
    private readonly UnityCodeCopilotServiceLogger _log;
    private readonly CopilotTelemetry _telemetry;

    public McpConfigLoader(ProjectPaths paths, UnityCodeCopilotServiceLogger log, CopilotTelemetry telemetry)
    {
        _paths = paths;
        _log = log;
        _telemetry = telemetry;
    }

    private async Task<McpLoadResult> LoadServerDefinitionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(_paths.McpConfigPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return CreateInvalidConfigResult("MCP config root must be a JSON object.");
            }

            if (!document.RootElement.TryGetProperty("mcpServers", out var mcpServers) || mcpServers.ValueKind != JsonValueKind.Object)
            {
                return CreateInvalidConfigResult("MCP config must contain an 'mcpServers' object.");
            }

            var servers = new Dictionary<string, McpServerDefinition>(StringComparer.Ordinal);
            var diagnostics = new List<McpConfigDiagnostic>();
            foreach (var server in mcpServers.EnumerateObject())
            {
                var (definition, diagnostic) = TryParseServer(server.Name, server.Value);
                if (definition != null)
                {
                    servers[server.Name] = definition;
                }

                if (diagnostic != null)
                {
                    diagnostics.Add(diagnostic);
                }
            }

            return new McpLoadResult(servers, diagnostics);
        }
        catch (JsonException)
        {
            _log.Warning(nameof(McpConfigLoader), "MCP config file contains invalid JSON.", ("path", _paths.McpConfigPath));
            return CreateInvalidConfigResult("MCP config file contains invalid JSON.");
        }
    }

    public Task<McpLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.ClientRoot);

        return _telemetry.ExecuteAsync(TelemetryOperations.ServiceMcpLoadConfig, async operation =>
        {
            operation.SetTag("mcp.config.path", _paths.McpConfigPath);

            if (!File.Exists(_paths.McpConfigPath))
            {
                await File.WriteAllTextAsync(_paths.McpConfigPath, EmptyMcpConfig, cancellationToken);
                _log.Info(nameof(McpConfigLoader), "Created default MCP config file.", ("path", _paths.McpConfigPath));
            }

            var result = await LoadServerDefinitionsAsync(cancellationToken);

            if (result.Diagnostics.Count > 0)
            {
                operation.SetOutcome("degraded");
            }

            operation.SetTags(
                ("mcp.server.count", result.Servers.Count),
                ("mcp.diagnostic.count", result.Diagnostics.Count));

            _log.Info(nameof(McpConfigLoader), "Loaded MCP config.",
                ("serverCount", result.Servers.Count),
                ("diagnosticCount", result.Diagnostics.Count),
                ("path", _paths.McpConfigPath));
            return result;
        });
    }

    private McpLoadResult CreateInvalidConfigResult(string problem)
        => new(
            new Dictionary<string, McpServerDefinition>(),
            new[]
            {
                new McpConfigDiagnostic("error", $"Invalid MCP config in {_paths.McpConfigPath}. {problem} {ConfigInstruction}")
            });

    private (McpServerDefinition? Definition, McpConfigDiagnostic? Diagnostic) TryParseServer(string serverName, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return (null, CreateInvalidServerDiagnostic(serverName, "Server configuration must be a JSON object."));
        }

        var type = NormalizeServerType(ReadOptionalString(value, "type"));
        var tools = ReadOptionalStringArray(value, "tools");
        var timeout = ReadOptionalInt32(value, "timeout");

        if (string.IsNullOrEmpty(type) || string.Equals(type, "local", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            var command = ReadOptionalString(value, "command");
            if (string.IsNullOrWhiteSpace(command))
            {
                return (null, CreateInvalidServerDiagnostic(serverName, "Local MCP servers require a non-empty 'command' string."));
            }

            if (!HasArrayProperty(value, "args"))
            {
                return (null, CreateInvalidServerDiagnostic(serverName, "Local MCP servers require an 'args' array."));
            }

            return (new StdioMcpServerDefinition(
                command,
                ReadStringArray(value, "args"),
                ReadStringDictionary(value, "env"),
                ReadOptionalString(value, "cwd"),
                tools,
                timeout), null);
        }

        if (string.Equals(type, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(type, "sse", StringComparison.OrdinalIgnoreCase))
        {
            var url = ReadOptionalString(value, "url");
            if (string.IsNullOrWhiteSpace(url))
            {
                return (null, CreateInvalidServerDiagnostic(serverName, "Remote MCP servers require a non-empty 'url' string."));
            }

            return (new HttpMcpServerDefinition(
                url,
                ReadStringDictionary(value, "headers"),
                tools,
                timeout,
                type), null);
        }

        return (null, CreateInvalidServerDiagnostic(serverName, $"Server type '{type}' is not supported. Use no type or 'local'/'stdio' for local servers, or 'http'/'sse' for remote servers."));
    }

    private McpConfigDiagnostic CreateInvalidServerDiagnostic(string serverName, string problem)
        => new("error", $"Invalid MCP server '{serverName}' in {_paths.McpConfigPath}. {problem} {ConfigInstruction}");

    private static string? NormalizeServerType(string? type)
        => string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();

    private static bool HasArrayProperty(JsonElement value, string propertyName)
        => value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array;

    private static string? ReadOptionalString(JsonElement value, string propertyName)
        => value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? ReadOptionalInt32(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.TryGetInt32(out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var items = new List<string>();
        foreach (var element in property.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var item = element.GetString();
                if (!string.IsNullOrWhiteSpace(item))
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    private static IReadOnlyList<string>? ReadOptionalStringArray(JsonElement value, string propertyName)
        => value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? ReadStringArray(value, propertyName)
            : null;

    private static IReadOnlyDictionary<string, string>? ReadStringDictionary(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String)
            {
                var itemValue = item.Value.GetString();
                if (itemValue != null)
                {
                    dictionary[item.Name] = itemValue;
                }
            }
        }

        return dictionary;
    }
}

public sealed record McpLoadResult(IReadOnlyDictionary<string, McpServerDefinition> Servers, IReadOnlyList<McpConfigDiagnostic> Diagnostics);

public sealed record McpConfigDiagnostic(string Severity, string Message);

public abstract record McpServerDefinition(IReadOnlyList<string>? Tools, int? Timeout);

public sealed record StdioMcpServerDefinition(
    string Command,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string>? Env,
    string? WorkingDirectory,
    IReadOnlyList<string>? Tools,
    int? Timeout) : McpServerDefinition(Tools, Timeout);

public sealed record HttpMcpServerDefinition(
    string Url,
    IReadOnlyDictionary<string, string>? Headers,
    IReadOnlyList<string>? Tools,
    int? Timeout,
    string RemoteType = "http") : McpServerDefinition(Tools, Timeout);