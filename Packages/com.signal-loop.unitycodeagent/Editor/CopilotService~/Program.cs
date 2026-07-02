using System.Text;
using Newtonsoft.Json;
using UnityCodeCopilot.Service;
using UnityCodeCopilot.Service.Api;
using UnityCodeCopilot.Service.Infrastructure;
using UnityCodeCopilot.Service.Options;
using UnityCodeCopilot.Service.Settings;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddUnityCodeCopilotService(builder.Configuration);

var app = builder.Build();
var runtimeLifecycle = app.Services.GetRequiredService<ServiceRuntimeLifecycle>();

app.Use(async (context, next) =>
{
    var log = context.RequestServices.GetRequiredService<UnityCodeCopilotServiceLogger>();

    try
    {
        await next();
    }
    catch (Exception exception)
    {
        log.Error(
            "UnhandledException",
            "Unhandled request exception.",
            exception,
            ("method", context.Request.Method),
            ("path", context.Request.Path.ToString()));
        throw;
    }
});

app.Lifetime.ApplicationStarted.Register(() => runtimeLifecycle.OnStarted(app.Urls));
app.Lifetime.ApplicationStopping.Register(runtimeLifecycle.OnStopping);

app.MapGet("/health", (ServiceHealth currentHealth) => Results.Content(
    JsonConvert.SerializeObject(new
    {
        state = currentHealth.State,
        detail = currentHealth.Detail,
    }),
    "application/json",
    Encoding.UTF8,
    HealthResponse.GetStatusCode(currentHealth)));

ServiceEndpoints.Map(app);

app.Run();

public partial class Program;
