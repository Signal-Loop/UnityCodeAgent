using Newtonsoft.Json.Linq;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Tools.Helpers;
using SignalLoop.UnityCodeAgent.Tools.Interfaces;
using SignalLoop.UnityCodeAgent.Tools.Protocol;
using UnityEditor;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Tools.CustomTools
{
    /// <summary>
    /// Tool that enters Unity Play Mode in the Editor.
    /// NOTE: This is a synchronous tool that returns immediately after triggering play mode.
    /// It does NOT wait for play mode to complete because Unity performs a domain reload
    /// during play mode transition which would kill the async context and TCP connection.
    /// </summary>
    public class EnterPlayModeTool : IToolSync
    {
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();
        public string Name => "enter_play_mode";

        public string Description => "Enters Unity Play Mode in the Editor.";

        public JToken InputSchema => JsonHelper.ParseElement(@"
            {
                ""type"": ""object"",
                ""properties"": {}
            }
            ");

        public ToolsCallResult Execute(JToken arguments)
        {
            if (EditorApplication.isPlaying)
            {
                return ToolsCallResult.TextResult("Already in Play Mode.");
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return ToolsCallResult.ErrorResult("Play Mode transition already in progress.");
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return ToolsCallResult.ErrorResult("Unity is compiling or updating. Try again once the Editor is idle.");
            }

            _log.Debug(nameof(EnterPlayModeTool), "EnterPlayModeTool: triggering play mode.");

            EditorApplication.isPlaying = true;
            Time.timeScale = 0;

            return ToolsCallResult.TextResult("Play Mode transition initiated.");
        }
    }


}
