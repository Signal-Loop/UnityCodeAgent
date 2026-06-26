using System;
using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Logging;

namespace SignalLoop.UnityCodeAgent.Tools.AsyncAwait
{
    public static class TaskExtensions
    {
        private static readonly UnityCodeAgentLogger _log = new UnityCodeAgentLogger();

        public static async void Forget(this Task task, string operationName = null)
        {
            if (task == null)
            {
                return;
            }

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                string prefix = string.IsNullOrWhiteSpace(operationName)
                    ? "[AsyncAwait] Detached task failed"
                    : $"[AsyncAwait] Detached task failed ({operationName})";
                _log.Error(nameof(TaskExtensions), $"{prefix}: {ex}");
            }
        }
    }
}
