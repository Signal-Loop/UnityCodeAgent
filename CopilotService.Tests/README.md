dotnet test CopilotService.Tests/UnityCodeCopilot.Service.Tests.csproj --filter "ServiceEventEnvelopeFactoryTests|ListModels_GitHubCopilotAuthFailure|CreateSession_ByokAuthFailure|CopilotSessionManagerTests" --artifacts-path .artifacts/tests/auth-errors

dotnet test CopilotService.Tests\UnityCodeCopilot.Service.Tests.csproj

New-Item -ItemType Directory -Force .artifacts\contracts\openapi, .artifacts\contracts\asyncapi | Out-Null; Copy-Item contracts\openapi\agent-service.openapi.yaml .artifacts\contracts\openapi\agent-service.openapi.yaml -Force; Copy-Item contracts\asyncapi\agent-service-events.asyncapi.yaml .artifacts\contracts\asyncapi\agent-service-events.asyncapi.yaml -Force

dotnet test CopilotService.Tests\UnityCodeCopilot.Service.Tests.csproj --artifacts-path .artifacts\copilot-service-tests -p:UseAppHost=false