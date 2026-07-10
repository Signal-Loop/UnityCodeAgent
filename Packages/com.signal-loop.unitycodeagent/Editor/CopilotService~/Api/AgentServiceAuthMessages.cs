using SignalLoop.UnityCodeAgent.Contracts;

namespace UnityCodeCopilot.Service.Api;

public static class AgentServiceAuthMessages
{
    private const string CopilotGuidance = "GitHub Copilot authentication failed. Login to Copilot CLI, then retry.";
    private const string ByokAuthenticationGuidance = "BYOK provider authentication failed. Check the configured ApiKey in UnityCodeAgent settings, then retry.";
    private const string ByokProviderConfigurationGuidance = "BYOK provider request failed. Check the configured BaseUrl and selected model in UnityCodeAgent settings, then retry.";
    private const string UnsupportedImageInputGuidance = "The selected model is not available for image input through the configured provider. Choose a vision-capable model in UnityCodeAgent settings, then retry.";

    public static string ForProvider(ProviderConfigDto? provider)
        => provider?.HasByok == true ? ByokProviderConfigurationGuidance : CopilotGuidance;

    public static string ForByokAuthenticationFailure()
        => ByokAuthenticationGuidance;

    public static string ForByokProviderConfigurationFailure()
        => ByokProviderConfigurationGuidance;

    public static string ForUnsupportedImageInput()
        => UnsupportedImageInputGuidance;
}
