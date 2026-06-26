using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Tools.AsyncAwait;
using SignalLoop.UnityCodeAgent.Tools.Helpers;
using SignalLoop.UnityCodeAgent.Tools.Interfaces;
using SignalLoop.UnityCodeAgent.Tools.Protocol;
using UnityEditor;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Tools.CustomTools
{
    /// <summary>
    /// Tool that exits Unity Play Mode in the Editor.
    /// This async tool waits for the play mode transition to complete before resetting Time.timeScale.
    /// </summary>
    public class ExitPlayModeTool : IToolAsync
    {
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();

        public string Name => "exit_play_mode";

        public string Description => "Exits Unity Play Mode in the Editor. Returns immediately after triggering exit. Note: Unity will perform a domain reload which may briefly disconnect the tool host.";

        public JToken InputSchema => JsonHelper.ParseElement(@"
            {
                ""type"": ""object"",
                ""properties"": {}
            }
            ");

        public async Task<ToolsCallResult> ExecuteAsync(JToken arguments)
        {
            if (!EditorApplication.isPlaying)
            {
                Time.timeScale = 1;
                return ToolsCallResult.TextResult("Unity is already in Edit Mode.");
            }

            _log.Debug(nameof(ExitPlayModeTool), "ExitPlayModeTool: triggering exit play mode.");

            EditorApplication.isPlaying = false;
            await UnityEditorAsync.DelayRealtimeAsync(1);
            Time.timeScale = 1;

            return ToolsCallResult.TextResult("Exit Play Mode transition initiated.");
        }
    }


}