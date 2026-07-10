using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service;
using UnityCodeCopilot.Service.Api;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Options;
using UnityCodeCopilot.Service.Settings;

namespace UnityCodeCopilot.Service.Tests;

public sealed class ServiceNoUnityModeTests
{
    private string? _testRoot;

    [SetUp]
    public void SetUp()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "ServiceNoUnityModeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (!string.IsNullOrWhiteSpace(_testRoot) && Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, true);
        }
    }

    [Test]
    public void Validator_AcceptsNoUnityModeWithoutUnityProcessId()
    {
        var result = new ServiceOptionsValidator().Validate(null, new ServiceOptions
        {
            ProjectRoot = _testRoot!,
            NoUnity = true,
            UnityProcessId = 0,
        });

        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public void Validator_RejectsUnityModeWithoutUnityProcessId()
    {
        var result = new ServiceOptionsValidator().Validate(null, new ServiceOptions
        {
            ProjectRoot = _testRoot!,
            NoUnity = false,
            UnityProcessId = 0,
        });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.Failures, Has.Some.EqualTo("Configuration value 'UnityProcessId' must be greater than 0."));
    }

    [Test]
    public void CommandLineConfiguration_BindsNoUnityLikeOtherServiceOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(new[] { "--NoUnity=true" })
            .Build();

        Assert.That(configuration.GetValue<bool>(nameof(ServiceOptions.NoUnity)), Is.True);
    }

    [Test]
    public async Task ServiceRegistration_OmitsParentMonitorOnlyInNoUnityMode()
    {
        await using var provider = CreateServices(noUnity: true).BuildServiceProvider();

        var hostedServiceTypes = provider.GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        Assert.That(hostedServiceTypes, Has.No.EqualTo(typeof(ParentProcessMonitor)));
        Assert.That(hostedServiceTypes, Has.Some.EqualTo(typeof(ManifestOwnershipMonitor)));
    }

    [Test]
    public async Task ServiceRegistration_KeepsParentMonitorInUnityMode()
    {
        await using var provider = CreateServices(noUnity: false).BuildServiceProvider();

        var hostedServiceTypes = provider.GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        Assert.That(hostedServiceTypes, Has.Some.EqualTo(typeof(ParentProcessMonitor)));
        Assert.That(hostedServiceTypes, Has.Some.EqualTo(typeof(ManifestOwnershipMonitor)));
    }

    [Test]
    public async Task StopEndpoint_ReturnsAcceptedAndStopsApplication_WhenNoUnityModeIsEnabled()
    {
        await using var factory = new StopEndpointApplicationFactory(noUnity: true);
        using var client = factory.CreateClient();
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();

        using var response = await client.PostAsync("/api/service/stop", content: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(await WaitForStopAsync(lifetime, TimeSpan.FromSeconds(5)), Is.True);
    }

    [Test]
    public async Task StopEndpoint_ReturnsUnprocessableEntityAndDoesNotStopApplication_WhenNoUnityModeIsDisabled()
    {
        var specs = ContractSpecExampleCatalog.Load();
        await using var factory = new StopEndpointApplicationFactory(noUnity: false);
        using var client = factory.CreateClient();
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();

        using var response = await client.PostAsync("/api/service/stop", content: null);
        var body = await response.Content.ReadAsStringAsync();
        var error = JsonConvert.DeserializeObject<AgentServiceErrorResponse>(body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(error?.Code, Is.EqualTo(AgentServiceErrorCodes.OperationFailed));
        Assert.That(error?.Message, Does.Contain("no-Unity mode"));
        Assert.That(JToken.Parse(body), Is.EqualTo(JToken.Parse(specs.GetOpenApiResponseExampleJson("/api/service/stop", "post", "422"))));
        Assert.That(lifetime.ApplicationStopping.IsCancellationRequested, Is.False);
    }

    private IServiceCollection CreateServices(bool noUnity)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [nameof(ServiceOptions.ProjectRoot)] = _testRoot!,
                [nameof(ServiceOptions.UnityProcessId)] = noUnity ? "0" : "1",
                [nameof(ServiceOptions.NoUnity)] = noUnity.ToString(),
                [nameof(ServiceOptions.EnableTelemetry)] = "false",
                [nameof(ServiceOptions.LogToFile)] = "false",
                [nameof(ServiceOptions.MinLogLevel)] = UnityCodeCopilotServiceLogger.LogLevel.Off.ToString(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IHostApplicationLifetime, RecordingHostApplicationLifetime>();
        return services.AddUnityCodeCopilotService(configuration);
    }

    private static async Task<bool> WaitForStopAsync(IHostApplicationLifetime lifetime, TimeSpan timeout)
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStopping.Register(() => stopped.TrySetResult());
        if (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            return true;
        }

        return await Task.WhenAny(stopped.Task, Task.Delay(timeout)) == stopped.Task;
    }

    private sealed class StopEndpointApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly bool _noUnity;

        public StopEndpointApplicationFactory(bool noUnity)
            => _noUnity = noUnity;

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey, "Testing");
            builder.UseSetting(nameof(ServiceOptions.ProjectRoot), AppContext.BaseDirectory);
            builder.UseSetting(nameof(ServiceOptions.UnityProcessId), _noUnity ? "0" : "1");
            builder.UseSetting(nameof(ServiceOptions.NoUnity), _noUnity.ToString());
            builder.UseSetting(nameof(ServiceOptions.OrphanTimeoutSeconds), "90");
            builder.UseSetting(nameof(ServiceOptions.EnableTelemetry), "false");
            builder.UseSetting(nameof(ServiceOptions.LogToFile), "false");
            builder.UseSetting(nameof(ServiceOptions.MinLogLevel), UnityCodeCopilotServiceLogger.LogLevel.Off.ToString());

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
            });
        }
    }

    private sealed class RecordingHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
            => _stopping.Cancel();
    }
}
