using System.IO;
using UnityEditor;
using UnityEditorInternal;

namespace SignalLoop.UnityCodeAgent.Infrastructure
{
    public static class McpConfigUtility
    {
        private const string DefaultMcpConfig = "{\n  \"mcpServers\": {}\n}";

        public static string EnsureDefaultExists(UnityCodeAgentPaths paths)
        {
            Directory.CreateDirectory(paths.ClientRoot);

            if (!File.Exists(paths.McpConfigPath))
            {
                File.WriteAllText(paths.McpConfigPath, DefaultMcpConfig);
            }

            return paths.McpConfigPath;
        }

        public static void OpenInEditor(UnityCodeAgentPaths paths)
        {
            var configPath = EnsureDefaultExists(paths);
            // EditorUtility.RevealInFinder(configPath);
            InternalEditorUtility.OpenFileAtLineExternal(configPath, 1);
        }
    }
}