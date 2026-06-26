using System;
using System.IO;
using Newtonsoft.Json;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Logging;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class EventStreamCursorStore
    {
        private readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();

        public EventStreamCursor Load(UnityCodeAgentPaths paths)
        {
            if (paths == null || string.IsNullOrWhiteSpace(paths.EventCursorPath) || !File.Exists(paths.EventCursorPath))
            {
                return EventStreamCursor.Empty;
            }

            try
            {
                return JsonConvert.DeserializeObject<EventStreamCursor>(File.ReadAllText(paths.EventCursorPath)) ?? EventStreamCursor.Empty;
            }
            catch (Exception exception) when (exception is IOException || exception is JsonException || exception is UnauthorizedAccessException)
            {
                _log.Warning(nameof(EventStreamCursorStore), $"Failed to read event cursor file. path={paths.EventCursorPath} error={exception.Message}");
                return EventStreamCursor.Empty;
            }
        }

        public void Save(UnityCodeAgentPaths paths, string streamGenerationId, long? lastAcceptedSequenceNumber)
        {
            if (paths == null || string.IsNullOrWhiteSpace(paths.EventCursorPath) || string.IsNullOrWhiteSpace(streamGenerationId))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(paths.RuntimeRoot);
                var cursor = new EventStreamCursor(streamGenerationId, lastAcceptedSequenceNumber, DateTimeOffset.UtcNow);
                File.WriteAllText(paths.EventCursorPath, JsonConvert.SerializeObject(cursor));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _log.Warning(nameof(EventStreamCursorStore), $"Failed to write event cursor file. path={paths.EventCursorPath} error={exception.Message}");
            }
        }
    }

    public sealed record EventStreamCursor(
        string StreamGenerationId,
        long? LastAcceptedSequenceNumber,
        DateTimeOffset UpdatedAtUtc)
    {
        public static EventStreamCursor Empty { get; } = new EventStreamCursor(string.Empty, null, default);
    }
}
