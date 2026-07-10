using System;
using System.Collections.Concurrent;
using System.Threading;
using SignalLoop.UnityCodeAgent.Infrastructure;
using UnityEngine.UIElements;

namespace SignalLoop.UnityCodeAgent.UI
{
    internal sealed class ChatProgressMessages
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(10);

        private static readonly string[] Messages =
        {
            "Drawing level tiles...",
            "Sorting sprite frames...",
            "Checking hitboxes...",
            "Testing jump height...",
            "Placing enemy patrols...",
            "Fixing tile seams...",
            "Timing attack frames...",
            "Saving checkpoint data...",
            "Loading map chunks...",
            "Updating sprite sheets...",
            "Tuning walk speed...",
            "Testing door triggers...",
            "Placing health pickups...",
            "Checking boss patterns...",
            "Measuring input delay...",
            "Updating collision masks...",
            "Fixing ladder logic...",
            "Setting spawn points...",
            "Testing screen transitions...",
            "Balancing damage values...",
            "Drawing item icons...",
            "Checking parallax layers...",
            "Tuning camera follow...",
            "Fixing menu navigation...",
            "Testing save slots...",
            "Updating animation loops...",
            "Placing secret rooms...",
            "Checking weapon range...",
            "Tuning knockback...",
            "Fixing moving platforms...",
            "Testing pause behavior...",
            "Updating sound cues...",
            "Placing coin pickups...",
            "Checking enemy drops...",
            "Fixing tile collisions...",
            "Tuning boss health...",
            "Testing fall damage...",
            "Updating level exits...",
            "Checking frame pacing...",
            "Fixing bullet patterns...",
            "Testing respawn logic...",
            "Drawing background tiles...",
            "Updating HUD counters...",
            "Checking controller input...",
            "Fixing water physics...",
            "Tuning enemy speed...",
            "Testing shop prices...",
            "Updating damage flashes...",
            "Checking map bounds...",
            "Building the ROM...",
        };

        private readonly TextField _progressField;
        private readonly ConcurrentQueue<string> _pendingMessages = new ConcurrentQueue<string>();
        private readonly Random _random = new Random();
        private DateTimeOffset _lastVisibleMessageUtc;
        private DateTimeOffset _lastProgressUtc;
        private int _messageEpoch;

        public ChatProgressMessages(TextField progressField)
        {
            _progressField = progressField;
        }

        public Action<string> ShowProgressMessage => EnqueueFromAnyThread;

        public void Reset()
        {
            _lastVisibleMessageUtc = DateTimeOffset.UtcNow;
            _lastProgressUtc = default;
        }

        public void HandleBusyStateChanged(bool isBusy, bool wasBusy)
        {
            if (!isBusy)
            {
                ClearProgress();
                InvalidatePendingProgress();
                Reset();
                return;
            }

            if (!wasBusy)
            {
                Reset();
            }
        }

        public void NotifyVisibleTranscriptMessage()
        {
            InvalidatePendingProgress();
            _lastVisibleMessageUtc = DateTimeOffset.UtcNow;
        }

        public void DrainPending()
        {
            while (_pendingMessages.TryDequeue(out var content))
            {
                ShowProgress(content);
            }
        }

        public void ReportIfDue(bool isBusy)
        {
            if (!isBusy)
            {
                _lastProgressUtc = default;
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (_lastVisibleMessageUtc == default)
            {
                _lastVisibleMessageUtc = now;
            }

            if (now - _lastVisibleMessageUtc < InitialDelay)
            {
                return;
            }

            if (_lastProgressUtc != default && now - _lastProgressUtc < RefreshDelay)
            {
                return;
            }

            _lastProgressUtc = now;
            EnqueueFromAnyThread(Messages[_random.Next(Messages.Length)]);
        }

        public void ClearPending()
        {
            while (_pendingMessages.TryDequeue(out _))
            {
            }
        }

        public void ClearProgressAndPending()
        {
            InvalidatePendingProgress();
            ClearProgress();
        }

        private void EnqueueFromAnyThread(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            var scheduledEpoch = _messageEpoch;
            _ = UnityEditorThread.RunAsync(
                () =>
                {
                    if (scheduledEpoch != _messageEpoch)
                    {
                        return false;
                    }

                    _pendingMessages.Enqueue(content);
                    DrainPending();
                    return true;
                },
                CancellationToken.None);
        }

        private void InvalidatePendingProgress()
        {
            _messageEpoch++;
            ClearPending();
        }

        private void ShowProgress(string content)
        {
            if (string.IsNullOrWhiteSpace(content) || _progressField == null)
            {
                return;
            }

            _progressField.value = content;
        }

        public void ClearProgress()
        {
            if (_progressField == null)
            {
                return;
            }

            _progressField.value = string.Empty;
        }
    }
}
