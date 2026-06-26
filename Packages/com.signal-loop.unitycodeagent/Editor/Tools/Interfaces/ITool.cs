using Newtonsoft.Json.Linq;
using SignalLoop.UnityCodeAgent.Tools.Protocol;

namespace SignalLoop.UnityCodeAgent.Tools.Interfaces
{
    /// <summary>
    /// Common metadata contract for local Unity tools.
    /// Tools are discovered by UnityAgentToolRegistry from loaded editor assemblies.
    /// </summary>
    public interface ITool
    {
        /// <summary>
        /// Unique name identifying this tool
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Human-readable description of what this tool does
        /// </summary>
        string Description { get; }

        /// <summary>
        /// JSON Schema defining the input parameters for this tool
        /// </summary>
        JToken InputSchema { get; }
    }

    /// <summary>
    /// Interface for synchronous local Unity tools.
    /// </summary>
    public interface IToolSync : ITool
    {
        /// <summary>
        /// Execute the tool with the provided arguments
        /// </summary>
        /// <param name="arguments">Input arguments matching the InputSchema</param>
        /// <returns>Result containing content items and error status</returns>
        ToolsCallResult Execute(JToken arguments);
    }
}
