using System;
using UnityEngine.UIElements;

namespace SignalLoop.UnityCodeAgent.UI
{
    public sealed class ChatProgressIndicator
    {
        public const string ElementName = "progress-indicator";
        public const string DefaultClassName = "chat-progress-indicator--default";
        public const string SpectrumClassPrefix = "chat-progress-indicator--spectrum-";
        private const int SpectrumColorCount = 8;

        private readonly VisualElement _indicator;
        private readonly Random _random = new Random();
        private int _lastSpectrumIndex = -1;

        public ChatProgressIndicator(VisualElement indicator)
        {
            _indicator = indicator ?? throw new ArgumentNullException(nameof(indicator));
            ShowDefault();
        }

        public void Apply(ChatProgressIndicatorCommand command)
        {
            switch (command)
            {
                case ChatProgressIndicatorCommand.Next:
                    ShowNext();
                    break;
                case ChatProgressIndicatorCommand.Default:
                default:
                    ShowDefault();
                    break;
            }
        }

        public void ShowDefault()
        {
            ClearSpectrumClasses();
            _indicator.AddToClassList(DefaultClassName);
        }

        public void ShowNext()
        {
            _indicator.RemoveFromClassList(DefaultClassName);
            ClearSpectrumClasses();
            _lastSpectrumIndex = NextSpectrumIndex();
            _indicator.AddToClassList($"{SpectrumClassPrefix}{_lastSpectrumIndex}");
        }

        private int NextSpectrumIndex()
        {
            var next = _random.Next(SpectrumColorCount);
            if (next == _lastSpectrumIndex)
            {
                next = (next + 1) % SpectrumColorCount;
            }

            return next;
        }

        private void ClearSpectrumClasses()
        {
            for (var index = 0; index < SpectrumColorCount; index++)
            {
                _indicator.RemoveFromClassList($"{SpectrumClassPrefix}{index}");
            }
        }
    }
}
