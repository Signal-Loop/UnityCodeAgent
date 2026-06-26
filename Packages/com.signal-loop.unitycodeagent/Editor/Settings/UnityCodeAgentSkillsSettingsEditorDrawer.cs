using System.IO;
using SignalLoop.UnityCodeAgent.Infrastructure;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Settings
{
    internal static class UnityCodeAgentSkillsSettingsEditorDrawer
    {
        public static void Draw(UnityCodeAgentSettings settings)
        {
            settings.EnsureDefaults();

            EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel);
            DrawFolders(settings);
            DrawDiscoveredSkills(settings);
        }

        public static void DrawMcp()
        {
            EditorGUILayout.LabelField("MCP", EditorStyles.boldLabel);
            var paths = new UnityCodeAgentPaths(GetProjectRoot());
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(paths.McpConfigProjectRelativePath, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Open", GUILayout.Width(72f)))
                {
                    McpConfigUtility.OpenInEditor(paths);
                }
            }
        }

        private static void DrawFolders(UnityCodeAgentSettings settings)
        {
            EditorGUILayout.LabelField("Skill folders:");
            var folders = settings.Skills.GetEnabledSkillDirectories();
            for (var index = 0; index < folders.Count; index++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.SelectableLabel(folders[index], GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    if (GUILayout.Button("Remove", GUILayout.Width(72f)))
                    {
                        Undo.RecordObject(settings, "Remove Unity Code Agent Skills Folder");
                        settings.Skills.RemoveFolder(folders[index]);
                        EditorUtility.SetDirty(settings);
                        return;
                    }
                }
            }

            if (GUILayout.Button("Add Skills Folder"))
            {
                var selected = EditorUtility.OpenFolderPanel("Select Skills Folder", GetProjectRoot(), string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    var projectRelativePath = UnityCodeAgentPaths.ToProjectRelativePath(GetProjectRoot(), selected);
                    if (string.IsNullOrWhiteSpace(projectRelativePath))
                    {
                        EditorUtility.DisplayDialog("Invalid Skills Folder", "Skills folders must be inside the current Unity project.", "OK");
                        return;
                    }

                    Undo.RecordObject(settings, "Add Unity Code Agent Skills Folder");
                    settings.Skills.AddFolder(projectRelativePath);
                    EditorUtility.SetDirty(settings);
                }
            }
        }

        private static void DrawDiscoveredSkills(UnityCodeAgentSettings settings)
        {
            var skills = UnityCodeAgentSkillCatalog.Discover(GetProjectRoot(), settings.Skills);
            if (skills.Count == 0)
            {
                EditorGUILayout.HelpBox("No skills found. Add folders that contain skill-name/SKILL.md directories.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Available skills");
            foreach (var skill in skills)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var enabled = settings.IsSkillEnabled(skill.Name);
                    var nextEnabled = EditorGUILayout.Toggle(enabled, GUILayout.Width(20f));
                    if (nextEnabled != enabled)
                    {
                        Undo.RecordObject(settings, "Toggle Unity Code Agent Skill");
                        settings.SetSkillEnabled(skill.Name, nextEnabled);
                        EditorUtility.SetDirty(settings);
                    }

                    if (GUILayout.Button(skill.Name, EditorStyles.linkLabel))
                    {
                        InternalEditorUtility.OpenFileAtLineExternal(Path.Combine(GetProjectRoot(), skill.SkillFileProjectRelativePath), 1);
                    }

                    EditorGUILayout.LabelField(skill.FolderProjectRelativePath, EditorStyles.miniLabel);
                }
            }
        }

        private static string GetProjectRoot()
            => Path.GetDirectoryName(Application.dataPath);
    }
}
