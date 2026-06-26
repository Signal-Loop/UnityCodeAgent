using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Settings;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class UnityCodeAgentSkillCatalogTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "UnityCodeAgentSkillCatalogTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_projectRoot) && Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        [Test]
        public void Discover_LoadsImmediateSkillDirectoriesOnly()
        {
            WriteSkill("ephemeral-skills/code-review/SKILL.md", "code-review");
            WriteSkill("ephemeral-skills/nested/not-a-direct-skill/SKILL.md", "not-a-direct-skill");
            WriteSkill("other-skills/pdf-processing/SKILL.md", "pdf-processing");

            var skills = UnityCodeAgentSkillCatalog.Discover(_projectRoot, new[] { "ephemeral-skills" });

            Assert.That(skills.Select(skill => skill.Name), Is.EqualTo(new[] { "code-review" }));
            Assert.That(skills[0].FolderProjectRelativePath, Is.EqualTo("ephemeral-skills"));
            Assert.That(skills[0].SkillFileProjectRelativePath, Is.EqualTo("ephemeral-skills/code-review/SKILL.md"));
        }

        [Test]
        public void Discover_UsesFrontmatterNameAndNormalizesConfiguredFolder()
        {
            WriteSkill("custom-skills/data-analysis/SKILL.md", "data-analysis");
            var settings = new UnityCodeAgentSkillsSettings();
            settings.RemoveFolderAt(0);
            settings.AddFolder(@"\custom-skills\");

            var skills = UnityCodeAgentSkillCatalog.Discover(_projectRoot, settings);

            Assert.That(settings.GetEnabledSkillDirectories(), Is.EqualTo(new[] { "custom-skills" }));
            Assert.That(skills.Select(skill => skill.Name), Is.EqualTo(new[] { "data-analysis" }));
        }

        [Test]
        public void Settings_TogglesDisabledSkills()
        {
            var settings = new UnityCodeAgentSkillsSettings();

            settings.SetSkillEnabled("code-review", false);

            Assert.That(settings.IsSkillEnabled("code-review"), Is.False);
            Assert.That(settings.GetDisabledSkillNames(), Is.EqualTo(new[] { "code-review" }));

            settings.SetSkillEnabled("code-review", true);

            Assert.That(settings.IsSkillEnabled("code-review"), Is.True);
            Assert.That(settings.GetDisabledSkillNames(), Is.Empty);
        }

        [Test]
        public void Settings_AllowsRemovingDefaultSkillsFolder()
        {
            var settings = new UnityCodeAgentSkillsSettings();

            settings.RemoveFolderAt(0);

            Assert.That(settings.GetEnabledSkillDirectories(), Is.Empty);
        }

        [Test]
        public void NormalizeProjectRelativePath_RemovesTraversalSegments()
        {
            var normalized = UnityCodeAgentPaths.NormalizeProjectRelativePath(@"./skills\unity/../agent/../../.agents/skills/");

            Assert.That(normalized, Is.EqualTo(".agents/skills"));
        }

        private void WriteSkill(string projectRelativePath, string skillName)
        {
            var path = Path.Combine(_projectRoot, projectRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, $"---\nname: {skillName}\ndescription: Ephemeral skill for tests.\n---\n");
        }
    }
}
