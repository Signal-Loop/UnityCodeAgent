using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnityCodeCopilot.Service.Infrastructure;

public sealed class EndpointManifestStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly ProjectPaths _paths;

    public EndpointManifestStore(ProjectPaths paths)
    {
        _paths = paths;
    }

    public async Task WriteAsync(int port, int unityProcessId, int serviceProcessId, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.RuntimeRoot);

        var payload = new EndpointManifest(
            1,
            _paths.ProjectRoot,
            CreateProjectId(_paths.ProjectRoot),
            unityProcessId,
            serviceProcessId,
            port,
            DateTimeOffset.UtcNow,
            Guid.NewGuid().ToString("N"));

        var temporaryPath = $"{_paths.EndpointManifestPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(_paths.EndpointManifestPath))
            {
                ReplaceAtomically(temporaryPath, _paths.EndpointManifestPath);
            }
            else
            {
                File.Move(temporaryPath, _paths.EndpointManifestPath);
            }
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    public void DeleteIfPresent()
    {
        if (File.Exists(_paths.EndpointManifestPath))
        {
            File.Delete(_paths.EndpointManifestPath);
        }
    }

    public void DeleteIfOwned(int serviceProcessId)
    {
        var readResult = ReadCurrentIdentity();
        if (readResult.Status != EndpointManifestReadStatus.Found || readResult.Identity.ServiceProcessId != serviceProcessId)
        {
            return;
        }

        File.Delete(_paths.EndpointManifestPath);
    }

    public EndpointManifestIdentityReadResult ReadCurrentIdentity()
    {
        if (!File.Exists(_paths.EndpointManifestPath))
        {
            return EndpointManifestIdentityReadResult.Missing();
        }

        try
        {
            using var stream = new FileStream(
                _paths.EndpointManifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty("serviceProcessId", out var processProperty)
                || !processProperty.TryGetInt32(out var serviceProcessId))
            {
                return EndpointManifestIdentityReadResult.Invalid();
            }

            var projectRoot = document.RootElement.TryGetProperty("projectRoot", out var rootProperty)
                ? rootProperty.GetString() ?? string.Empty
                : string.Empty;
            var projectId = document.RootElement.TryGetProperty("projectId", out var idProperty)
                ? idProperty.GetString() ?? string.Empty
                : string.Empty;

            return EndpointManifestIdentityReadResult.Found(
                new EndpointManifestIdentity(projectRoot, projectId, serviceProcessId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return EndpointManifestIdentityReadResult.Invalid();
        }
    }

    public static string CreateProjectId(string projectRoot)
    {
        var projectName = Path.GetFileName(projectRoot.TrimEnd('/'));
        var slugBuilder = new StringBuilder(projectName.Length);
        var previousWasSeparator = false;

        foreach (var character in projectName)
        {
            if (char.IsLetterOrDigit(character))
            {
                slugBuilder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                slugBuilder.Append('-');
                previousWasSeparator = true;
            }
        }

        var slug = slugBuilder.ToString().Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            slug = "project";
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(projectRoot));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return $"{slug}-{hash[..12]}";
    }

    private static void ReplaceAtomically(string sourcePath, string destinationPath)
    {
        try
        {
            File.Replace(sourcePath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(sourcePath, destinationPath, overwrite: true);
        }
    }

    private sealed record EndpointManifest(
        int version,
        string projectRoot,
        string projectId,
        int unityProcessId,
        int serviceProcessId,
        int port,
        DateTimeOffset startedAtUtc,
        string streamGenerationId);
}

public enum EndpointManifestReadStatus
{
    Found,
    Missing,
    Invalid,
}

public sealed record EndpointManifestIdentity(string ProjectRoot, string ProjectId, int ServiceProcessId);

public sealed record EndpointManifestIdentityReadResult(EndpointManifestReadStatus Status, EndpointManifestIdentity Identity)
{
    private static readonly EndpointManifestIdentity EmptyIdentity = new(string.Empty, string.Empty, 0);

    public static EndpointManifestIdentityReadResult Found(EndpointManifestIdentity identity)
        => new(EndpointManifestReadStatus.Found, identity);

    public static EndpointManifestIdentityReadResult Missing()
        => new(EndpointManifestReadStatus.Missing, EmptyIdentity);

    public static EndpointManifestIdentityReadResult Invalid()
        => new(EndpointManifestReadStatus.Invalid, EmptyIdentity);
}
