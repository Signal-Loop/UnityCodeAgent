using System;
using System.Collections.Generic;
using System.Linq;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Settings;
using SignalLoop.UnityCodeAgent.Tools;

namespace SignalLoop.UnityCodeAgent.Service
{
    public static class SessionRequestFactory
    {
        public static SessionRequestOptions CreateOptions(UnityContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (string.IsNullOrWhiteSpace(context.Paths.ProjectRoot))
            {
                throw new ArgumentNullException(nameof(context.Paths.ProjectRoot));
            }

            if (!context.IsProviderValid)
            {
                throw new InvalidOperationException(context.ProviderValidationMessage);
            }

            return new SessionRequestOptions(
                context,
                context.Provider,
                context.Paths.ProjectRoot,
                context.SkillDirectories,
                context.DisabledSkills,
                UnityAgentToolRegistry.Shared.GetDefinitions());
        }

        public static OpenAgentSessionRequestDto CreateOpenSessionRequest(SessionRequestOptions options, string sessionId)
            => CreateSessionRequest(options, sessionId, new InfiniteSessionsDto());

        public static CreateAgentSessionRequestDto CreateNewSessionRequest(SessionRequestOptions options, string sessionId)
        {
            var openRequest = CreateSessionRequest(options, sessionId, new InfiniteSessionsDto(false, 0.25, 0.75));
            return new CreateAgentSessionRequestDto(
                openRequest.SessionId,
                openRequest.Provider,
                openRequest.Streaming,
                openRequest.InfiniteSessions,
                openRequest.WorkingDirectory,
                openRequest.SkillDirectories,
                openRequest.DisabledSkills,
                openRequest.Tools);
        }

        private static OpenAgentSessionRequestDto CreateSessionRequest(
            SessionRequestOptions options,
            string sessionId,
            InfiniteSessionsDto infiniteSessions)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentNullException(nameof(sessionId));
            }

            return new OpenAgentSessionRequestDto(
                sessionId,
                options.Provider,
                true,
                infiniteSessions,
                options.WorkingDirectory,
                options.SkillDirectories,
                options.DisabledSkills,
                options.Tools);
        }

        public readonly struct SessionRequestOptions
        {
            public SessionRequestOptions(
                UnityContext context,
                ProviderConfigDto provider,
                string workingDirectory,
                IReadOnlyList<string> skillDirectories,
                IReadOnlyList<string> disabledSkills,
                IReadOnlyList<AgentToolDefinitionDto> tools)
            {
                Context = context ?? throw new ArgumentNullException(nameof(context));
                Provider = provider;
                WorkingDirectory = workingDirectory ?? string.Empty;
                SkillDirectories = Copy(skillDirectories);
                DisabledSkills = Copy(disabledSkills);
                Tools = Copy(tools);
                Signature = AgentSessionRequestSignature.Create(Provider, WorkingDirectory, SkillDirectories, DisabledSkills, Tools);
            }

            public UnityContext Context { get; }

            public ProviderConfigDto Provider { get; }

            public string WorkingDirectory { get; }

            public IReadOnlyList<string> SkillDirectories { get; }

            public IReadOnlyList<string> DisabledSkills { get; }

            public IReadOnlyList<AgentToolDefinitionDto> Tools { get; }

            public string Signature { get; }

            private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
                => values == null || values.Count == 0
                    ? Array.Empty<T>()
                    : values.ToArray();
        }
    }
}
