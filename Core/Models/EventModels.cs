using System;

namespace PlaywrightSmartRecorder.Core.Models
{
    public abstract record UserAction
    {
        public string ActionType { get; init; } = "";
        public DateTime Timestamp { get; init; } = DateTime.Now;
        public string PageAlias { get; init; } = "page"; // Hangi sekmede işlem yapıldı? (page, page1, page2...)
        public string CssSelector { get; init; } = "";
        public bool IsDynamicListElement { get; init; } = false;
    }

    public record ClickAction : UserAction
    {
        public string Tag { get; init; } = "";
        public string ElementId { get; init; } = "";
        public string TextContent { get; init; } = "";
        public string Placeholder { get; init; } = "";
        public string AriaLabel { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public record HoverAction : UserAction
    {
        public string Tag { get; init; } = "";
        public string ElementId { get; init; } = "";
        public string TextContent { get; init; } = "";
        public string Placeholder { get; init; } = "";
        public string AriaLabel { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public record NavigationAction : UserAction
    {
        public string Url { get; init; } = "";
    }

    public record NetworkAction : UserAction
    {
        public string Url { get; init; } = "";
        public string Method { get; init; } = "";
        public int StatusCode { get; init; }
    }

    public record DialogAction : UserAction
    {
        public string DialogType { get; init; } = "";
        public string Message { get; init; } = "";
    }

    public record InputAction : UserAction
    {
        public string Tag { get; init; } = "";
        public string ElementId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Value { get; init; } = "";
        public string Placeholder { get; init; } = "";
        public string AriaLabel { get; init; } = "";
       public string TextContent { get; init; } = "";

    }

    public record SelectAction : UserAction
    {
        public string Tag { get; init; } = "";
        public string ElementId { get; init; } = "";
        public string Name { get; init; } = "";
        public string SelectedValue { get; init; } = "";
        public string AriaLabel { get; init; } = "";
        public string Placeholder { get; init; } = "";
        public string TextContent { get; init; } = "";
    }

    public record AssertAction : UserAction
    {
        public string Tag { get; init; } = "";
        public string ElementId { get; init; } = "";
        public string TextContent { get; init; } = "";
        public string Placeholder { get; init; } = "";
        public string AriaLabel { get; init; } = "";
        public string Name { get; init; } = "";
    }

    public record KeyboardAction : UserAction
    {
        public string Key { get; init; } = "";
        public string Tag { get; init; } = "";
        public string ElementId { get; init; } = "";
        public string Placeholder { get; init; } = "";
        public string AriaLabel { get; init; } = "";
        public string TextContent { get; init; } = "";
        public string Name { get; init; } = "";
    }
}