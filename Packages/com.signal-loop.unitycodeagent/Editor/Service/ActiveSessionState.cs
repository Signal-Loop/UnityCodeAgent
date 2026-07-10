namespace SignalLoop.UnityCodeAgent.Service
{
    internal sealed class ActiveSessionState
    {
        public string SessionId { get; private set; } = string.Empty;

        public string RequestSignature { get; private set; } = string.Empty;

        public string Status { get; private set; } = string.Empty;

        public bool IsBusy => AgentSessionStatus.IsBusy(Status);

        public void Set(string sessionId, string requestSignature, string status)
        {
            SessionId = sessionId ?? string.Empty;
            RequestSignature = requestSignature ?? string.Empty;
            SetStatus(status);
        }

        public void SetRequestSignature(string requestSignature)
        {
            RequestSignature = requestSignature ?? string.Empty;
        }

        public void ClearRequestSignature()
        {
            RequestSignature = string.Empty;
        }

        public void SetStatus(string status)
        {
            Status = AgentSessionStatus.Normalize(status);
        }

        public void Clear()
        {
            SessionId = string.Empty;
            RequestSignature = string.Empty;
            Status = string.Empty;
        }
    }
}
