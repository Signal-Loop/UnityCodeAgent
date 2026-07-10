using Microsoft.Extensions.Options;
using UnityCodeCopilot.Service.Infrastructure;

namespace UnityCodeCopilot.Service.Options;

public sealed class ServiceOptionsValidator : IValidateOptions<ServiceOptions>
{
    public ValidateOptionsResult Validate(string? name, ServiceOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ProjectRoot))
        {
            failures.Add("Configuration value 'ProjectRoot' is required.");
        }
        else if (!ProjectPaths.TryNormalizeProjectRoot(options.ProjectRoot, out _, out _))
        {
            failures.Add("Configuration value 'ProjectRoot' must point to an existing directory.");
        }

        if (!options.NoUnity && options.UnityProcessId <= 0)
        {
            failures.Add("Configuration value 'UnityProcessId' must be greater than 0.");
        }

        if (options.OrphanTimeoutSeconds <= 0)
        {
            failures.Add("Configuration value 'OrphanTimeoutSeconds' must be greater than 0.");
        }

        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint) &&
            (!Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out var endpointUri) ||
             (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps)))
        {
            failures.Add("Configuration value 'OtlpEndpoint' must be an absolute http or https URL.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
