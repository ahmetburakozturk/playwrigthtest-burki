using System;

namespace PlaywrightSmartRecorder.Core.Models
{
    // ========================================================================
    // BASE USER ACTION
    // ========================================================================

    public abstract record UserAction
    {
        public string ActionType { get; init; } = "";

        // C# tarafında action'ın işlendiği zaman.
        public DateTime Timestamp { get; init; } = DateTime.Now;

        // Playwright Page alias.
        // Örn: page, page1, page2
        public string PageAlias { get; init; } = "page";

        // Recorder'ın oluşturduğu CSS selector.
        public string CssSelector { get; init; } = "";

        // Tablo/liste içerisindeyse true.
        public bool IsDynamicListElement { get; init; } = false;

        // data-name / data-testid.
        public string CustomTestId { get; init; } = "";

        // Görünür tablo/list satır index'i.
        public int RowIndex { get; init; } = -1;

        // En yakın table id.
        public string ParentTableId { get; init; } = "";

        // ====================================================================
        // BROWSER EVENT CORRELATION
        // ====================================================================

        // Browser'da Date.now() ile alınan timestamp.
        public long ClientTimestamp { get; init; } = 0;

        // Browser tarafında monotonik event sequence.
        public long ClientSequence { get; init; } = 0;
    }

    // ========================================================================
    // TAB OPENED
    // ========================================================================

    public record TabOpenedAction : UserAction
    {
        // Yeni sekme açıldığında bilinen ilk URL.
        //
        // Bazı popup senaryolarında başlangıç URL'si kullanılabilir.
        public string Url { get; init; } = "";
    }

    // ========================================================================
    // TAB ACTIVATED
    // ========================================================================

    public record TabActivatedAction : UserAction
    {
    }

    // ========================================================================
    // EXTRACTION
    // ========================================================================

    public record ExtractAction : UserAction
    {
        public string ExtractedValue { get; init; } = "";

        public string Placeholder { get; init; } = "";

        public string AriaLabel { get; init; } = "";

        public string Name { get; init; } = "";

        public string Tag { get; init; } = "";

        public string ElementId { get; init; } = "";

        // Text / Attribute / Popover
        public string ExtractionMode { get; init; } = "Text";

        // Örn: data-content
        public string AttributeName { get; init; } = "";

        // Örn: Kullanıcı Adı
        public string ExtractionLabel { get; init; } = "";
        public int ExtractionLabelIndex { get; init; } = 0;
        public string ExtractPrefix { get; init; } = "";
        public string ExtractSuffix { get; init; } = "";
    }

    // ========================================================================
    // CLICK
    // ========================================================================

    public record ClickAction : UserAction
    {
        public string Tag { get; init; } = "";

        public string ElementId { get; init; } = "";

        public string TextContent { get; init; } = "";

        public string Placeholder { get; init; } = "";

        public string AriaLabel { get; init; } = "";

        public string Name { get; init; } = "";
    }

    // ========================================================================
    // HOVER
    // ========================================================================

    public record HoverAction : UserAction
    {
        public string Tag { get; init; } = "";

        public string ElementId { get; init; } = "";

        public string TextContent { get; init; } = "";

        public string Placeholder { get; init; } = "";

        public string AriaLabel { get; init; } = "";

        public string Name { get; init; } = "";
    }

    // ========================================================================
    // NAVIGATION
    // ========================================================================

    public record NavigationAction : UserAction
    {
        // Navigation sonrasında oluşan gerçek URL.
        public string Url { get; init; } = "";

        // ================================================================
        // NAVIGATION KIND
        // ================================================================
        //
        // Initial
        //      Recorder'ın ilk açılış navigation'ı.
        //
        // UserAction
        //      Sayfadaki click / Enter / select / form submit vb. sonucu.
        //
        // Manual
        //      Adres çubuğundan yazılan URL gibi manuel navigation.
        //
        // Reload
        //      F5 / Ctrl+R / browser refresh.
        //
        // History
        //      Back / Forward.
        //
        // Automatic
        //      Uygulamanın kendi otomatik navigation/redirect'i.
        //
        // Unknown
        //      Kaynak tespit edilemedi.
        // ================================================================

        public string NavigationKind { get; init; } = "Unknown";

        // Chrome DevTools Protocol transitionType.
        //
        // Örnek:
        // link
        // typed
        // address_bar
        // form_submit
        // generated
        // auto_toplevel
        // reload
        // back_forward
        // other
        public string TransitionType { get; init; } = "";

        // CDP frameRequestedNavigation reason.
        //
        // Örnek:
        // anchorClick
        // formSubmissionGet
        // formSubmissionPost
        // reload
        // scriptInitiated
        public string NavigationReason { get; init; } = "";

        // Kullanıcının Chrome adres çubuğuna yazdığı URL.
        //
        // CDP NavigationEntry.userTypedURL.
        public string UserTypedUrl { get; init; } = "";

        // Navigation'ı oluşturan browser action'ın ClientSequence değeri.
        //
        // 0 ise navigation bağımsızdır.
        public long NavigationTriggerClientSequence { get; init; } = 0;
    }

    // ========================================================================
    // NETWORK
    // ========================================================================

    public record NetworkAction : UserAction
    {
        public string Url { get; init; } = "";

        public string Method { get; init; } = "";

        public int StatusCode { get; init; }
    }

    // ========================================================================
    // DIALOG
    // ========================================================================

    public record DialogAction : UserAction
    {
        public string DialogType { get; init; } = "";

        public string Message { get; init; } = "";
    }

    // ========================================================================
    // INPUT
    // ========================================================================

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

    // ========================================================================
    // SELECT
    // ========================================================================

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

    // ========================================================================
    // ASSERT
    // ========================================================================

    public record AssertAction : UserAction
    {
        public string Tag { get; init; } = "";

        public string ElementId { get; init; } = "";

        public string TextContent { get; init; } = "";

        public string Placeholder { get; init; } = "";

        public string AriaLabel { get; init; } = "";

        public string Name { get; init; } = "";
    }

    // ========================================================================
    // KEYBOARD
    // ========================================================================

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