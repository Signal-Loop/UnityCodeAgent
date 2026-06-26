using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SignalLoop.UnityCodeAgent.Tools.Protocol;

namespace SignalLoop.UnityCodeAgent.Tools.Interfaces
{
    /// <summary>
    /// Interface for asynchronous local Unity tools.
    /// Implement this interface to create tools that execute asynchronously.
    /// Tools are discovered by UnityAgentToolRegistry from loaded editor assemblies.
    /// </summary>
    public interface IToolAsync : ITool
    {
        /// <summary>
        /// Execute the tool asynchronously with the provided arguments
        /// </summary>
        /// <param name="arguments">Input arguments matching the InputSchema</param>
        /// <returns>Async result containing content items and error status</returns>
        Task<ToolsCallResult> ExecuteAsync(JToken arguments);
    }
}
