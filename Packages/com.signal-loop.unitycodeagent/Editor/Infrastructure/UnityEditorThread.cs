using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace SignalLoop.UnityCodeAgent.Infrastructure
{
    [InitializeOnLoad]
    internal static class UnityEditorThread
    {
        private static int _mainThreadId;
        private static SynchronizationContext _mainContext;

        static UnityEditorThread()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _mainContext = SynchronizationContext.Current;
        }

        public static Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                return Task.FromResult(action());
            }

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration cancellationRegistration = default;
            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            }

            void CompleteOnMainThread()
            {
                try
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetResult(action());
                    }
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    cancellationRegistration.Dispose();
                }
            }

            var mainContext = _mainContext;
            if (mainContext == null)
            {
                cancellationRegistration.Dispose();
                completion.TrySetException(new InvalidOperationException("Unity editor synchronization context is not available."));
                return completion.Task;
            }

            mainContext.Post(_ => CompleteOnMainThread(), null);
            return completion.Task;
        }
    }
}
