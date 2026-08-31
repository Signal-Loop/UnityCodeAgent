# Unreliable test ConnectOrStartAsync_FromBackgroundThread_PublishesManifest
- status: Completed
- order: 2600
- goal: Make the background-thread service-start manifest test reliable by preventing build-output file-lock failures, verified by the focused test while preserving normal service startup behavior.
[ERROR] #UnityCodeAgent [AgentService] StartAsync failed elapsedMs=21134
Exception:
System.InvalidOperationException: Service process exited before publishing its endpoint manifest. exitCode=1. Captured output:
stdout:
C:\Program Files\dotnet\sdk\10.0.301\Microsoft.Common.CurrentVersion.targets(5397,5): warning MSB3026: Could not copy "C:\Users\tbory\source\Workspaces\Loop\UnityCodeCopilot\Packages\com.signal-loop.unitycodeagent\Editor\CopilotService~\obj\Debug\net8.0\apphost.exe" to "bin\Debug\net8.0\UnityCodeCopilot.Service.exe". Beginning retry 1 in 1000ms. The process cannot access the file 'C:\Users\tbory\source\Workspaces\Loop\UnityCodeCopilot\Packages\com.signal-loop.unitycodeagent\Editor\CopilotService~\bin\Debug\net8.0\UnityCodeCopilot.Service.exe' because it is being used by another process. The file is locked by: "UnityCodeCopilot.Service (11228)" [C:\Users\tbory\source\Workspaces\Loop\UnityCodeCopilot\Packages\com.signal-loop.unitycodeagent\Editor\CopilotService~\UnityCodeCopilot.Service.csproj]

