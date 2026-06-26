using System;
using Newtonsoft.Json;

namespace SignalLoop.UnityCodeAgent.Service
{
    [Serializable]
    public sealed class EndpointManifest
    {
        [JsonProperty("version")]
        public int Version { get; set; }

        [JsonProperty("projectRoot")]
        public string ProjectRoot { get; set; } = string.Empty;

        [JsonProperty("projectId")]
        public string ProjectId { get; set; } = string.Empty;

        [JsonProperty("unityProcessId")]
        public int UnityProcessId { get; set; }

        [JsonProperty("serviceProcessId")]
        public int ServiceProcessId { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("startedAtUtc")]
        public DateTimeOffset StartedAtUtc { get; set; }

        [JsonProperty("streamGenerationId")]
        public string StreamGenerationId { get; set; } = string.Empty;
    }
}
