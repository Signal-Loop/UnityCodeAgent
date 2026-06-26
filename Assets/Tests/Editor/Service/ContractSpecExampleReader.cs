#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SignalLoop.UnityCodeAgent.Service
{
    public static class ContractSpecExampleReader
    {
        private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static readonly string OpenApiPath = Path.Combine(ProjectRoot, "contracts", "openapi", "agent-service.openapi.yaml");
        private static readonly string AsyncApiPath = Path.Combine(ProjectRoot, "contracts", "asyncapi", "agent-service-events.asyncapi.yaml");

        public static string ReadOpenApiSchemaExampleAsJson(string schemaName)
        {
            var lines = File.ReadAllLines(OpenApiPath);
            var schemaIndex = FindLineIndex(lines, schemaName + ":");
            var schemaIndent = CountIndent(lines[schemaIndex]);
            var exampleIndex = FindLineIndex(lines, "example:", schemaIndex + 1, schemaIndent + 2, schemaIndent + 6);
            var exampleIndent = CountIndent(lines[exampleIndex]);
            var exampleLines = CollectIndentedBlock(lines, exampleIndex, exampleIndent);
            return ParseYamlObject(exampleLines, exampleIndent).ToString(Formatting.None);
        }

        public static string ReadAsyncApiEnvelopeExampleAsJson()
        {
            var lines = File.ReadAllLines(AsyncApiPath);
            var schemaIndex = FindLineIndex(lines, "AgentServiceEventEnvelope:");
            var schemaIndent = CountIndent(lines[schemaIndex]);
            var examplesIndex = FindLineIndex(lines, "examples:", schemaIndex + 1, schemaIndent + 2, schemaIndent + 6);
            var examplesIndent = CountIndent(lines[examplesIndex]);
            var rawExampleLines = CollectIndentedBlock(lines, examplesIndex, examplesIndent);
            if (rawExampleLines.Count == 0 || !rawExampleLines[0].TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The AsyncAPI event example is missing or malformed.");
            }

            rawExampleLines[0] = rawExampleLines[0].Replace("- ", string.Empty);
            return ParseYamlObject(rawExampleLines, examplesIndent + 1).ToString(Formatting.None);
        }

        private static int FindLineIndex(IReadOnlyList<string> lines, string trimmedLine, int startIndex = 0, int minIndent = -1, int maxIndent = int.MaxValue)
        {
            for (var index = startIndex; index < lines.Count; index++)
            {
                var line = lines[index];
                var indent = CountIndent(line);
                if (indent < minIndent || indent > maxIndent)
                {
                    continue;
                }

                if (string.Equals(line.Trim(), trimmedLine, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            throw new InvalidOperationException($"Could not find '{trimmedLine}' in contract spec.");
        }

        private static List<string> CollectIndentedBlock(IReadOnlyList<string> lines, int startIndex, int parentIndent)
        {
            var block = new List<string>();

            for (var index = startIndex + 1; index < lines.Count; index++)
            {
                var line = lines[index];

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (block.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                var indent = CountIndent(line);
                if (indent <= parentIndent)
                {
                    break;
                }

                block.Add(line);
            }

            if (block.Count == 0)
            {
                throw new InvalidOperationException("The requested example block is missing.");
            }

            return block;
        }

        private static JObject ParseYamlObject(IReadOnlyList<string> lines, int parentIndent)
        {
            var root = new JObject();
            var stack = new Stack<(int Indent, JObject Object)>();
            stack.Push((parentIndent, root));

            for (var index = 0; index < lines.Count; index++)
            {
                var originalLine = lines[index];
                var indent = CountIndent(originalLine);
                var line = originalLine.TrimStart();
                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    line = line.Substring(2);
                }

                while (stack.Count > 1 && indent <= stack.Peek().Indent)
                {
                    stack.Pop();
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex < 0)
                {
                    throw new InvalidOperationException($"Malformed example line: {originalLine}");
                }

                var key = line.Substring(0, separatorIndex).Trim();
                var valueText = line.Substring(separatorIndex + 1).Trim();
                var current = stack.Peek().Object;

                if (string.IsNullOrEmpty(valueText))
                {
                    var child = new JObject();
                    current[key] = child;
                    stack.Push((indent, child));
                    continue;
                }

                current[key] = ParseScalar(valueText);
            }

            return root;
        }

        private static JToken ParseScalar(string valueText)
        {
            if (valueText.StartsWith("[", StringComparison.Ordinal) && valueText.EndsWith("]", StringComparison.Ordinal))
            {
                return JArray.Parse(valueText);
            }

            if (string.Equals(valueText, "null", StringComparison.OrdinalIgnoreCase))
            {
                return JValue.CreateNull();
            }

            if (bool.TryParse(valueText, out var boolValue))
            {
                return new JValue(boolValue);
            }

            if (long.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            {
                return new JValue(longValue);
            }

            if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return new JValue(doubleValue);
            }

            if (valueText.Length >= 2 && valueText[0] == '"' && valueText[^1] == '"')
            {
                return new JValue(JsonConvert.DeserializeObject<string>(valueText));
            }

            if (valueText.Length >= 2 && valueText[0] == '\'' && valueText[^1] == '\'')
            {
                return new JValue(valueText.Substring(1, valueText.Length - 2).Replace("''", "'"));
            }

            return new JValue(valueText);
        }

        private static int CountIndent(string value)
        {
            var count = 0;
            while (count < value.Length && value[count] == ' ')
            {
                count++;
            }

            return count;
        }
    }
}
