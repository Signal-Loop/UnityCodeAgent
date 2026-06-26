using System.Text.Json;
using GitHub.Copilot;
using Microsoft.Extensions.AI;
using SignalLoop.UnityCodeAgent.Contracts;

namespace UnityCodeCopilot.Service.Copilot;

public sealed class UnityAgentToolFunction : AIFunction
{
    private static readonly IReadOnlyDictionary<string, object?> SkipPermissionProperties =
        new Dictionary<string, object?> { ["skip_permission"] = true };

    private readonly AgentToolDefinitionDto _definition;
    private readonly AgentToolInvocationBridge _bridge;
    private readonly JsonElement _jsonSchema;

    public UnityAgentToolFunction(AgentToolDefinitionDto definition, AgentToolInvocationBridge bridge)
    {
        _definition = ValidateDefinition(definition);
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _jsonSchema = ParseJsonSchema(_definition);
    }

    public override string Name => _definition.Name;

    public override string Description => _definition.Description;

    public override JsonElement JsonSchema => _jsonSchema;

    public override IReadOnlyDictionary<string, object?> AdditionalProperties => SkipPermissionProperties;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (arguments.Context == null
            || !arguments.Context.TryGetValue(typeof(ToolInvocation), out var invocationValue)
            || invocationValue is not ToolInvocation invocation)
        {
            throw new InvalidOperationException("Unity tool invocation context was not provided by the Copilot SDK.");
        }

        return await _bridge.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
    }

    private static AgentToolDefinitionDto ValidateDefinition(AgentToolDefinitionDto definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException("Tool name must be provided.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.Description))
        {
            throw new ArgumentException($"Tool '{definition.Name}' must provide a description.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.InputSchemaJson))
        {
            throw new ArgumentException($"Tool '{definition.Name}' must provide an input schema.", nameof(definition));
        }

        return definition;
    }

    private static JsonElement ParseJsonSchema(AgentToolDefinitionDto definition)
    {
        try
        {
            return JsonDocument.Parse(definition.InputSchemaJson).RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"Tool '{definition.Name}' provided an invalid input schema.", nameof(definition), exception);
        }
    }
}
