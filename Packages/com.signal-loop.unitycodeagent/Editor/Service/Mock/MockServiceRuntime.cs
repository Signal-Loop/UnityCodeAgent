using System;
using System.IO;
using System.Threading;
using SignalLoop.UnityCodeAgent.Infrastructure;

namespace SignalLoop.UnityCodeAgent.Service.Mock
{
    public static class MockServiceRuntime
    {
        private static MockServiceState _sharedState = new MockServiceState();
        private static string _streamGenerationId = System.Guid.NewGuid().ToString("N");

        public static MockServiceState SharedState => Volatile.Read(ref _sharedState);

        public static string StreamGenerationId => Volatile.Read(ref _streamGenerationId);

        public static void Reset()
        {
            var previousState = Volatile.Read(ref _sharedState);
            var nextSequenceNumber = previousState.CurrentSequenceNumber;
            var replacementState = new MockServiceState(nextSequenceNumber);
            previousState = Interlocked.Exchange(ref _sharedState, replacementState);
            Interlocked.Exchange(ref _streamGenerationId, System.Guid.NewGuid().ToString("N"));
            previousState.CancelActivePrompt();
        }

        public static void Reset(UnityCodeAgentPaths paths)
        {
            Reset();
            DeleteRuntimeFile(paths?.EndpointManifestPath);
        }

        private static void DeleteRuntimeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
            }
        }
    }
}
