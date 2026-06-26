using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SignalLoop.UnityCodeAgent.Settings;

namespace SignalLoop.UnityCodeAgent.Infrastructure
{
    public static class UnityCodeAgentSkillCatalog
    {
        public static IReadOnlyList<UnityCodeAgentSkillInfo> Discover(string projectRoot, UnityCodeAgentSkillsSettings skillsSettings)
        {
            if (skillsSettings == null)
            {
                throw new ArgumentNullException(nameof(skillsSettings));
            }

            skillsSettings.EnsureDefaults();
            return Discover(projectRoot, skillsSettings.GetEnabledSkillDirectories());
        }

        public static IReadOnlyList<UnityCodeAgentSkillInfo> Discover(string projectRoot, IReadOnlyList<string> skillFolders)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Project root is required.", nameof(projectRoot));
            }

            var discovered = new List<UnityCodeAgentSkillInfo>();
            foreach (var folder in skillFolders ?? Array.Empty<string>())
            {
                var normalizedFolder = UnityCodeAgentPaths.NormalizeProjectRelativePath(folder);
                if (string.IsNullOrWhiteSpace(normalizedFolder))
                {
                    continue;
                }

                var absoluteFolder = NormalizeAbsolutePath(Path.Combine(projectRoot, normalizedFolder));
                if (!Directory.Exists(absoluteFolder))
                {
                    continue;
                }

                foreach (var skillDirectory in Directory.GetDirectories(absoluteFolder))
                {
                    var skillFile = Path.Combine(skillDirectory, "SKILL.md");
                    if (!File.Exists(skillFile))
                    {
                        continue;
                    }

                    var directoryName = Path.GetFileName(NormalizeAbsolutePath(skillDirectory));
                    var name = ReadSkillName(skillFile, directoryName);
                    discovered.Add(new UnityCodeAgentSkillInfo(
                        name,
                        normalizedFolder,
                        UnityCodeAgentPaths.ToProjectRelativePath(projectRoot, skillFile)));
                }
            }

            return discovered
                .GroupBy(skill => skill.Name, StringComparer.Ordinal)
                .Select(group => group.OrderBy(skill => skill.SkillFileProjectRelativePath, StringComparer.OrdinalIgnoreCase).First())
                .OrderBy(skill => skill.Name, StringComparer.Ordinal)
                .ToArray();
        }

        private static string ReadSkillName(string skillFile, string fallback)
        {
            try
            {
                foreach (var line in File.ReadLines(skillFile).Take(40))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = trimmed.Substring("name:".Length).Trim().Trim('"', '\'');
                        return string.IsNullOrWhiteSpace(name) ? fallback : name;
                    }
                }
            }
            catch (IOException)
            {
            }

            return fallback;
        }

        private static string NormalizeAbsolutePath(string path)
            => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
    }

    public readonly struct UnityCodeAgentSkillInfo
    {
        public UnityCodeAgentSkillInfo(string name, string folderProjectRelativePath, string skillFileProjectRelativePath)
        {
            Name = name ?? string.Empty;
            FolderProjectRelativePath = folderProjectRelativePath ?? string.Empty;
            SkillFileProjectRelativePath = skillFileProjectRelativePath ?? string.Empty;
        }

        public string Name { get; }

        public string FolderProjectRelativePath { get; }

        public string SkillFileProjectRelativePath { get; }
    }
}
