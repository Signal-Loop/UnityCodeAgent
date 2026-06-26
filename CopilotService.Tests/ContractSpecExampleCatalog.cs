using System.Globalization;
using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Contracts;
using YamlDotNet.Serialization;

namespace UnityCodeCopilot.Service.Tests;

internal sealed class ContractSpecExampleCatalog
{
    private readonly Dictionary<object, object> _openApi;
    private readonly Dictionary<object, object> _asyncApi;

    private ContractSpecExampleCatalog(Dictionary<object, object> openApi, Dictionary<object, object> asyncApi)
    {
        _openApi = openApi;
        _asyncApi = asyncApi;
    }

    public static ContractSpecExampleCatalog Load()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var deserializer = new DeserializerBuilder().Build();
        var openApi = deserializer.Deserialize<Dictionary<object, object>>(File.ReadAllText(Path.Combine(root, "contracts", "openapi", "agent-service.openapi.yaml")));
        var asyncApi = deserializer.Deserialize<Dictionary<object, object>>(File.ReadAllText(Path.Combine(root, "contracts", "asyncapi", "agent-service-events.asyncapi.yaml")));
        return new ContractSpecExampleCatalog(openApi, asyncApi);
    }

    public string GetOpenApiSchemaExampleJson(string schemaName)
        => SerializeToJson(GetRequiredMap(GetRequiredMap(GetRequiredMap(_openApi, "components"), "schemas"), schemaName)["example"]);

    public string GetOpenApiResponseExampleJson(string path, string method, string statusCode)
        => SerializeToJson(GetRequiredMap(GetRequiredMap(GetRequiredMap(GetRequiredMap(GetRequiredMap(GetRequiredMap(_openApi, "paths"), path), method), "responses"), statusCode), "content")["application/json"].AsMap()["example"]);

    public string GetAsyncApiEnvelopeExampleJson()
    {
        var examples = (List<object>)GetRequiredMap(GetRequiredMap(GetRequiredMap(_asyncApi, "components"), "schemas"), "AgentServiceEventEnvelope")["examples"];
        return SerializeToJson(examples[0]);
    }

    public AgentServiceEventEnvelope GetAsyncApiEnvelopeExample()
        => Deserialize<AgentServiceEventEnvelope>(GetAsyncApiEnvelopeExampleJson());

    public T Deserialize<T>(string json)
        => JsonConvert.DeserializeObject<T>(json) ?? throw new InvalidOperationException($"Could not deserialize contract example to {typeof(T).Name}.");

    private static Dictionary<object, object> GetRequiredMap(Dictionary<object, object> source, string key)
        => source[key].AsMap();

    private static string SerializeToJson(object value)
        => JsonConvert.SerializeObject(ToPlainObject(value));

    private static object? ToPlainObject(object? value)
    {
        if (value is Dictionary<object, object> dictionary)
        {
            return dictionary.ToDictionary(entry => entry.Key.ToString()!, entry => ToPlainObject(entry.Value));
        }

        if (value is List<object> list)
        {
            return list.Select(ToPlainObject).ToList();
        }

        if (value is string text)
        {
            if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (bool.TryParse(text, out var boolValue))
            {
                return boolValue;
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            {
                return longValue;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return doubleValue;
            }

            return text;
        }

        return value;
    }
}

internal static class ContractSpecExampleCatalogExtensions
{
    public static Dictionary<object, object> AsMap(this object value)
        => value as Dictionary<object, object>
           ?? throw new InvalidOperationException($"Expected a YAML map but got {value.GetType().Name}.");
}
