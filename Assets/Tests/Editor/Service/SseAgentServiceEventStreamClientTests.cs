using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;

// Test file goal: verify the Unity SSE client matches the shared event-stream contract.
// Scope: replay header behavior, SSE frame parsing, multiline data handling, and event-envelope deserialization.
// Boundaries: excludes the real service broker, long-lived streaming resilience, and live Copilot event generation.

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class SseAgentServiceEventStreamClientTests
    {
        [Test]
        [Description("Goal: verify the SSE client sends the replay header and parses a retained event from the data frame that matches the AsyncAPI example. Scope: request headers, event-frame parsing, and envelope deserialization only. Boundaries: excludes live broker behavior, reconnection policy, and runtime event production.")]
        public async Task StreamEventsAsync_SendsReplayHeaderAndParsesDataOnlySpecShapedEnvelope()
        {
            var envelopeJson = ContractSpecExampleReader.ReadAsyncApiEnvelopeExampleAsJson();
            using var server = new SseLoopbackServer(
                ": keep-alive\n" +
                "data: " + envelopeJson + "\n\n");
            var client = new SseAgentServiceEventStreamClient(server.CreateManifest());
            var events = new List<AgentServiceEventEnvelope>();

            await client.StreamEventsAsync(events.Add, 7, CancellationToken.None);

            var request = await server.ReceiveSingleRequestAsync();

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(JsonConvert.SerializeObject(events[0]), Is.EqualTo(envelopeJson));
            Assert.That(request.Accept, Is.EqualTo("text/event-stream"));
            Assert.That(request.LastEventId, Is.EqualTo("7"));
        }

        [Test]
        [Description("Goal: verify the SSE client ignores keep-alive comments and combines multiline data frames into one payload. Scope: line-oriented SSE parsing behavior only. Boundaries: excludes spec-example matching, broker replay state, and continuous streaming under load.")]
        public async Task StreamEventsAsync_MultilineData_IgnoresCommentsAndCombinesLines()
        {
            using var server = new SseLoopbackServer(
                ": keep-alive\n" +
                "data: {\"SequenceNumber\":1,\"SessionId\":\"session-1\",\"TimestampUtc\":\"2026-05-28T12:00:00Z\",\n" +
                "data: \"Content\":\"first line\\nsecond line\",\"StreamKey\":null,\"Type\":\"AssistantMessage\",\"SourceJson\":\"{}\"}\n\n");
            var client = new SseAgentServiceEventStreamClient(server.CreateManifest());
            var events = new List<AgentServiceEventEnvelope>();

            await client.StreamEventsAsync(events.Add, null, CancellationToken.None);

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0].Content, Is.EqualTo("first line\nsecond line"));
        }

        private sealed class SseLoopbackServer : IDisposable
        {
            private readonly HttpListener _listener;
            private readonly string _responseText;
            private readonly TaskCompletionSource<CapturedSseRequest> _requestCompletion = new TaskCompletionSource<CapturedSseRequest>();

            public SseLoopbackServer(string responseText)
            {
                _responseText = responseText;
                Port = GetOpenPort();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                _ = ListenAsync();
            }

            public int Port { get; }

            public EndpointManifest CreateManifest()
                => new EndpointManifest { Port = Port, ServiceProcessId = 1234, StartedAtUtc = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero) };

            public Task<CapturedSseRequest> ReceiveSingleRequestAsync()
                => _requestCompletion.Task;

            public void Dispose()
            {
                _listener.Stop();
                _listener.Close();
            }

            private async Task ListenAsync()
            {
                var context = await _listener.GetContextAsync();
                _requestCompletion.TrySetResult(new CapturedSseRequest(
                    context.Request.Headers["Accept"],
                    context.Request.Headers["Last-Event-ID"]));

                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/event-stream";
                var buffer = Encoding.UTF8.GetBytes(_responseText);
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();
            }

            private static int GetOpenPort()
            {
                var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
        }

        private sealed class CapturedSseRequest
        {
            public CapturedSseRequest(string accept, string lastEventId)
            {
                Accept = accept;
                LastEventId = lastEventId;
            }

            public string Accept { get; }

            public string LastEventId { get; }
        }
    }
}