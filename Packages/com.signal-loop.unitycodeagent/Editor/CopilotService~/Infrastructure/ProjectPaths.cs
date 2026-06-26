namespace UnityCodeCopilot.Service.Infrastructure;

//TODO: Duplicate with Assets\Plugins\UnityCodeAgent\Editor\Infrastructure\UnityCodeAgentPaths.cs
public sealed class ProjectPaths
{
    public ProjectPaths(string projectRoot)
    {
        ProjectRoot = Normalize(projectRoot);
        AppRoot = Combine(ProjectRoot, ".unityCodeAgent");
        ClientRoot = Combine(AppRoot, "client");
        ServiceRoot = Combine(AppRoot, "service");
        RuntimeRoot = Combine(ServiceRoot, "runtime");
        LogsRoot = Combine(ServiceRoot, "logs");
        EndpointManifestPath = Combine(RuntimeRoot, "endpoint.json");
        McpConfigPath = Combine(ClientRoot, "mcp.json");
    }

    public string ProjectRoot { get; }

    public string AppRoot { get; }

    public string ClientRoot { get; }

    public string ServiceRoot { get; }

    public string RuntimeRoot { get; }

    public string LogsRoot { get; }

    public string EndpointManifestPath { get; }

    public string McpConfigPath { get; }

    private static string Normalize(string path)
    {
        if (!TryNormalizeProjectRoot(path, out var normalizedPath, out var error))
        {
            throw new ArgumentException(error, nameof(path));
        }

        return normalizedPath;
    }

    public static bool TryNormalizeProjectRoot(string path, out string normalizedPath, out string? error)
    {
        normalizedPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Project root is required.";
            return false;
        }

        try
        {
            var canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (!Directory.Exists(canonicalPath))
            {
                error = "Project root must point to an existing directory.";
                return false;
            }

            normalizedPath = canonicalPath.Replace('\\', '/');
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or PathTooLongException)
        {
            error = "Project root must point to an existing directory.";
            return false;
        }
    }

    private static string Combine(string left, string right) => $"{left}/{right}";
}