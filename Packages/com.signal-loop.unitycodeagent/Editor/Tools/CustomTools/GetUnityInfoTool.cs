using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using SignalLoop.UnityCodeAgent.Settings;
using SignalLoop.UnityCodeAgent.Tools.Interfaces;
using SignalLoop.UnityCodeAgent.Tools.Protocol;
using UnityEditor;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Tools.CustomTools
{
    /// <summary>
    /// Tool that returns Unity Editor project information and current agent context.
    /// </summary>
    public class GetUnityInfoTool : IToolSync, IUnityContextTool
    {
        private const string UnavailableValue = "(not provided)";

        private UnityContext _context;

        public string Name => "get_unity_info";

        public string Description =>
            @"Returns information about the current Unity Editor project and UnityCodeAgent context.

**Returns:**
- `project_path`: The absolute path to the Unity project root directory.
- `context`: The current values relevant to local tools.";

        public JToken InputSchema => JsonHelper.ParseElement(@"
        {
            ""type"": ""object"",
            ""properties"": {}
        }
        ");

        public void SetContext(UnityContext context)
            => _context = context;

        public ToolsCallResult Execute(JToken arguments)
        {
            var context = _context;
            var projectPath = context?.Paths.ProjectRoot ?? Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            var sb = new StringBuilder();
            sb.AppendLine("## Unity Project Info");
            sb.AppendLine();
            sb.AppendLine($"**Project Path:** {projectPath}");
            sb.AppendLine($"**Unity Version:** {Application.unityVersion}");
            sb.AppendLine();
            sb.AppendLine("## UnityCodeAgent Tool Settings");
            sb.AppendLine();
            sb.AppendLine($"- **Min Log Level:** {(context == null ? UnavailableValue : context.MinLogLevel.ToString())}");
            sb.AppendLine($"- **Log To File:** {(context == null ? UnavailableValue : context.LogToFile.ToString())}");
            sb.AppendLine($"- **Input Actions Asset Path:** {(context == null || string.IsNullOrWhiteSpace(context.InputActionsAssetPath) ? "(auto-detect)" : context.InputActionsAssetPath)}");
            sb.AppendLine();
            sb.AppendLine("### Script Execution Assemblies");
            AppendAssemblies(sb, "Default Assemblies", UnityCodeAgentSettings.DefaultToolAssemblyNames);
            AppendAssemblies(sb, "Additional Assemblies", context?.AdditionalToolAssemblyNames);
            sb.AppendLine();
            string mode = EditorApplication.isPlaying ? "Play Mode" : "Edit Mode";
            sb.AppendLine($"**Unity Editor is in {mode}**");

            return ToolsCallResult.TextResult(sb.ToString());
        }

        private static void AppendAssemblies(StringBuilder sb, string title, System.Collections.Generic.IReadOnlyList<string> assemblies)
        {
            sb.AppendLine($"**{title}:**");
            if (assemblies == null || assemblies.Count == 0)
            {
                sb.AppendLine("  (none)");
                return;
            }

            foreach (var assembly in assemblies)
            {
                sb.AppendLine($"  - {assembly}");
            }
        }
    }
}
