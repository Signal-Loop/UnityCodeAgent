using System;

namespace SignalLoop.UnityCodeAgent.Service
{
    internal static class AgentSessionStatus
    {
        public static bool IsBusy(string status)
        {
            status = Normalize(status);
            return status.Equals("streaming", StringComparison.OrdinalIgnoreCase)
                || status.Equals("queued", StringComparison.OrdinalIgnoreCase)
                || status.Equals("aborting", StringComparison.OrdinalIgnoreCase);
        }

        internal static string Normalize(string status)
        {
            const string prefix = "Status:";
            status = (status ?? string.Empty).Trim();
            return status.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? status.Substring(prefix.Length).Trim()
                : status;
        }
    }
}
