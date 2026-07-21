using System.Collections.ObjectModel;
using GitHub.Copilot;

namespace UnityCodeCopilot.Service.Copilot;

internal readonly record struct UnityCodeSystemMessageSection(
    SectionOverrideAction Action,
    string Content)
{
    public static UnityCodeSystemMessageSection Empty { get; } =
        new(SectionOverrideAction.Preserve, string.Empty);
}

internal static class UnityCodeSystemMessage
{
    internal static IReadOnlyDictionary<SystemMessageSection, UnityCodeSystemMessageSection> Sections { get; } =
        new ReadOnlyDictionary<SystemMessageSection, UnityCodeSystemMessageSection>(
            new Dictionary<SystemMessageSection, UnityCodeSystemMessageSection>
            {
                [SystemMessageSection.Preamble] = new(
                    SectionOverrideAction.Replace,
                    """
                    You are Unity Code Copilot, an assistant inside Unity Editor build by Signal Loop. You help users in game development tasks.
                    """),
                [SystemMessageSection.Identity] = UnityCodeSystemMessageSection.Empty,
                [SystemMessageSection.Tone] = UnityCodeSystemMessageSection.Empty,
                [SystemMessageSection.ToolEfficiency] = UnityCodeSystemMessageSection.Empty,
                [SystemMessageSection.EnvironmentContext] = UnityCodeSystemMessageSection.Empty,
                [SystemMessageSection.CodeChangeRules] = UnityCodeSystemMessageSection.Empty,
                [SystemMessageSection.Guidelines] = UnityCodeSystemMessageSection.Empty,
                [SystemMessageSection.Safety] = new(
                    SectionOverrideAction.Append,
                    """
                    <unity_safety>
                    * Never fabricate, simulate, or claim an Editor change or test result that did not occur.
                    * Never use `async`/`await`, `Task`, or background threads in executed Unity Editor scripts.
                    * Never use the Editor script tool to edit C# source or plain files, or to run Unity tests.
                    * Never modify a prefab asset through an in-scene instance; load the prefab contents, modify them, save the prefab asset, and unload the contents.
                    </unity_safety>
                    """),
                [SystemMessageSection.ToolInstructions] = new(
                    SectionOverrideAction.Append,
                    """
                    <unity_editor_tools>
                    * Use `execute_csharp_script_in_unity_editor` for live Unity state and Editor automation. Use file tools for C# source and plain project files.
                    * Check `read_unity_console_logs` before Editor scripts or Unity tests that depend on compiled code.
                    * Use `run_unity_tests` for EditMode and PlayMode tests, not the Editor script tool.
                    * Editor scripts are synchronous top-level statements, safe to rerun, use Unity-safe `== null` checks, and report real results.
                    </unity_editor_tools>
                    """),
                [SystemMessageSection.CustomInstructions] = UnityCodeSystemMessageSection.Empty,
                [SystemMessageSection.RuntimeInstructions] = UnityCodeSystemMessageSection.Empty,
                [SystemMessageSection.LastInstructions] = UnityCodeSystemMessageSection.Empty,
            });

    internal static void ApplyTo(SessionConfigBase config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.SystemMessage = CreateConfig();
    }

    internal static SystemMessageConfig CreateConfig()
        => CreateConfig(Sections);

    internal static SystemMessageConfig CreateConfig(
        IReadOnlyDictionary<SystemMessageSection, UnityCodeSystemMessageSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var overrides = sections
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value.Content))
            .ToDictionary(
                entry => entry.Key,
                entry => new SectionOverride
                {
                    Action = entry.Value.Action,
                    Content = NormalizeNewlines(entry.Value.Content),
                });

        return new SystemMessageConfig
        {
            Mode = SystemMessageMode.Customize,
            Sections = overrides,
        };
    }

    private static string NormalizeNewlines(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
