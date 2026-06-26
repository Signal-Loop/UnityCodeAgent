using System;
using System.Collections.Concurrent;
using System.Threading;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Infrastructure;
using SignalLoop.UnityCodeAgent.Logging;
using SignalLoop.UnityCodeAgent.Settings;
using UnityEngine.UIElements;

namespace SignalLoop.UnityCodeAgent.UI
{
    internal sealed class ChatProgressMessages
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1.5);
        private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(5);

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

        private readonly ScrollView _scrollView;
        private readonly ChatTranscriptScroller _transcriptScroller;
        private readonly UnityCodeAgentLogger _log;
        private readonly string _templateAssetPath;
        private readonly ConcurrentQueue<string> _pendingMessages = new ConcurrentQueue<string>();
        private readonly Random _random = new Random();
        private DateTimeOffset _lastVisibleMessageUtc;
        private DateTimeOffset _lastProgressUtc;
        private int _messageEpoch;

        public ChatProgressMessages(
            ScrollView scrollView,
            ChatTranscriptScroller transcriptScroller,
            UnityCodeAgentLogger log,
            string templateAssetPath)
        {
            _scrollView = scrollView;
            _transcriptScroller = transcriptScroller;
            _log = log;
            _templateAssetPath = templateAssetPath;
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
                RemoveTrailingProgress();
                InvalidatePendingProgress();
                Reset();
                return;
            }

            if (!wasBusy)
            {
                Reset();
            }
        }

        public void PrepareForVisibleMessage()
        {
            InvalidatePendingProgress();
            _lastVisibleMessageUtc = DateTimeOffset.UtcNow;
            RemoveTrailingProgress();
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

        private bool ShowProgress(string content)
        {
            if (string.IsNullOrWhiteSpace(content) || _scrollView == null)
            {
                return false;
            }

            var existingProgress = GetLastProgressField();
            if (existingProgress != null)
            {
                existingProgress.value = content;
                _transcriptScroller?.RequestScrollToBottom(existingProgress);
                return true;
            }

            var template = UnityCodeAgentPackagePaths.LoadAsset<VisualTreeAsset>(_templateAssetPath);
            if (template == null)
            {
                _log.Warning(nameof(ChatProgressMessages), $"No visual tree asset found for progress message path={UnityCodeAgentPackagePaths.ResolveAssetPath(_templateAssetPath)}");
                return false;
            }

            var container = new VisualElement();
            template.CloneTree(container);
            container.SetEnabled(false);

            var messageField = container.Q<TextField>("chat-message");
            if (messageField == null)
            {
                return false;
            }

            messageField.value = content;
            _scrollView.Add(messageField);
            _transcriptScroller?.RequestScrollToBottom(messageField);
            return true;
        }

        public void RemoveTrailingProgress()
        {
            var progressField = GetLastProgressField();
            if (progressField != null)
            {
                progressField.RemoveFromHierarchy();
            }
        }

        private TextField GetLastProgressField()
        {
            if (_scrollView == null || _scrollView.contentContainer.childCount == 0)
            {
                return null;
            }

            var last = _scrollView.contentContainer[_scrollView.contentContainer.childCount - 1] as TextField;
            return last != null && last.ClassListContains("chat-message--progress") ? last : null;
        }
    }
}
