using System.Runtime.CompilerServices;
using System.Threading.Channels;
using GitHub.Copilot;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Copilot;

namespace UnityCodeCopilot.Service.Api;

public sealed class EventStreamBroker
{
    private readonly object _sync = new();
    private long _sequenceNumber;
    private readonly int _maxRetainedEvents;
    private readonly Queue<AgentServiceEventEnvelope> _events = new();
    private readonly Dictionary<Guid, Channel<AgentServiceEventEnvelope>> _subscribers = new();

    public EventStreamBroker(int maxRetainedEvents = 4096)
    {
        if (maxRetainedEvents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetainedEvents), "Retention limit must be greater than zero.");
        }

        this._maxRetainedEvents = maxRetainedEvents;
    }

    public long CurrentSequenceNumber => Interlocked.Read(ref _sequenceNumber);

    public void Publish(string sessionId, SessionEvent session)
    {
        var envelope = ServiceEventEnvelopeFactory.Create(
            Interlocked.Increment(ref _sequenceNumber),
            sessionId,
            session);

        if (envelope is null)
        {
            return;
        }

        Publish(envelope);
    }



    public void Publish(string sessionId, AgentEventType type, string content)
    {
        var envelope = ServiceEventEnvelopeFactory.Create(
            Interlocked.Increment(ref _sequenceNumber),
            type,
            sessionId,
            DateTimeOffset.UtcNow,
            content,
            null,
            string.Empty);

        if (envelope is null)
        {
            return;
        }

        Publish(envelope);
    }

    public void Publish(AgentServiceEventEnvelope envelope)
    {
        if (envelope == null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        Channel<AgentServiceEventEnvelope>[] liveSubscribers;
        var normalizedEnvelope = envelope;

        lock (_sync)
        {
            if (normalizedEnvelope.SequenceNumber <= 0)
            {
                normalizedEnvelope = normalizedEnvelope with { SequenceNumber = Interlocked.Increment(ref _sequenceNumber) };
            }
            else if (normalizedEnvelope.SequenceNumber > _sequenceNumber)
            {
                Interlocked.Exchange(ref _sequenceNumber, normalizedEnvelope.SequenceNumber);
            }

            _events.Enqueue(normalizedEnvelope);
            while (_events.Count > _maxRetainedEvents)
            {
                _events.Dequeue();
            }

            liveSubscribers = _subscribers.Values.ToArray();
        }

        foreach (var subscriber in liveSubscribers)
        {
            subscriber.Writer.TryWrite(normalizedEnvelope);
        }
    }

    public EventStreamSubscription Subscribe(long? afterSequenceNumber = null, CancellationToken cancellationToken = default)
    {
        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<AgentServiceEventEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        AgentServiceEventEnvelope[] retainedEvents;

        lock (_sync)
        {
            retainedEvents = afterSequenceNumber.HasValue
                ? _events.Where(envelope => envelope.SequenceNumber > afterSequenceNumber.Value).ToArray()
                : Array.Empty<AgentServiceEventEnvelope>();

            _subscribers.Add(subscriptionId, channel);
        }

        return new EventStreamSubscription(this, subscriptionId, channel, retainedEvents, cancellationToken);
    }

    private void Unsubscribe(Guid subscriptionId, Channel<AgentServiceEventEnvelope> channel)
    {
        lock (_sync)
        {
            _subscribers.Remove(subscriptionId);
        }

        channel.Writer.TryComplete();
    }

    public sealed class EventStreamSubscription : IDisposable
    {
        private readonly EventStreamBroker _broker;
        private readonly Guid _subscriptionId;
        private readonly Channel<AgentServiceEventEnvelope> _channel;
        private readonly IReadOnlyList<AgentServiceEventEnvelope> _retainedEvents;
        private readonly CancellationToken _subscriptionCancellationToken;
        private int _disposed;

        internal EventStreamSubscription(
            EventStreamBroker broker,
            Guid subscriptionId,
            Channel<AgentServiceEventEnvelope> channel,
            IReadOnlyList<AgentServiceEventEnvelope> retainedEvents,
            CancellationToken subscriptionCancellationToken)
        {
            this._broker = broker;
            this._subscriptionId = subscriptionId;
            this._channel = channel;
            this._retainedEvents = retainedEvents;
            this._subscriptionCancellationToken = subscriptionCancellationToken;
        }

        public IReadOnlyList<AgentServiceEventEnvelope> RetainedEvents => _retainedEvents;

        public async IAsyncEnumerable<AgentServiceEventEnvelope> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var linkedCancellationSource = CreateLinkedCancellationSource(cancellationToken, _subscriptionCancellationToken);
            var effectiveCancellationToken = linkedCancellationSource?.Token
                ?? (cancellationToken.CanBeCanceled ? cancellationToken : _subscriptionCancellationToken);

            try
            {
                foreach (var envelope in _retainedEvents)
                {
                    effectiveCancellationToken.ThrowIfCancellationRequested();
                    yield return envelope;
                }

                await foreach (var envelope in _channel.Reader.ReadAllAsync(effectiveCancellationToken))
                {
                    yield return envelope;
                }
            }
            finally
            {
                linkedCancellationSource?.Dispose();
                Dispose();
            }
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            => _channel.Reader.WaitToReadAsync(cancellationToken);

        public bool TryRead(out AgentServiceEventEnvelope envelope)
        {
            var success = _channel.Reader.TryRead(out var bufferedEnvelope);
            envelope = bufferedEnvelope!;
            return success;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _broker.Unsubscribe(_subscriptionId, _channel);
        }

        private static CancellationTokenSource? CreateLinkedCancellationSource(CancellationToken first, CancellationToken second)
        {
            if (first.CanBeCanceled && second.CanBeCanceled)
            {
                return CancellationTokenSource.CreateLinkedTokenSource(first, second);
            }

            return null;
        }
    }
}
