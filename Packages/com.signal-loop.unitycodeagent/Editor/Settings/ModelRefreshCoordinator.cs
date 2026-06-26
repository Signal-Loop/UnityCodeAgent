using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SignalLoop.UnityCodeAgent.Contracts;
using UnityEditor;

namespace SignalLoop.UnityCodeAgent.Settings
{
    public sealed class ModelRefreshCoordinator : IDisposable
    {
        private readonly Func<Action<string>, CancellationToken, Task<IReadOnlyList<ModelInfoDto>>> _loadModelsAsync;
        private readonly Action<IReadOnlyList<ModelInfoDto>> _applyModels;
        private readonly Func<Action, CancellationToken, Task> _runOnEditorThreadAsync;
        private readonly Action _repaint;
        private readonly Action<Exception> _onFailure;

        private CancellationTokenSource _refreshCancellation;
        private bool _disposed;

        public ModelRefreshCoordinator(
            Func<Action<string>, CancellationToken, Task<IReadOnlyList<ModelInfoDto>>> loadModelsAsync,
            Action<IReadOnlyList<ModelInfoDto>> applyModels,
            Func<Action, CancellationToken, Task> runOnEditorThreadAsync,
            Action repaint,
            Action<Exception> onFailure = null)
        {
            _loadModelsAsync = loadModelsAsync ?? throw new ArgumentNullException(nameof(loadModelsAsync));
            _applyModels = applyModels ?? throw new ArgumentNullException(nameof(applyModels));
            _runOnEditorThreadAsync = runOnEditorThreadAsync ?? throw new ArgumentNullException(nameof(runOnEditorThreadAsync));
            _repaint = repaint ?? (() => { });
            _onFailure = onFailure ?? (_ => { });
        }

        public bool IsRefreshInProgress { get; private set; }

        public string Message { get; private set; } = string.Empty;

        public MessageType MessageType { get; private set; } = MessageType.Info;

        public Task RefreshTask { get; private set; }

        public bool StartRefresh()
        {
            ThrowIfDisposed();
            if (IsRefreshInProgress)
            {
                return false;
            }

            _refreshCancellation = new CancellationTokenSource();
            IsRefreshInProgress = true;
            SetMessage("Refreshing models...", MessageType.Info);
            RefreshTask = RefreshAsync(_refreshCancellation.Token);
            return true;
        }

        public void Cancel()
        {
            if (_refreshCancellation == null)
            {
                return;
            }

            _refreshCancellation.Cancel();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
        }

        private async Task RefreshAsync(CancellationToken cancellationToken)
        {
            try
            {
                var models = await _loadModelsAsync(ReportProgress, cancellationToken).ConfigureAwait(false);
                await RunOnEditorThreadAsync(() =>
                {
                    _applyModels(models);
                    var count = models?.Count ?? 0;
                    SetMessage(
                        count == 0 ? "The server returned no models." : $"Loaded {count} model(s) from the server.",
                        count == 0 ? MessageType.Warning : MessageType.Info);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _onFailure(exception);
                await RunOnEditorThreadAsync(() => SetMessage(exception.Message, MessageType.Error), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await RunOnEditorThreadAsync(() =>
                {
                    IsRefreshInProgress = false;
                    if (_refreshCancellation != null)
                    {
                        _refreshCancellation.Dispose();
                        _refreshCancellation = null;
                    }

                    _repaint();
                }, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private void ReportProgress(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _ = RunOnEditorThreadAsync(() => SetMessage(message, MessageType.Info), CancellationToken.None);
        }

        private async Task RunOnEditorThreadAsync(Action action, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return;
            }

            await _runOnEditorThreadAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                action();
                _repaint();
            }, cancellationToken).ConfigureAwait(false);
        }

        private void SetMessage(string message, MessageType messageType)
        {
            Message = message ?? string.Empty;
            MessageType = messageType;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ModelRefreshCoordinator));
            }
        }
    }
}
