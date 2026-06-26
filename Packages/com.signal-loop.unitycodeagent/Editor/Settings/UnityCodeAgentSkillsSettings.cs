using System;
using System.Collections.Generic;
using System.Linq;
using SignalLoop.UnityCodeAgent.Infrastructure;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Settings
{
    [Serializable]
    public sealed class UnityCodeAgentSkillsSettings
    {
        [SerializeField]
        private List<string> _folders = new List<string> { UnityCodeAgentPaths.DefaultSkillsFolder };

        [SerializeField]
        private List<string> _disabledSkills = new List<string>();

        [SerializeField]
        private bool _defaultsInitialized = true;

        public IReadOnlyList<string> Folders => _folders;

        public IReadOnlyList<string> DisabledSkills => _disabledSkills;

        public void EnsureDefaults()
        {
            _folders ??= new List<string>();
            _disabledSkills ??= new List<string>();
            if (!_defaultsInitialized)
            {
                if (_folders.Count == 0)
                {
                    _folders.Add(UnityCodeAgentPaths.DefaultSkillsFolder);
                }

                _defaultsInitialized = true;
            }

            for (var index = 0; index < _folders.Count; index++)
            {
                _folders[index] = UnityCodeAgentPaths.NormalizeProjectRelativePath(_folders[index]);
            }
        }

        public void AddFolder(string projectRelativePath)
        {
            EnsureDefaults();
            var normalized = UnityCodeAgentPaths.NormalizeProjectRelativePath(projectRelativePath);
            if (string.IsNullOrWhiteSpace(normalized)
                || _folders.Any(folder => string.Equals(folder, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _folders.Add(normalized);
        }

        public void RemoveFolderAt(int index)
        {
            EnsureDefaults();
            if (index >= 0 && index < _folders.Count)
            {
                _folders.RemoveAt(index);
            }
        }

        public void RemoveFolder(string projectRelativePath)
        {
            EnsureDefaults();
            var normalized = UnityCodeAgentPaths.NormalizeProjectRelativePath(projectRelativePath);
            var index = _folders.FindIndex(folder => string.Equals(folder, normalized, StringComparison.OrdinalIgnoreCase));
            RemoveFolderAt(index);
        }

        public IReadOnlyList<string> GetEnabledSkillDirectories()
        {
            EnsureDefaults();
            return _folders
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Select(UnityCodeAgentPaths.NormalizeProjectRelativePath)
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyList<string> GetDisabledSkillNames()
        {
            _disabledSkills ??= new List<string>();
            return _disabledSkills
                .Where(skill => !string.IsNullOrWhiteSpace(skill))
                .Select(skill => skill.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        public bool IsSkillEnabled(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName))
            {
                return false;
            }

            _disabledSkills ??= new List<string>();
            return !_disabledSkills.Contains(skillName.Trim());
        }

        public void SetSkillEnabled(string skillName, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(skillName))
            {
                return;
            }

            _disabledSkills ??= new List<string>();
            var normalized = skillName.Trim();
            _disabledSkills.RemoveAll(skill => string.Equals(skill, normalized, StringComparison.Ordinal));
            if (!enabled)
            {
                _disabledSkills.Add(normalized);
            }
        }
    }
}
