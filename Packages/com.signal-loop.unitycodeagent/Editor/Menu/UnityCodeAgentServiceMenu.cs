using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Service;
using SignalLoop.UnityCodeAgent.Settings;
using SignalLoop.UnityCodeAgent.UI;
using UnityEditor;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Menu
{
    public static class UnityCodeAgentServiceMenu
    {
        public const string MenuRoot = "Tools/UnityCodeAgent/";

        [MenuItem(MenuRoot + "Open Chat")]
        public static void OpenChatWindow()
        {
            ChatEditorWindow.ShowWindow();
        }

        [MenuItem(MenuRoot + "Open Settings")]
        public static void OpenSettings()
        {
            var settings = UnityCodeAgentSettings.Instance;
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem(MenuRoot + "Open MCP Config")]
        public static void OpenMcpConfig()
        {
            McpConfigUtility.OpenInEditor(UnityCodeAgentSettings.GetUnityContext().Paths);
        }

        [MenuItem(MenuRoot + "Debug/Start Agent Service")]
        public static void StartService()
        {
            new AgentService().StartInBackground(UnityCodeAgentSettings.GetUnityContext());
        }

        [MenuItem(MenuRoot + "Restart Agent Service")]
        public static void RestartService()
        {
            new AgentService().RestartInBackground(UnityCodeAgentSettings.GetUnityContext());
        }

        [MenuItem(MenuRoot + "Debug/Stop Agent Service")]
        public static void StopService()
        {
            new AgentService().Stop(UnityCodeAgentSettings.GetUnityContext());
        }

        [MenuItem(MenuRoot + "Debug/Agent Service Status")]
        public static void ServiceStatus()
        {
            new AgentService().Status(UnityCodeAgentSettings.GetUnityContext());
        }

        [MenuItem(MenuRoot + "Debug/Snapshot")]
        public static void Snapshot()
        {
            var snapshot = new AgentService().GetSessionsAsync(UnityCodeAgentSettings.GetUnityContext()).GetAwaiter().GetResult();
            Debug.Log(JsonConvert.SerializeObject(snapshot, Formatting.Indented));
        }

        [MenuItem(MenuRoot + "Debug/Get Current Session")]
        public static void GetCurrentSession()
        {
            var history = new AgentService().GetCurrentSessionAsync(UnityCodeAgentSettings.GetUnityContext()).GetAwaiter().GetResult();
            Debug.Log(JsonConvert.SerializeObject(history, Formatting.Indented));
        }
    }
}
