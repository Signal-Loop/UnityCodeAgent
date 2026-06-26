using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;

namespace SignalLoop.UnityCodeAgent.Service.Mock
{
    public sealed class MockAgentServiceApiClient : IAgentServiceApiClient
    {
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();
        private readonly MockServiceState _state;
        private readonly EndpointManifest _manifest;
        private long _nextSequenceNumber = 1000;

        public MockAgentServiceApiClient(EndpointManifest manifest)
            : this(manifest, MockServiceRuntime.SharedState)
        {
        }

        public MockAgentServiceApiClient(EndpointManifest manifest, MockServiceState state)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(CancellationToken cancellationToken)
        {
            _log.Debug(nameof(MockAgentServiceApiClient), $"GetSessionsAsync returning {MockSessionData.SessionSummaries.Count} mock sessions.");
            return Task.FromResult(MockSessionData.SessionSummaries);
        }

        public Task<IReadOnlyList<ModelInfoDto>> GetModelsAsync(ListAgentModelsRequestDto request, CancellationToken cancellationToken)
        {
            var models = new List<ModelInfoDto>
            {
                new ModelInfoDto("gpt-4o", "GPT-4o"),
                new ModelInfoDto("claude-sonnet-4", "Claude Sonnet 4"),
            };
            _log.Debug(nameof(MockAgentServiceApiClient), $"GetModelsAsync returning {models.Count} mock models.");
            return Task.FromResult<IReadOnlyList<ModelInfoDto>>(models);
        }

        public Task<AgentSessionResponseDto> OpenSessionAsync(OpenAgentSessionRequestDto request, CancellationToken cancellationToken)
        {
            _log.Debug(nameof(MockAgentServiceApiClient), $"OpenSessionAsync sessionId={request.SessionId}");
            var messages = MockSessionData.GetMessages(request.SessionId);

            // Pre-register response sequences so the first prompt gets a response
            if (!_state.ResponseSequences.ContainsKey(request.SessionId))
            {
                var sequences = MockSessionData.GetResponseSequences(request.SessionId).ToList();
                if (sequences.Count == 0)
                {
                    sequences.Add(CreateDefaultResponseSequence(request.SessionId));
                }
                _state.ResponseSequences[request.SessionId] = sequences;
                _state.SequenceIndices[request.SessionId] = 0;
            }

            return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ok", messages));
        }

        public Task<AgentSessionResponseDto> CreateSessionAsync(CreateAgentSessionRequestDto request, CancellationToken cancellationToken)
        {
            _log.Debug(nameof(MockAgentServiceApiClient), $"CreateSessionAsync sessionId={request.SessionId}");

            // Register a default response sequence for the new session
            var sequences = new List<IReadOnlyList<AgentServiceEventEnvelope>>
            {
                CreateDefaultResponseSequence(request.SessionId),
            };
            _state.ResponseSequences[request.SessionId] = sequences;
            _state.SequenceIndices[request.SessionId] = 0;

            var welcomeMessages = MockSessionData.CreateWelcomeMessages(request.SessionId);
            return Task.FromResult(new AgentSessionResponseDto(request.SessionId, "ok", welcomeMessages));
        }

        public Task SendPromptAsync(SendAgentPromptRequestDto request, CancellationToken cancellationToken)
        {
            _log.Info(nameof(MockAgentServiceApiClient), $"SendPromptAsync sessionId={request.SessionId} (prompt ignored in mock mode)");

            _state.IsPromptInFlight = true;
            _state.ActivePromptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Get the response sequences for this session
            if (!_state.ResponseSequences.TryGetValue(request.SessionId, out var sequences))
            {
                // Session was opened (not created) — register its sequences
                var sessionSequences = MockSessionData.GetResponseSequences(request.SessionId).ToList();
                if (sessionSequences.Count == 0)
                {
                    sessionSequences.Add(CreateDefaultResponseSequence(request.SessionId));
                }
                sequences = sessionSequences;
                _state.ResponseSequences[request.SessionId] = sequences;
                _state.SequenceIndices[request.SessionId] = 0;
            }

            var index = _state.SequenceIndices.GetOrAdd(request.SessionId, 0);
            var sequenceIndex = index % sequences.Count;
            var responseSequence = sequences[sequenceIndex];
            _state.SequenceIndices[request.SessionId] = index + 1;

            // Enqueue events with updated sequence numbers and a unique stream key per prompt
            var turnKey = $"mock-turn-{request.SessionId}-{index}";
            foreach (var template in responseSequence)
            {
                var seq = _state.GetNextSequenceNumber();
                var envelope = new AgentServiceEventEnvelope(
                    SequenceNumber: seq,
                    SessionId: request.SessionId,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Content: template.Content,
                    StreamKey: turnKey,
                    Type: template.Type,
                    SourceJson: template.SourceJson,
                    IsSubAgentEvent: template.IsSubAgentEvent);
                _state.EnqueueEvent(envelope);
            }

            _state.IsPromptInFlight = false;
            return Task.CompletedTask;
        }

        public Task AbortPromptAsync(AbortAgentPromptRequestDto request, CancellationToken cancellationToken)
        {
            _log.Info(nameof(MockAgentServiceApiClient), $"AbortPromptAsync sessionId={request.SessionId}");
            _state.CancelActivePrompt();
            return Task.CompletedTask;
        }

        public Task SendToolInvocationResultAsync(AgentToolInvocationResultDto request, CancellationToken cancellationToken)
            => Task.CompletedTask;

        private IReadOnlyList<AgentServiceEventEnvelope> CreateDefaultResponseSequence(string sessionId)
        {
            return new[]
            {
                new AgentServiceEventEnvelope(
                    SequenceNumber: Interlocked.Increment(ref _nextSequenceNumber),
                    SessionId: sessionId,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Content: "This is a mock response. The mock service is active.",
                    StreamKey: null,
                    Type: AgentEventType.AssistantMessage,
                    SourceJson: string.Empty,
                    IsSubAgentEvent: false),
                new AgentServiceEventEnvelope(
                    SequenceNumber: Interlocked.Increment(ref _nextSequenceNumber),
                    SessionId: sessionId,
                    TimestampUtc: DateTimeOffset.UtcNow,
                    Content: string.Empty,
                    StreamKey: null,
                    Type: AgentEventType.SessionIdle,
                    SourceJson: string.Empty,
                    IsSubAgentEvent: false),
            };
        }

    }
}
