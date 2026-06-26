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

        private static UnityCodeAgentSettings CreateSettings()
            => ScriptableObject.CreateInstance<UnityCodeAgentSettings>();
    }
}
