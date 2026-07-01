using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Settings;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class UnityCodeAgentSettingsModelSelectionTests
    {
        [Test]
        [Description("Goal: verify serialized empty model values are not treated as valid selections. Scope: UnityCodeAgentSettings model validation only. Boundaries: excludes inspector rendering and service calls.")]
        public void HasValidSelectedModel_EmptyModelId_ReturnsFalse()
        {
            var settings = CreateSettings();
            settings.AvailableModelsBaseUrl = settings.GetCurrentBaseUrlKey();
            settings.AvailableModels.Add(new ModelInfoDto(string.Empty, string.Empty));
            settings.Model = new ModelInfoDto(string.Empty, string.Empty);

            Assert.That(settings.HasValidSelectedModel(), Is.False);
        }

        [Test]
        [Description("Goal: verify changing BYOK BaseUrl invalidates an existing model selection. Scope: UnityCodeAgentSettings provider provenance only. Boundaries: excludes the custom editor clearing hook.")]
        public void HasValidSelectedModel_BaseUrlChanged_ReturnsFalse()
        {
            var settings = CreateSettings();
            settings.SetAvailableModels(new[] { new ModelInfoDto("gpt-5-mini", "GPT-5 Mini") });
            Assert.That(settings.SelectModel(new ModelInfoDto("gpt-5-mini", "GPT-5 Mini")), Is.True);

            settings.ProviderType = UnityCodeAgentProviderType.Byok;
            settings.ByokBaseUrl = "https://provider.example/v1/";

            Assert.That(settings.HasValidSelectedModel(), Is.False);
            Assert.That(settings.TryCreateProviderConfig(out var provider, out var validationMessage), Is.True);
            Assert.That(provider.Model, Is.Null);
            Assert.That(provider.BaseUrl, Is.EqualTo("https://provider.example/v1"));
            Assert.That(validationMessage, Is.Empty);
        }

        [Test]
        [Description("Goal: verify clearing provider model cache removes both available models and selected model. Scope: UnityCodeAgentSettings cache clearing only. Boundaries: excludes inspector event handling.")]
        public void ClearAvailableModelsAndSelection_ClearsListModelAndBaseUrl()
        {
            var settings = CreateSettings();
            settings.ProviderType = UnityCodeAgentProviderType.Byok;
            settings.ByokBaseUrl = "https://provider.example/v1";
            settings.SetAvailableModels(new[] { new ModelInfoDto("provider-model", "Provider Model") });
            settings.SelectModel(new ModelInfoDto("provider-model", "Provider Model"));

            settings.ClearAvailableModelsAndSelection();

            Assert.That(settings.AvailableModels, Is.Empty);
            Assert.That(settings.Model, Is.Null);
            Assert.That(settings.AvailableModelsBaseUrl, Is.Empty);
        }

        [Test]
        [Description("Goal: verify API key changes do not invalidate a selected model because provider identity is BaseUrl-only. Scope: UnityCodeAgentSettings model provenance only. Boundaries: excludes EditorPrefs persistence.")]
        public void HasValidSelectedModel_ApiKeyChanged_RemainsTrue()
        {
            var settings = CreateSettings();
            var previousApiKey = settings.ByokApiKey;
            try
            {
                settings.ProviderType = UnityCodeAgentProviderType.Byok;
                settings.ByokBaseUrl = "https://provider.example/v1";
                settings.SetAvailableModels(new[] { new ModelInfoDto("provider-model", "Provider Model") });
                Assert.That(settings.SelectModel(new ModelInfoDto("provider-model", "Provider Model")), Is.True);

                settings.ByokApiKey = "new-secret";

                Assert.That(settings.HasValidSelectedModel(), Is.True);
            }
            finally
            {
                settings.ByokApiKey = previousApiKey;
            }
        }

        [Test]
        [Description("Goal: verify refreshing models updates the available list but does not select a model automatically. Scope: UnityCodeAgentSettings model list handling only. Boundaries: excludes live model refresh transport.")]
        public void SetAvailableModels_LeavesModelUnselected()
        {
            var settings = CreateSettings();

            settings.SetAvailableModels(new[] { new ModelInfoDto("gpt-5-mini", "GPT-5 Mini") });

            Assert.That(settings.AvailableModels.Count, Is.EqualTo(1));
            Assert.That(settings.Model, Is.Null);
            Assert.That(settings.AvailableModelsBaseUrl, Is.EqualTo(settings.GetCurrentBaseUrlKey()));
            Assert.That(settings.HasValidSelectedModel(), Is.False);
        }

        [Test]
        [Description("Goal: verify explicit model selection makes provider config use that model. Scope: UnityCodeAgentSettings selection and provider config only. Boundaries: excludes chat client behavior.")]
        public void SelectModel_StampsCurrentModelListAndProviderUsesSelectedModel()
        {
            var settings = CreateSettings();
            settings.ProviderType = UnityCodeAgentProviderType.Byok;
            settings.ByokBaseUrl = "https://provider.example/v1/";
            settings.SetAvailableModels(new[] { new ModelInfoDto("provider-model", "Provider Model") });

            Assert.That(settings.SelectModel(new ModelInfoDto("provider-model", "Provider Model")), Is.True);
            Assert.That(settings.HasValidSelectedModel(), Is.True);
            Assert.That(settings.TryCreateProviderConfig(out var provider, out var validationMessage), Is.True);
            Assert.That(provider.Model, Is.EqualTo("provider-model"));
            Assert.That(provider.ModelName, Is.EqualTo("Provider Model"));
            Assert.That(provider.BaseUrl, Is.EqualTo("https://provider.example/v1"));
            Assert.That(validationMessage, Is.Empty);
        }

        [Test]
        [Description("Goal: verify Copilot provider ignores stale BYOK credentials. Scope: UnityCodeAgentSettings provider config only. Boundaries: excludes inspector rendering and service calls.")]
        public void TryCreateProviderConfig_CopilotWithStaleByokFields_DoesNotSendByok()
        {
            var settings = CreateSettings();
            settings.ProviderType = UnityCodeAgentProviderType.Copilot;
            settings.ByokBaseUrl = "https://provider.example/v1/";
            settings.SetAvailableModels(new[] { new ModelInfoDto("gpt-5-mini", "GPT-5 Mini") });
            Assert.That(settings.SelectModel(new ModelInfoDto("gpt-5-mini", "GPT-5 Mini")), Is.True);

            Assert.That(settings.TryCreateProviderConfig(out var provider, out var validationMessage), Is.True);
            Assert.That(provider.Model, Is.EqualTo("gpt-5-mini"));
            Assert.That(provider.HasByok, Is.False);
            Assert.That(provider.BaseUrl, Is.Null);
            Assert.That(provider.ApiKey, Is.Null);
            Assert.That(settings.GetCurrentBaseUrlKey(), Is.Empty);
            Assert.That(validationMessage, Is.Empty);
        }

        [TestCase("")]
        [TestCase("http://provider.example/v1")]
        [Description("Goal: verify BYOK provider requires a full HTTPS BaseUrl. Scope: UnityCodeAgentSettings provider validation only. Boundaries: excludes inspector rendering and service calls.")]
        public void TryCreateProviderConfig_ByokWithMissingOrInvalidBaseUrl_ReturnsValidationError(string baseUrl)
        {
            var settings = CreateSettings();
            settings.ProviderType = UnityCodeAgentProviderType.Byok;
            settings.ByokBaseUrl = baseUrl;

            Assert.That(settings.TryCreateProviderConfig(out var provider, out var validationMessage), Is.False);
            Assert.That(provider, Is.Null);
            Assert.That(validationMessage, Is.EqualTo("BaseUrl must be a full HTTPS URL."));
        }

        [Test]
        [Description("Goal: verify tool assembly validation accepts loaded non-default assemblies and rejects missing ones. Scope: UnityCodeAgentSettings tool assembly validation only. Boundaries: excludes UI dropdown behavior and persistence.")]
        public void ValidateAssemblyName_ValidatesLoadedGlobalAssemblies()
        {
            var settings = CreateSettings();
            var loadedAssemblyName = "Unity.RenderPipelines.Core.Runtime";

            Assert.That(settings.ValidateAssemblyName(loadedAssemblyName), Is.Empty);
            Assert.That(settings.AddToolAssembly(loadedAssemblyName), Is.True);
            Assert.That(settings.AdditionalToolAssemblyNames, Does.Contain(loadedAssemblyName));
        }

        [Test]
        [Description("Goal: verify tool assembly validation rejects names that are not present in the loaded global assemblies. Scope: UnityCodeAgentSettings tool assembly validation only. Boundaries: excludes UI dropdown behavior and persistence.")]
        public void ValidateAssemblyName_MissingAssembly_ThrowsArgumentException()
        {
            var settings = CreateSettings();

            var exception = Assert.Throws<System.ArgumentException>(() => settings.ValidateAssemblyName("Definitely.Missing.Assembly"));
            Assert.That(exception.Message, Is.EqualTo("Assembly 'Definitely.Missing.Assembly' is not loaded in the current AppDomain."));
            Assert.Throws<System.ArgumentException>(() => settings.AddToolAssembly("Definitely.Missing.Assembly"));
            Assert.That(settings.AdditionalToolAssemblyNames, Is.Empty);
        }

        private static UnityCodeAgentSettings CreateSettings()
            => ScriptableObject.CreateInstance<UnityCodeAgentSettings>();
    }
}
