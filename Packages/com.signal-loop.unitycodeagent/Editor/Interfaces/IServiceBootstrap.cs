using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Service;
using SignalLoop.UnityCodeAgent.Settings;

namespace SignalLoop.UnityCodeAgent.Interfaces
{
    public interface IServiceBootstrap
    {
        Task<EndpointManifest> ConnectOrStartAsync(UnityContext context);
    }
}
