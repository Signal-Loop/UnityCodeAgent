using System;
using UnityEditor;
using UnityEngine.InputSystem;

namespace SignalLoop.UnityCodeAgent.Settings
{
    internal static class UnityCodeAgentInputActionsEditorDrawer
    {
        public static void Draw(UnityCodeAgentSettings settings, SerializedObject serializedObject)
        {
            EditorGUILayout.LabelField("Play Unity Game");
            EditorGUILayout.HelpBox(
                "play_unity_game resolves this InputActionAsset path on every call. " +
                "If left empty, it auto-detects the first InputActionAsset under Assets, then falls back to the first one found anywhere.",
                MessageType.Info);

            InputActionAsset currentAsset = string.IsNullOrWhiteSpace(settings.InputActionsAssetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<InputActionAsset>(settings.InputActionsAssetPath);

            InputActionAsset selectedAsset = (InputActionAsset)EditorGUILayout.ObjectField(
                "Input Actions Asset",
                currentAsset,
                typeof(InputActionAsset),
                false);

            if (selectedAsset != currentAsset)
            {
                serializedObject.ApplyModifiedProperties();
                settings.SetInputActionsAssetPath(selectedAsset == null ? string.Empty : AssetDatabase.GetAssetPath(selectedAsset));
                serializedObject.UpdateIfRequiredOrScript();
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Asset Path", settings.InputActionsAssetPath);
            }

            if (!string.IsNullOrWhiteSpace(settings.InputActionsAssetPath) && currentAsset == null)
            {
                EditorGUILayout.HelpBox(
                    $"No InputActionAsset was found at '{settings.InputActionsAssetPath}'. The play tool will fall back to discovery.",
                    MessageType.Warning);
            }
        }
    }
}
