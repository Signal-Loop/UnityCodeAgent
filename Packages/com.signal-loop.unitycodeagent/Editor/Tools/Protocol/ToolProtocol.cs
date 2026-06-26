using System;
using System.Collections.Generic;

namespace SignalLoop.UnityCodeAgent.Tools.Protocol
{
    public static class ToolContentTypes
    {
        public const string Text = "text";
        public const string Image = "image";
    }

    [Serializable]
    public sealed class ToolsCallResult
    {
        public List<ContentItem> Content { get; set; } = new List<ContentItem>();

        public bool IsError { get; set; }

        public static ToolsCallResult TextResult(string text, bool isError = false)
            => new ToolsCallResult
            {
                Content = new List<ContentItem> { ContentItem.TextContent(text) },
                IsError = isError
            };

        public static ToolsCallResult ImageResult(string base64Data, string mimeType)
            => new ToolsCallResult
            {
                Content = new List<ContentItem> { ContentItem.ImageContent(base64Data, mimeType) }
            };

        public static ToolsCallResult ErrorResult(string errorMessage)
            => new ToolsCallResult
            {
                Content = new List<ContentItem> { ContentItem.TextContent(errorMessage) },
                IsError = true
            };
    }

    [Serializable]
    public sealed class ContentItem
    {
        public string Type { get; set; }

        public string Text { get; set; }

        public string Data { get; set; }

        public string MimeType { get; set; }

        public static ContentItem TextContent(string text)
            => new ContentItem { Type = ToolContentTypes.Text, Text = text };

        public static ContentItem ImageContent(string base64Data, string mimeType)
            => new ContentItem { Type = ToolContentTypes.Image, Data = base64Data, MimeType = mimeType };
    }
}
