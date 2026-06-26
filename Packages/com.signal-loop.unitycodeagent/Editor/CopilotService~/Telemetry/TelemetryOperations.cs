namespace UnityCodeCopilot.Service.Telemetry;

internal static class TelemetryOperations
{
    public const string HttpSessionsList = "http.sessions.list";
    public const string HttpModelsList = "http.models.list";
    public const string HttpSessionCreate = "http.session.create";
    public const string HttpSessionOpen = "http.session.open";
    public const string HttpSessionSend = "http.session.send";
    public const string HttpSessionAbort = "http.session.abort";
    public const string HttpToolInvocationResult = "http.tool_invocation.result";
    public const string HttpEventsStream = "http.events.stream";

    public const string ServiceSessionCreate = "service.session.create";
    public const string ServiceSessionOpen = "service.session.open";
    public const string ServiceSessionSnapshot = "service.session.snapshot";
    public const string ServiceSessionsList = "service.sessions.list";
    public const string ServiceSessionSend = "service.session.send";
    public const string ServiceSessionAbort = "service.session.abort";
    public const string ServiceMcpLoadConfig = "service.mcp.load_config";

    public const string SdkClientStart = "sdk.client.start";
    public const string SdkClientStop = "sdk.client.stop";
    public const string SdkModelsList = "sdk.models.list";
    public const string SdkAuthStatus = "sdk.auth.status";
    public const string SdkSessionsList = "sdk.sessions.list";
    public const string SdkSessionCreate = "sdk.session.create";
    public const string SdkSessionResume = "sdk.session.resume";
}
