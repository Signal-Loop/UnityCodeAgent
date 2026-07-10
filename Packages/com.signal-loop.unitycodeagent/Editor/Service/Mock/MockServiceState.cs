using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Logging;

namespace SignalLoop.UnityCodeAgent.Service.Mock
{
    public sealed class MockServiceState
    {
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();
        private long _nextSequenceNumber = 1;
        private int _abortPromptCount;

        public MockServiceState()
        {
        }

        public MockServiceState(long initialSequenceNumber)
        {
            _nextSequenceNumber = initialSequenceNumber;
        }

        public ConcurrentQueue<AgentServiceEventEnvelope> PendingEvents { get; } = new ConcurrentQueue<AgentServiceEventEnvelope>();

        public CancellationTokenSource ActivePromptCts { get; set; }

        public bool IsPromptInFlight { get; set; }

        /// <summary>
        /// Response sequence templates keyed by session ID.
        /// Shared across all MockAgentServiceApiClient instances because AgentService
        /// creates a new client per method call via its factory.
        /// </summary>
        public ConcurrentDictionary<string, List<IReadOnlyList<AgentServiceEventEnvelope>>> ResponseSequences { get; }
            = new ConcurrentDictionary<string, List<IReadOnlyList<AgentServiceEventEnvelope>>>();

        /// <summary>
        /// Tracks the next response sequence index per session.
        /// </summary>
        public ConcurrentDictionary<string, int> SequenceIndices { get; }
            = new ConcurrentDictionary<string, int>();

        public long GetNextSequenceNumber()
            => Interlocked.Increment(ref _nextSequenceNumber);

        public long CurrentSequenceNumber
            => Interlocked.Read(ref _nextSequenceNumber);

        public int AbortPromptCount
            => Volatile.Read(ref _abortPromptCount);

        public void AdvanceSequenceNumberTo(long sequenceNumber)
        {
            long current;
            do
            {
                current = Interlocked.Read(ref _nextSequenceNumber);
                if (current >= sequenceNumber)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _nextSequenceNumber, sequenceNumber, current) != current);
        }

        public void ResetSequenceNumber()
            => Interlocked.Exchange(ref _nextSequenceNumber, 1);

        public void EnqueueEvent(AgentServiceEventEnvelope envelope)
        {
            AdvanceSequenceNumberTo(envelope.SequenceNumber);
            _log.Debug(nameof(MockServiceState), $"EnqueueEvent type={envelope.Type} seq={envelope.SequenceNumber} sessionId={envelope.SessionId}");
            PendingEvents.Enqueue(envelope);
        }

        public bool TryDequeueEvent(out AgentServiceEventEnvelope envelope)
            => PendingEvents.TryDequeue(out envelope);

        public void CancelActivePrompt()
        {
            Interlocked.Increment(ref _abortPromptCount);
            var cts = ActivePromptCts;
            if (cts != null && !cts.IsCancellationRequested)
            {
                _log.Debug(nameof(MockServiceState), "Cancelling active prompt.");
                cts.Cancel();
            }

            ActivePromptCts = null;
            IsPromptInFlight = false;
        }
    }
}
