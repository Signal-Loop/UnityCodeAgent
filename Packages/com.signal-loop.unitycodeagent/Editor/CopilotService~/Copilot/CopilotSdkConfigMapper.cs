using GitHub.Copilot;

namespace UnityCodeCopilot.Service.Copilot;

public static class CopilotSdkConfigMapper
{
    public static Dictionary<string, McpServerConfig>? ToMcpServers(IReadOnlyDictionary<string, McpServerDefinition> servers)
    {
        if (servers == null || servers.Count == 0)
        {
            return null;
        }

        var mappedServers = new Dictionary<string, McpServerConfig>(servers.Count, StringComparer.Ordinal);
        foreach (var pair in servers)
        {
            var config = ToMcpServerConfig(pair.Value);
            if (config != null)
            {
                mappedServers[pair.Key] = config;
            }
        }

        return mappedServers.Count == 0 ? null : mappedServers;
    }

    private static McpServerConfig? ToMcpServerConfig(McpServerDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        return definition switch
        {
            StdioMcpServerDefinition stdio => CreateLocalServerConfig(stdio),
            HttpMcpServerDefinition http => CreateRemoteServerConfig(http),
            _ => null,
        };
    }

    private static McpStdioServerConfig CreateLocalServerConfig(StdioMcpServerDefinition stdio)
    {
        var config = new McpStdioServerConfig
        {
            Command = stdio.Command,
            Args = CopyList(stdio.Args) ?? new List<string>(),
            Tools = ResolveTools(stdio.Tools),
            Timeout = stdio.Timeout,
        };

        if (stdio.Env != null)
        {
            config.Env = CopyDictionary(stdio.Env);
        }

        if (stdio.WorkingDirectory != null)
        {
            config.WorkingDirectory = stdio.WorkingDirectory;
        }

        return config;
    }

    private static McpHttpServerConfig CreateRemoteServerConfig(HttpMcpServerDefinition http)
    {
        var config = new McpHttpServerConfig
        {
            Url = http.Url,
            Tools = ResolveTools(http.Tools),
            Timeout = http.Timeout,
        };

        if (http.Headers != null)
        {
            config.Headers = CopyDictionary(http.Headers);
        }

        return config;
    }

    private static List<string> ResolveTools(IReadOnlyList<string>? values)
        => values == null ? new List<string> { "*" } : new List<string>(values);

    private static List<string>? CopyList(IReadOnlyList<string>? values)
        => values == null ? null : new List<string>(values);

    private static Dictionary<string, string>? CopyDictionary(IReadOnlyDictionary<string, string>? values)
        => values == null ? null : new Dictionary<string, string>(values, StringComparer.Ordinal);
}