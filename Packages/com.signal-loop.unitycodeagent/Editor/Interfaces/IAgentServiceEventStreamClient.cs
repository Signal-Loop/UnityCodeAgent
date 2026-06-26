using System;
using System.Threading;
using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Contracts;

namespace SignalLoop.UnityCodeAgent.Interfaces
{
    public interface IAgentServiceEventStreamClient
    {
        Task StreamEventsAsync(Action<AgentServiceEventEnvelope> onEvent, long? lastEventId, CancellationToken cancellationToken);
    }
}