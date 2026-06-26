using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Settings;
using SignalLoop.UnityCodeAgent.Tools.Interfaces;
using SignalLoop.UnityCodeAgent.Tools.Protocol;

namespace SignalLoop.UnityCodeAgent.Tools
{
    public sealed class UnityAgentToolRegistry
    {
        private const string DefaultInputSchemaJson = "{\"type\":\"object\",\"properties\":{}}";
        private static readonly Lazy<UnityAgentToolRegistry> SharedInstance =
            new Lazy<UnityAgentToolRegistry>(() => new UnityAgentToolRegistry());

        private readonly Dictionary<string, ITool> _tools = new Dictionary<string, ITool>(StringComparer.Ordinal);

        public UnityAgentToolRegistry()
            : this(CreateDiscoveredTools())
        {
        }

        public UnityAgentToolRegistry(IEnumerable<ITool> tools)
        {
            if (tools == null)
            {
                throw new ArgumentNullException(nameof(tools));
            }

            foreach (var tool in tools)
            {
                ValidateTool(tool);
                if (_tools.ContainsKey(tool.Name))
                {
                    throw new ArgumentException($"A Unity tool named '{tool.Name}' is already registered.", nameof(tools));
                }

                _tools[tool.Name] = tool;
            }
        }

        public static UnityAgentToolRegistry Shared => SharedInstance.Value;

        public IReadOnlyList<AgentToolDefinitionDto> GetDefinitions()
        {
            var definitions = new List<AgentToolDefinitionDto>(_tools.Count);
            foreach (var tool in _tools.Values)
            {
                definitions.Add(new AgentToolDefinitionDto(
                    tool.Name,
                    tool.Description,
                    GetInputSchemaJson(tool)));
            }

            return definitions;
        }

        public async Task<AgentToolInvocationResultDto> ExecuteAsync(AgentToolInvocationRequestDto request, UnityContext context)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.CallId))
            {
                return CreateErrorResult(request, "Unity tool call id was not provided.");
            }

            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                return CreateErrorResult(request, "Unity tool session id was not provided.");
            }

            if (string.IsNullOrWhiteSpace(request.ToolName))
            {
                return CreateErrorResult(request, "Unity tool name was not provided.");
            }

            if (!_tools.TryGetValue(request.ToolName, out var tool))
            {
                return CreateErrorResult(request, $"Unknown Unity tool '{request.ToolName}'.");
            }

            JToken arguments;
            try
            {
                arguments = string.IsNullOrWhiteSpace(request.ArgumentsJson)
                    ? new JObject()
                    : JToken.Parse(request.ArgumentsJson);
            }
            catch (JsonReaderException exception)
            {
                return CreateErrorResult(
                    request,
                    $"Unity tool '{request.ToolName}' received invalid JSON arguments: {exception.Message}");
            }

            var result = await ExecuteToolAsync(tool, arguments, context).ConfigureAwait(false);
            return ToDto(request, result);
        }

        private static IReadOnlyList<ITool> CreateDiscoveredTools()
            => AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(GetLoadableTypes)
                .Where(IsConcreteToolType)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .Select(CreateTool)
                .ToList();

        private static AgentToolInvocationResultDto ToDto(AgentToolInvocationRequestDto request, ToolsCallResult result)
        {
            if (result == null)
            {
                return CreateErrorResult(request, $"Unity tool '{request.ToolName}' returned no result.");
            }

            var textParts = new List<string>();
            var binaryResults = new List<AgentToolBinaryResultDto>();

            if (result.Content != null)
            {
                foreach (var item in result.Content)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(item.Text))
                    {
                        textParts.Add(item.Text);
                    }

                    if (!string.IsNullOrEmpty(item.Data))
                    {
                        binaryResults.Add(new AgentToolBinaryResultDto(
                            item.Data,
                            string.IsNullOrWhiteSpace(item.MimeType) ? "application/octet-stream" : item.MimeType,
                            string.Equals(item.Type, ToolContentTypes.Image, StringComparison.OrdinalIgnoreCase)
                                ? "image"
                                : "resource",
                            null));
                    }
                }
            }

            var textResult = string.Join("\n", textParts);
            return new AgentToolInvocationResultDto(
                request.CallId,
                request.SessionId,
                request.ToolName,
                result.IsError,
                textResult,
                binaryResults.Count == 0 ? null : binaryResults,
                result.IsError ? textResult : null);
        }

        private static AgentToolInvocationResultDto CreateErrorResult(AgentToolInvocationRequestDto request, string message)
            => new AgentToolInvocationResultDto(
                request.CallId,
                request.SessionId,
                request.ToolName,
                true,
                message,
                null,
                message);

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic)
            {
                return Enumerable.Empty<Type>();
            }

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }

        private static bool IsConcreteToolType(Type type)
            => typeof(ITool).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.IsInterface
                && !type.ContainsGenericParameters
                && type.GetConstructor(Type.EmptyTypes) != null;

        private static ITool CreateTool(Type type)
        {
            try
            {
                return (ITool)Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Failed to create Unity tool '{type.FullName}': {exception.Message}", exception);
            }
        }

        private static void ValidateTool(ITool tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                throw new ArgumentException("Registered Unity tool name must be provided.", nameof(tool));
            }

            if (string.IsNullOrWhiteSpace(tool.Description))
            {
                throw new ArgumentException($"Registered Unity tool '{tool.Name}' must provide a description.", nameof(tool));
            }

            if (tool is not IToolSync && tool is not IToolAsync)
            {
                throw new ArgumentException($"Registered Unity tool '{tool.GetType().FullName}' must implement IToolSync or IToolAsync.", nameof(tool));
            }
        }

        private static string GetInputSchemaJson(ITool tool)
            => tool.InputSchema?.ToString(Formatting.None) ?? DefaultInputSchemaJson;

        private static async Task<ToolsCallResult> ExecuteToolAsync(ITool tool, JToken arguments, UnityContext context)
        {
            if (tool is IUnityContextTool contextTool)
            {
                contextTool.SetContext(context);
            }

            if (tool is IToolAsync asyncTool)
            {
                return await asyncTool.ExecuteAsync(arguments).ConfigureAwait(false);
            }

            if (tool is IToolSync syncTool)
            {
                return syncTool.Execute(arguments);
            }

            throw new InvalidOperationException($"Registered Unity tool '{tool.Name}' is not executable.");
        }
    }
}
