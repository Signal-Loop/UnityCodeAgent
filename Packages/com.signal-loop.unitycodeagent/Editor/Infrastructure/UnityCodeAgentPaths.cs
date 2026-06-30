using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SignalLoop.UnityCodeAgent.Infrastructure
{
    //TODO: Duplicate with CopilotService\Infrastructure\ProjectPaths.cs
    public sealed class UnityCodeAgentPaths
    {
        public const string DefaultSkillsFolder = ".agents/skills";
        public const string McpConfigRelativePath = ".unityCodeAgent/client/mcp.json";

        public UnityCodeAgentPaths(string projectRoot)
        {
            ProjectRoot = NormalizeProjectRoot(projectRoot);
            SanitizedProjectRoot = SanitizeProjectRoot(ProjectRoot);
            AppRoot = Combine(ProjectRoot, ".unityCodeAgent");
            ClientRoot = Combine(AppRoot, "client");
            ServiceRoot = Combine(AppRoot, "service");
            RuntimeRoot = Combine(ServiceRoot, "runtime");
            McpConfigPath = Combine(ClientRoot, "mcp.json");
            McpConfigProjectRelativePath = McpConfigRelativePath;
            EndpointManifestPath = Combine(RuntimeRoot, "endpoint.json");
            EventCursorPath = Combine(RuntimeRoot, "event-cursor.json");
        }

        public string ProjectRoot { get; }

        public string SanitizedProjectRoot { get; }

        public string AppRoot { get; }

        public string ClientRoot { get; }

        public string ServiceRoot { get; }

        public string RuntimeRoot { get; }

        public string McpConfigPath { get; }

        public string McpConfigProjectRelativePath { get; }

        public string EndpointManifestPath { get; }

        public string EventCursorPath { get; }

        public static string NormalizeProjectRelativePath(string value)
        {
            var parts = (value ?? string.Empty).Trim().Replace('\\', '/').Trim('/').Split('/');
            var normalized = new System.Collections.Generic.List<string>();
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index].Trim();
                if (string.IsNullOrWhiteSpace(part) || string.Equals(part, ".", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(part, "..", StringComparison.Ordinal))
                {
                    if (normalized.Count > 0)
                    {
                        normalized.RemoveAt(normalized.Count - 1);
                    }

                    continue;
                }

                normalized.Add(part);
            }

            return string.Join("/", normalized);
        }

        public static string ToProjectRelativePath(string projectRoot, string absolutePath)
        {
            var root = NormalizeProjectRoot(projectRoot);
            var normalizedPath = Path.GetFullPath(absolutePath).Replace('\\', '/');
            return normalizedPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring(root.Length + 1)
                : string.Empty;
        }

        private static string NormalizeProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            return Path.GetFullPath(projectRoot).Replace('\\', '/').TrimEnd('/');
        }

        private static string SanitizeProjectRoot(string value)
            => Regex.Replace(value, "[^A-Za-z0-9]", "_");

        private static string Combine(string left, string right) => $"{left}/{right}";
    }
}
