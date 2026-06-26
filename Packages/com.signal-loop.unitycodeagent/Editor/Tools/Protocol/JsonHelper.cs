using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace SignalLoop.UnityCodeAgent.Tools.Protocol
{
    /// <summary>
    /// JSON serialization utilities using Newtonsoft.Json
    /// </summary>
    public static class JsonHelper
    {
        public static JToken ParseElement(string json)
        {
            return JToken.Parse(json);
        }

        public static bool TryGetProperty(this JToken element, string propertyName, out JToken value)
        {
            if (element is JObject jsonObject)
            {
                return jsonObject.TryGetValue(propertyName, out value);
            }

            value = default;
            return false;
        }

        public static string GetStringOrDefault(this JToken element, string propertyName, string defaultValue = null)
        {
            if (element.TryGetProperty(propertyName, out JToken prop) && prop.Type == JTokenType.String)
            {
                return prop.Value<string>();
            }

            return defaultValue;
        }

        public static int GetIntOrDefault(this JToken element, string propertyName, int defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out JToken prop) && prop.Type == JTokenType.Integer)
            {
                return prop.Value<int>();
            }

            return defaultValue;
        }
    }
}
