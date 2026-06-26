using System;
using System.Net;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class AgentServiceApiException : InvalidOperationException
    {
        public AgentServiceApiException(HttpStatusCode statusCode, string message, string errorCode = null)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode ?? string.Empty;
        }

        public HttpStatusCode StatusCode { get; }

        public string ErrorCode { get; }
    }
}
