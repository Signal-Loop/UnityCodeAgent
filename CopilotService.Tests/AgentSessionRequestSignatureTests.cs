using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;

namespace UnityCodeCopilot.Service.Tests;

public sealed class AgentSessionRequestSignatureTests
{
    [Test]
    public void Create_NormalizesOrderingAndWhitespace()
    {
        var provider = new ProviderConfigDto("gpt-5-mini", " openai ", "https://example.test/v1/", " secret ", " chat ");
        var tools = new[]
        {
            new AgentToolDefinitionDto("tool-b", "second", "{\"type\":\"object\",\"properties\":{\"b\":{\"type\":\"string\"}}}"),
            new AgentToolDefinitionDto("tool-a", "first", "{\"type\":\"object\",\"properties\":{\"a\":{\"type\":\"string\"}}}"),
        };
        var reorderedTools = tools.Reverse().ToArray();

        var signature = AgentSessionRequestSignature.Create(
            provider,
            "C:/work",
            new[] { " skills/b ", "skills/a" },
            new[] { "z-disabled", " a-disabled " },
            tools);

        var reorderedSignature = AgentSessionRequestSignature.Create(
            provider,
            "C:/work",
            new[] { "skills/a", "skills/b" },
            new[] { "a-disabled", "z-disabled" },
            reorderedTools);

        Assert.That(reorderedSignature, Is.EqualTo(signature));
    }

    [Test]
    public void Create_ChangesForSessionBoundInputs()
    {
        var baseline = AgentSessionRequestSignature.Create(
            new ProviderConfigDto("gpt-5-mini", "openai", "https://example.test/v1", "secret", "chat"),
            "C:/work",
            new[] { "skills/a" },
            new[] { "disabled-a" },
            new[] { new AgentToolDefinitionDto("tool-a", "first", "{\"type\":\"object\"}") });

        Assert.That(
            AgentSessionRequestSignature.Create(
                new ProviderConfigDto("gpt-5", "openai", "https://example.test/v1", "secret", "chat"),
                "C:/work",
                new[] { "skills/a" },
                new[] { "disabled-a" },
                new[] { new AgentToolDefinitionDto("tool-a", "first", "{\"type\":\"object\"}") }),
            Is.Not.EqualTo(baseline));

        Assert.That(
            AgentSessionRequestSignature.Create(
                new ProviderConfigDto("gpt-5-mini", "openai", "https://example.test/v1", "secret", "chat"),
                "D:/work",
                new[] { "skills/a" },
                new[] { "disabled-a" },
                new[] { new AgentToolDefinitionDto("tool-a", "first", "{\"type\":\"object\"}") }),
            Is.Not.EqualTo(baseline));

        Assert.That(
            AgentSessionRequestSignature.Create(
                new ProviderConfigDto("gpt-5-mini", "openai", "https://example.test/v1", "secret", "chat"),
                "C:/work",
                new[] { "skills/a" },
                new[] { "disabled-b" },
                new[] { new AgentToolDefinitionDto("tool-a", "first", "{\"type\":\"object\"}") }),
            Is.Not.EqualTo(baseline));

        Assert.That(
            AgentSessionRequestSignature.Create(
                new ProviderConfigDto("gpt-5-mini", "openai", "https://example.test/v1", "secret", "chat"),
                "C:/work",
                new[] { "skills/a" },
                new[] { "disabled-a" },
                new[] { new AgentToolDefinitionDto("tool-a", "first", "{\"type\":\"object\",\"required\":[\"value\"]}") }),
            Is.Not.EqualTo(baseline));
    }
}
