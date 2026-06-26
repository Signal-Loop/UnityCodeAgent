using UnityEngine.UIElements;

namespace SignalLoop.UnityCodeAgent.UI
{
    internal sealed class ChatTranscriptScroller
    {
        private ScrollView _scrollView;
        private bool _isScrollScheduled;
        private VisualElement _pendingScrollTarget;

        public ChatTranscriptScroller(ScrollView scrollView)
        {
            _scrollView = scrollView;
        }

        public void Reset(ScrollView scrollView = null)
        {
            _scrollView = scrollView;
            _isScrollScheduled = false;
            _pendingScrollTarget = null;
        }

        public void RequestScrollToBottom(VisualElement target = null)
        {
            if (_scrollView == null)
            {
                return;
            }

            ScrollToBottom(target);
            RegisterDeferredGeometryScroll(target);
            _pendingScrollTarget = target ?? _pendingScrollTarget;
            if (_isScrollScheduled)
            {
                return;
            }

            _isScrollScheduled = true;
            ScheduleDeferredScrollPasses(3);
        }

        private void RegisterDeferredGeometryScroll(VisualElement target)
        {
            if (target == null)
            {
                return;
            }

            EventCallback<GeometryChangedEvent> callback = null;
            callback = _ =>
            {
                target.UnregisterCallback(callback);
                ScrollToBottom(target);
            };

            target.RegisterCallback(callback);
        }

        private void ScheduleDeferredScrollPasses(int remainingPasses)
        {
            if (_scrollView == null)
            {
                _isScrollScheduled = false;
                _pendingScrollTarget = null;
                return;
            }

            _scrollView.schedule.Execute(() =>
            {
                var target = ResolveScrollTarget();
                ScrollToBottom(target);
                RegisterDeferredGeometryScroll(target);

                if (remainingPasses > 1)
                {
                    ScheduleDeferredScrollPasses(remainingPasses - 1);
                    return;
                }

                _isScrollScheduled = false;
                _pendingScrollTarget = null;
            }).StartingIn(16);
        }

        private VisualElement ResolveScrollTarget()
        {
            if (_pendingScrollTarget != null)
            {
                return _pendingScrollTarget;
            }

            if (_scrollView?.contentContainer != null && _scrollView.contentContainer.childCount > 0)
            {
                return _scrollView.contentContainer[_scrollView.contentContainer.childCount - 1];
            }

            return null;
        }

        private void ScrollToBottom(VisualElement target = null)
        {
            if (_scrollView?.verticalScroller == null)
            {
                return;
            }

            var elementToScrollTo = target;
            if (elementToScrollTo == null && _scrollView.contentContainer.childCount > 0)
            {
                elementToScrollTo = _scrollView.contentContainer[_scrollView.contentContainer.childCount - 1];
            }

            if (elementToScrollTo != null && _scrollView.panel != null && elementToScrollTo.panel != null)
            {
                _scrollView.ScrollTo(elementToScrollTo);
            }

            _scrollView.verticalScroller.value = _scrollView.verticalScroller.highValue;
        }
    }
}