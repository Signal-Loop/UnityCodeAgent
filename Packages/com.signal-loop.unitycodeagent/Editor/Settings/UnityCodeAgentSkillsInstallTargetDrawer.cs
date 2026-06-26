using System;
using System.IO;
using SignalLoop.UnityCodeAgent.Editor.Installer;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Logging;
using UnityEditor;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Settings
{
    internal static class UnityCodeAgentSkillsInstallTargetDrawer
    {
        private static readonly UnityCodeAgentLogger Log = new UnityCodeAgentLogger();

        public static void Draw(UnityCodeAgentSettings settings)
        {
            settings.InitializeSkillsTarget();

            EditorGUILayout.LabelField("Skills Installer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Bundled skill files are installed automatically when the package is installed or updated.",
                MessageType.Info);

            var selectedTarget = (UnityCodeAgentSkillInstallTarget)EditorGUILayout.EnumPopup(
                "Install Directory",
                settings.SkillsInstallTarget);
            if (selectedTarget != settings.SkillsInstallTarget)
            {
                UpdateSkillsTarget(settings, () => settings.SetSkillsInstallTarget(selectedTarget));
            }

            if (settings.SkillsInstallTarget == UnityCodeAgentSkillInstallTarget.Custom)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var customPath = EditorGUILayout.DelayedTextField("Custom Folder", settings.SkillsTargetPath);
                    if (!string.Equals(customPath, settings.SkillsTargetPath, StringComparison.Ordinal))
                    {
                        UpdateSkillsTarget(settings, () => settings.SetCustomSkillsTargetPath(customPath));
                    }

                    if (GUILayout.Button("Browse", GUILayout.Width(70f)))
                    {
                        var chosen = EditorUtility.OpenFolderPanel(
                            "Select skills target folder",
                            string.IsNullOrWhiteSpace(settings.SkillsTargetPath)
                                ? GetProjectRoot()
                                : settings.GetEffectiveSkillsTargetPath(),
                            string.Empty);
                        if (!string.IsNullOrWhiteSpace(chosen))
                        {
                            UpdateSkillsTarget(settings, () => settings.SetCustomSkillsTargetPath(chosen));
                        }
                    }
                }
            }

            EditorGUILayout.LabelField("Current Target Directory", settings.GetEffectiveSkillsTargetPath(), EditorStyles.wordWrappedMiniLabel);
            var projectRelativePath = settings.GetEffectiveSkillsTargetProjectRelativePath();
            if (string.IsNullOrWhiteSpace(projectRelativePath))
            {
                EditorGUILayout.HelpBox(
                    "The selected target is outside the Unity project. Skills will be copied there, but the Agent runtime only receives project-relative skill folders.",
                    MessageType.Warning);
            }
        }

        public static string ResolveSkillsSourcePath()
        {
            return UnityCodeAgentPackagePaths.ResolveExistingDirectory(UnityCodeAgentPackagePaths.SkillsSourceRelativePath);
        }

        private static void UpdateSkillsTarget(UnityCodeAgentSettings settings, Action updateTarget)
        {
            var previousTargetPath = settings.GetEffectiveSkillsTargetPath();
            Undo.RecordObject(settings, "Change Unity Code Agent Skills Install Target");
            updateTarget();
            var newTargetPath = settings.GetEffectiveSkillsTargetPath();

            if (string.Equals(previousTargetPath, newTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var sourcePath = ResolveSkillsSourcePath();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                Log.Warning(nameof(UnityCodeAgentSkillsInstallTargetDrawer), "Could not locate the Skills source directory within the package. Skipping skill relocation.");
                return;
            }

            IFileSystem fileSystem = new EditorFileSystem();
            var installer = new SkillsInstaller(fileSystem);
            if (installer.RelocateInstalledSkills(sourcePath, previousTargetPath, newTargetPath))
            {
                AssetDatabase.Refresh();
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        private static string GetProjectRoot()
            => Path.GetDirectoryName(Application.dataPath);
    }
}
