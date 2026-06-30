using System;
using System.Globalization;
using SignalLoop.UnityCodeAgent.Infrastructure;

namespace SignalLoop.UnityCodeAgent.Service
{
    public readonly struct UnityCodeAgentSessionId
    {
        public UnityCodeAgentSessionId(DateTimeOffset timestampUtc, string projectIdentity)
        {
            TimestampUtc = timestampUtc;
            SanitizedProjectRoot = projectIdentity ?? string.Empty;
        }

        public DateTimeOffset TimestampUtc { get; }

        public string SanitizedProjectRoot { get; }
    }

    public static class UnityCodeAgentSessionIds
    {
        public const string Prefix = "UnityCodeAgentSession";
        private const string TimestampFormat = "yyyyMMddHHmmssfff";

        public static string Create(UnityCodeAgentPaths paths, DateTimeOffset timestampUtc)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            return string.IsNullOrWhiteSpace(paths.SanitizedProjectRoot)
                ? $"{Prefix}-{timestampUtc:yyyyMMddHHmmssfff}"
                : $"{Prefix}-{timestampUtc:yyyyMMddHHmmssfff}-{paths.SanitizedProjectRoot}";
        }

        public static bool TryParse(string sessionId, out UnityCodeAgentSessionId parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            var prefix = Prefix + "-";
            if (!sessionId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var timestampStart = prefix.Length;
            if (sessionId.Length <= timestampStart + TimestampFormat.Length || sessionId[timestampStart + TimestampFormat.Length] != '-')
            {
                return false;
            }

            var timestampPart = sessionId.Substring(timestampStart, TimestampFormat.Length);
            if (!DateTime.TryParseExact(
                    timestampPart,
                    TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var timestamp))
            {
                return false;
            }

            var projectIdentity = sessionId.Substring(timestampStart + TimestampFormat.Length + 1);
            if (string.IsNullOrWhiteSpace(projectIdentity))
            {
                return false;
            }

            parsed = new UnityCodeAgentSessionId(new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)), projectIdentity);
            return true;
        }
    }
}
