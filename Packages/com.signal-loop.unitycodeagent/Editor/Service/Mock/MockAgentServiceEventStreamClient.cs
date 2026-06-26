using System;
using System.Threading;
using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Interfaces;
using SignalLoop.UnityCodeAgent.Logging;

namespace SignalLoop.UnityCodeAgent.Service.Mock
{
    public sealed class MockAgentServiceEventStreamClient : IAgentServiceEventStreamClient
    {
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();
        private readonly MockServiceState _state;

        public MockAgentServiceEventStreamClient(EndpointManifest manifest)
            : this(manifest, MockServiceRuntime.SharedState)
        {
        }

        public MockAgentServiceEventStreamClient(EndpointManifest manifest, MockServiceState state)
        {
            _ = manifest;
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public async Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken)
        {
            _log.Debug(nameof(MockAgentServiceEventStreamClient), $"StreamEventsAsync begin lastEventId={lastEventId?.ToString() ?? "null"}");

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_state.TryDequeueEvent(out var envelope))
                {
                    // Skip events we already delivered (reconnection scenario)
                    if (lastEventId.HasValue && envelope.SequenceNumber <= lastEventId.Value)
                    {
                        continue;
                    }

                    // Simulate SSE delivery delay
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);

                    _log.Debug(nameof(MockAgentServiceEventStreamClient), $"Delivering event seq={envelope.SequenceNumber} type={envelope.Type} sessionId={envelope.SessionId}");
                    onEvent(envelope);
                }
                else
                {
                    // No events available — wait a bit before checking again
                    try
                    {
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            _log.Debug(nameof(MockAgentServiceEventStreamClient), "StreamEventsAsync ended.");
        }
    }
}
