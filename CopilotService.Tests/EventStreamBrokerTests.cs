using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityCodeCopilot.Service.Api;

namespace UnityCodeCopilot.Service.Tests;

public sealed class EventStreamBrokerTests
{
    [Test]
    public void Subscribe_WithCursor_ReplaysOnlyEventsAfterCursor()
    {
        var broker = new EventStreamBroker();
        broker.Publish(CreateEvent(1, "session-1", "first"));
        broker.Publish(CreateEvent(2, "session-2", "second"));
        broker.Publish(CreateEvent(3, "session-1", "third"));

        using var subscription = broker.Subscribe(1);

        Assert.That(subscription.RetainedEvents.Select(e => e.SequenceNumber), Is.EqualTo(new[] { 2L, 3L }));
    }

    [Test]
    public void Subscribe_WithoutCursor_DoesNotReplayRetainedEvents()
    {
        var broker = new EventStreamBroker();
        broker.Publish(CreateEvent(1, "session-1", "first"));
        broker.Publish(CreateEvent(2, "session-2", "second"));

        using var subscription = broker.Subscribe();

        Assert.That(subscription.RetainedEvents, Is.Empty);
    }

    private static AgentServiceEventEnvelope CreateEvent(long sequenceNumber, string sessionId, string content)
        => new(
            sequenceNumber,
            sessionId,
            DateTimeOffset.UtcNow,
            content,
            string.Empty,
            AgentEventType.AssistantMessage,
            string.Empty,
            false);
}
