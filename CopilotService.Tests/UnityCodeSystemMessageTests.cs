using System.Reflection;
using GitHub.Copilot;
using NUnit.Framework;
using UnityCodeCopilot.Service.Copilot;

namespace UnityCodeCopilot.Service.Tests;

[TestFixture]
public sealed class UnityCodeSystemMessageTests
{
    [Test]
    public void Sections_ContainsEveryPublicSdkSectionExactlyOnce()
    {
        var sdkSections = typeof(SystemMessageSection)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(SystemMessageSection))
            .Select(property => (SystemMessageSection)property.GetValue(null)!)
            .ToArray();

        Assert.That(UnityCodeSystemMessage.Sections.Keys, Is.EquivalentTo(sdkSections));
        Assert.That(UnityCodeSystemMessage.Sections.Count, Is.EqualTo(sdkSections.Length));
    }

    [Test]
    public void Empty_UsesCanonicalInactiveValue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UnityCodeSystemMessageSection.Empty.Action, Is.EqualTo(SectionOverrideAction.Preserve));
            Assert.That(UnityCodeSystemMessageSection.Empty.Content, Is.Empty);
            Assert.That(UnityCodeSystemMessage.Sections[SystemMessageSection.Identity], Is.EqualTo(UnityCodeSystemMessageSection.Empty));
            Assert.That(UnityCodeSystemMessage.Sections[SystemMessageSection.LastInstructions], Is.EqualTo(UnityCodeSystemMessageSection.Empty));
        });
    }

    [Test]
    public void Sections_DefinesOnlyRequestedActiveContentAndActions()
    {
        var active = UnityCodeSystemMessage.Sections
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value.Content))
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        Assert.That(active.Keys, Is.EquivalentTo(new[]
        {
            SystemMessageSection.Preamble,
            SystemMessageSection.ToolInstructions,
            SystemMessageSection.Safety,
        }));
        Assert.Multiple(() =>
        {
            Assert.That(active[SystemMessageSection.Preamble].Action, Is.EqualTo(SectionOverrideAction.Replace));
            Assert.That(active[SystemMessageSection.Preamble].Content, Does.Contain("Unity Code Copilot"));
            Assert.That(active[SystemMessageSection.Preamble].Content, Does.Contain("live Unity Editor"));
            Assert.That(active[SystemMessageSection.Preamble].Content, Does.Contain("shared project workspace"));
            Assert.That(active[SystemMessageSection.ToolInstructions].Action, Is.EqualTo(SectionOverrideAction.Append));
            Assert.That(active[SystemMessageSection.ToolInstructions].Content, Does.Contain("execute_csharp_script_in_unity_editor"));
            Assert.That(active[SystemMessageSection.ToolInstructions].Content, Does.Contain("read_unity_console_logs"));
            Assert.That(active[SystemMessageSection.ToolInstructions].Content, Does.Contain("run_unity_tests"));
            Assert.That(active[SystemMessageSection.ToolInstructions].Content, Does.Contain("synchronous top-level statements"));
            Assert.That(active[SystemMessageSection.ToolInstructions].Content, Does.Contain("== null"));
            Assert.That(active[SystemMessageSection.Safety].Action, Is.EqualTo(SectionOverrideAction.Append));
            Assert.That(active[SystemMessageSection.Safety].Content, Does.Contain("Never fabricate"));
            Assert.That(active[SystemMessageSection.Safety].Content, Does.Contain("background threads"));
            Assert.That(active[SystemMessageSection.Safety].Content, Does.Contain("in-scene instance"));
        });
    }

    [Test]
    public void CreateConfig_FiltersInactiveContentNormalizesNewlinesAndMapsActionsDirectly()
    {
        var sections = new Dictionary<SystemMessageSection, UnityCodeSystemMessageSection>
        {
            [SystemMessageSection.Identity] = UnityCodeSystemMessageSection.Empty,
            [SystemMessageSection.Tone] = new(SectionOverrideAction.Preserve, " \r\n\t"),
            [SystemMessageSection.Guidelines] = new(SectionOverrideAction.Prepend, "before\r\nafter\r"),
        };

        var config = UnityCodeSystemMessage.CreateConfig(sections);

        Assert.Multiple(() =>
        {
            Assert.That(config.Mode, Is.EqualTo(SystemMessageMode.Customize));
            Assert.That(config.Content, Is.Null);
            Assert.That(config.Sections!.Keys, Is.EquivalentTo(new[] { SystemMessageSection.Guidelines }));
            Assert.That(config.Sections[SystemMessageSection.Guidelines].Action, Is.EqualTo(SectionOverrideAction.Prepend));
            Assert.That(config.Sections[SystemMessageSection.Guidelines].Content, Is.EqualTo("before\nafter\n"));
        });
    }

    [Test]
    public void ApplyTo_ProducesEquivalentIndependentCreateAndResumeConfigs()
    {
        var create = new SessionConfig();
        var resume = new ResumeSessionConfig();

        UnityCodeSystemMessage.ApplyTo(create);
        UnityCodeSystemMessage.ApplyTo(resume);

        var createMessage = create.SystemMessage ?? throw new AssertionException("Create config has no system message.");
        var resumeMessage = resume.SystemMessage ?? throw new AssertionException("Resume config has no system message.");
        var createSections = createMessage.Sections ?? throw new AssertionException("Create config has no section overrides.");
        var resumeSections = resumeMessage.Sections ?? throw new AssertionException("Resume config has no section overrides.");

        Assert.That(createMessage, Is.Not.SameAs(resumeMessage));
        Assert.Multiple(() =>
        {
            Assert.That(createMessage.Mode, Is.EqualTo(SystemMessageMode.Customize));
            Assert.That(resumeMessage.Mode, Is.EqualTo(createMessage.Mode));
            Assert.That(resumeMessage.Content, Is.EqualTo(createMessage.Content));
            Assert.That(resumeSections.Keys, Is.EquivalentTo(createSections.Keys));
        });

        foreach (var section in createSections)
        {
            Assert.Multiple(() =>
            {
                Assert.That(resumeSections[section.Key].Action, Is.EqualTo(section.Value.Action));
                Assert.That(resumeSections[section.Key].Content, Is.EqualTo(section.Value.Content));
            });
        }
    }

    [Test]
    public void CreateConfig_EmitsOnlyThreeOverridesRelativeToGeneratedPrompt()
    {
        var config = UnityCodeSystemMessage.CreateConfig();

        Assert.That(config.Sections, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(config.Sections![SystemMessageSection.Preamble].Action, Is.EqualTo(SectionOverrideAction.Replace));
            Assert.That(config.Sections[SystemMessageSection.ToolInstructions].Action, Is.EqualTo(SectionOverrideAction.Append));
            Assert.That(config.Sections[SystemMessageSection.Safety].Action, Is.EqualTo(SectionOverrideAction.Append));
        });
    }
}
