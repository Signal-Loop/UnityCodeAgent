
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Settings
{
    internal static class UnityCodeAgentPackagePaths
    {
        private const string ProjectPackageRootAssetPath = "Packages/com.signal-loop.unitycodeagent";

        public const string ServiceRootRelativePath = "Editor/CopilotService~";
        public const string SkillsSourceRelativePath = "Editor/Skills~";
        public const string SettingsAssetPath = "Assets/Plugins/UnityCodeAgent/Editor/UnityCodeAgentSettings.asset";

        public static T LoadAsset<T>(string relativePath) where T : UnityEngine.Object
        {
            foreach (var candidate in GetAssetPathCandidates(relativePath))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(candidate);
                if (asset != null)
                {
                    return asset;
                }
            }

            return null;
        }

        public static string ResolveAssetPath(string relativePath)
        {
            foreach (var candidate in GetAssetPathCandidates(relativePath))
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(candidate) != null)
                {
                    return candidate;
                }
            }

            return CombineAssetPath(ProjectPackageRootAssetPath, relativePath);
        }

        public static string ResolveProjectFileSystemPath(string relativePath)
            => NormalizePath(Path.GetFullPath(Path.Combine(ProjectPackageRootAssetPath, NormalizeRelativePath(relativePath))));

        public static string ResolveExistingDirectory(string relativePath)
        {
            foreach (var candidate in GetFileSystemPathCandidates(relativePath))
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        public static string ResolveServiceRootFileSystemPath()
            => ResolveExistingDirectory(ServiceRootRelativePath)
               ?? ResolveProjectFileSystemPath(ServiceRootRelativePath);

        public static string ResolveSkillsSourceFileSystemPath()
            => ResolveExistingDirectory(SkillsSourceRelativePath)
               ?? ResolveProjectFileSystemPath(SkillsSourceRelativePath);

        private static IEnumerable<string> GetAssetPathCandidates(string relativePath)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UnityCodeAgentSettings).Assembly);
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                var packageRootAssetPath = TryGetProjectRelativePath(packageInfo.resolvedPath);
                yield return CombineAssetPath(
                    string.IsNullOrWhiteSpace(packageRootAssetPath) ? $"Packages/{packageInfo.name}" : packageRootAssetPath,
                    relativePath);
            }

            yield return CombineAssetPath(ProjectPackageRootAssetPath, relativePath);
        }

        private static IEnumerable<string> GetFileSystemPathCandidates(string relativePath)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UnityCodeAgentSettings).Assembly);
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                yield return NormalizePath(Path.Combine(packageInfo.resolvedPath, NormalizeRelativePath(relativePath)));
            }

            yield return NormalizePath(Path.GetFullPath(Path.Combine(ProjectPackageRootAssetPath, NormalizeRelativePath(relativePath))));
        }

        private static string TryGetProjectRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return null;
            }

            var projectRoot = NormalizePath(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))).TrimEnd('/');
            var normalizedPath = NormalizePath(Path.GetFullPath(absolutePath)).TrimEnd('/');
            return normalizedPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring(projectRoot.Length + 1)
                : null;
        }

        private static string CombineAssetPath(string root, string relativePath)
            => $"{NormalizePath(root).TrimEnd('/')}/{NormalizeRelativePath(relativePath)}";

        private static string NormalizeRelativePath(string path)
            => NormalizePath(path).Trim('/');

        private static string NormalizePath(string path)
            => (path ?? string.Empty).Replace("\\", "/");
    }
}
