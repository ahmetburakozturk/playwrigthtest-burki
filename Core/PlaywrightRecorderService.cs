using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using PlaywrightSmartRecorder.Core.Models;
using IBrowser = Microsoft.Playwright.IBrowser;

namespace PlaywrightSmartRecorder.Core
{
    public class PlaywrightRecorderService : IAsyncDisposable
    {
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private IBrowserContext? _context;
        private IPage? _page;

        private int _pageCounter = 0;
        private bool _isFirstPage = true;

        private readonly Dictionary<IPage, string>
            _pageAliases = new();

        // ================================================================
        // CDP SESSION
        // ================================================================

        private readonly Dictionary<IPage, ICDPSession>
            _cdpSessions = new();

        // ================================================================
        // SON BROWSER ACTION
        // ================================================================

        private sealed class BrowserActionMetadata
        {
            public string ActionType { get; init; } = "";

            public long ClientTimestamp { get; init; }

            public long ClientSequence { get; init; }
        }

        private readonly ConcurrentDictionary<IPage, BrowserActionMetadata>
            _lastBrowserActions = new();

        // ================================================================
        // SON CDP NAVIGATION REQUEST
        // ================================================================

        private sealed class NavigationRequestMetadata
        {
            public string Url { get; init; } = "";
            public string Reason { get; init; } = "";
            public long Timestamp { get; init; }
        }

        private readonly ConcurrentDictionary<IPage, NavigationRequestMetadata>
            _lastNavigationRequests = new();

        // ================================================================
        // INITIAL NAVIGATION STATE
        // ================================================================

        private bool _initialMainNavigationRecorded = false;

        // ================================================================
        // EVENTS
        // ================================================================

        public event Action<UserAction>? OnActionRecorded;

        public event Action? OnRecordingStopped;

        // ====================================================================
        // START RECORDING
        // ====================================================================

        public async Task StartRecordingAsync(
            string targetUrl)
        {
            try
            {
                await StopRecordingAsync();

                _pageCounter = 0;
                _isFirstPage = true;
                _initialMainNavigationRecorded = false;

                // ============================================================
                // PORTABLE CHROMIUM
                // ============================================================

                // AppContext.BaseDirectory yerine, Single File (.exe) dostu olan gerçek dizin bulucu:
                string exeFolder = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                
                string browserPath = System.IO.Path.Combine(exeFolder, "browsers");

                Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", browserPath);

                int exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });

                if (exitCode != 0)
                {
                    throw new Exception("Playwright Chromium tarayıcısı kurulamadı veya bulunamadı.");
                }

                _playwright =
                    await Playwright.CreateAsync();

                _browser =
                    await _playwright.Chromium.LaunchAsync(
                        new BrowserTypeLaunchOptions
                        {
                            Headless = false
                        });

                _context =
                    await _browser.NewContextAsync();

                // ============================================================
                // C# <-> BROWSER BRIDGE
                // ============================================================

                await _context.ExposeBindingAsync(
                    "smartRecorderEmit",
                    (BindingSource source, string payload) =>
                    {
                        try
                        {
                            var options =
                                new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true
                                };

                            using var doc =
                                JsonDocument.Parse(
                                    payload);

                            var root =
                                doc.RootElement;

                            string actionType =
                                root.TryGetProperty(
                                    "actionType",
                                    out var actionTypeProperty)
                                    ? (
                                        actionTypeProperty.GetString()
                                        ?? ""
                                      )
                                    : "";

                            // ------------------------------------------------
                            // STOP
                            // ------------------------------------------------

                            if (
                                actionType ==
                                "StopControl")
                            {
                                _ = Task.Run(
                                    async () =>
                                    {
                                        await StopRecordingAsync();

                                        OnRecordingStopped?.Invoke();
                                    });

                                return;
                            }

                            // ------------------------------------------------
                            // SOURCE PAGE
                            // ------------------------------------------------

                            IPage? sourcePage =
                                source.Page;

                            // ------------------------------------------------
                            // CLIENT METADATA
                            // ------------------------------------------------

                            long clientTimestamp =
                                root.TryGetProperty(
                                    "clientTimestamp",
                                    out var timestampProperty)
                                    ? timestampProperty.GetInt64()
                                    : 0;

                            long clientSequence =
                                root.TryGetProperty(
                                    "clientSequence",
                                    out var sequenceProperty)
                                    ? sequenceProperty.GetInt64()
                                    : 0;

                            // ------------------------------------------------
                            // LAST BROWSER ACTION
                            // ------------------------------------------------

                            if (
                                sourcePage != null &&
                                (
                                    actionType ==
                                        "Click" ||
                                    actionType ==
                                        "Input" ||
                                    actionType ==
                                        "Keyboard" ||
                                    actionType ==
                                        "Select"
                                )
                            )
                            {
                                _lastBrowserActions[
                                    sourcePage] =
                                    new BrowserActionMetadata
                                    {
                                        ActionType =
                                            actionType,

                                        ClientTimestamp =
                                            clientTimestamp,

                                        ClientSequence =
                                            clientSequence
                                    };
                            }

                            // ------------------------------------------------
                            // PAGE ALIAS
                            // ------------------------------------------------

                            string pageAlias =
                                "page";

                            if (
                                sourcePage != null &&
                                _pageAliases.TryGetValue(
                                    sourcePage,
                                    out string alias)
                            )
                            {
                                pageAlias =
                                    alias;
                            }
                            else if (
                                _context != null &&
                                sourcePage != null &&
                                _context.Pages.Count > 1 &&
                                sourcePage != _page
                            )
                            {
                                pageAlias =
                                    $"page{_pageCounter}";
                            }

                            // ------------------------------------------------
                            // JSON -> ACTION
                            // ------------------------------------------------

                            UserAction? action =
                                actionType switch
                                {
                                    "Click" =>
                                        JsonSerializer.Deserialize<ClickAction>(
                                            payload,
                                            options),

                                    "Hover" =>
                                        JsonSerializer.Deserialize<HoverAction>(
                                            payload,
                                            options),

                                    "Input" =>
                                        JsonSerializer.Deserialize<InputAction>(
                                            payload,
                                            options),

                                    "Select" =>
                                        JsonSerializer.Deserialize<SelectAction>(
                                            payload,
                                            options),

                                    "Assert" =>
                                        JsonSerializer.Deserialize<AssertAction>(
                                            payload,
                                            options),

                                    "Keyboard" =>
                                        JsonSerializer.Deserialize<KeyboardAction>(
                                            payload,
                                            options),

                                    "Extract" =>
                                        JsonSerializer.Deserialize<ExtractAction>(
                                            payload,
                                            options),

                                    "TabActivated" =>
                                        JsonSerializer.Deserialize<TabActivatedAction>(
                                            payload,
                                            options),

                                    _ =>
                                        null
                                };

                            // ------------------------------------------------
                            // EMIT
                            // ------------------------------------------------

                            if (action != null)
                            {
                                action =
                                    action with
                                    {
                                        PageAlias =
                                            pageAlias
                                    };

                                OnActionRecorded?.Invoke(
                                    action);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(
                                $"[JSON PARSE HATA] {ex.Message}");
                        }
                    });

                // ============================================================
                // INIT SCRIPT
                // ============================================================

                await _context.AddInitScriptAsync("""
                    if (!window.__smartRecorderInitialized) {
                        window.__smartRecorderInitialized = true;

                        // ====================================================
                        // STATE
                        // ====================================================

                        let isPaused = false;
                        let isAssertMode = false;

                        let lastActionTime = 0;
                        let lastActionTarget = null;

                        let hoverTimer = null;
                        let lastHoverTarget = null;
                        let hoverPendingTarget = null;

                        let lastPopoverTrigger = null;

                        // Enter -> synthetic submit click koruması.
                        let lastKeyboardAction = null;

                        // ====================================================
                        // CLIENT SEQUENCE
                        // ====================================================

                        let clientActionSequence = 0;

                        try {
                            clientActionSequence =
                                Number(
                                    sessionStorage.getItem(
                                        '__smartRecorderActionSequence'
                                    ) || '0'
                                );
                        }
                        catch {
                            clientActionSequence = 0;
                        }

                        // ====================================================
                        // CLIENT EVENT METADATA
                        // ====================================================

                        const getClientEventMetadata =
                            (actionType) => {
                                const clientTimestamp =
                                    Date.now();

                                clientActionSequence++;

                                const metadata = {
                                    clientTimestamp:
                                        clientTimestamp,

                                    clientSequence:
                                        clientActionSequence
                                };

                                try {
                                    sessionStorage.setItem(
                                        '__smartRecorderActionSequence',
                                        String(
                                            clientActionSequence
                                        )
                                    );

                                    sessionStorage.setItem(
                                        '__smartRecorderLastAction',
                                        JSON.stringify({
                                            actionType:
                                                actionType,

                                            clientTimestamp:
                                                clientTimestamp,

                                            clientSequence:
                                                clientActionSequence
                                        })
                                    );
                                }
                                catch {
                                }

                                return metadata;
                            };

                        // ====================================================
                        // TEXT
                        // ====================================================

                        const normalizeText =
                            (value) => {
                                return (value || '')
                                    .replace(/\s+/g, ' ')
                                    .trim();
                            };

                        // ====================================================
                        // CSS PATH
                        // ====================================================

                        const getCssPath =
                            (el) => {
                                if (
                                    !(el instanceof Element)
                                ) {
                                    return '';
                                }

                                let path = [];

                                while (
                                    el.nodeType ===
                                    Node.ELEMENT_NODE
                                ) {
                                    let selector =
                                        el.nodeName.toLowerCase();

                                    if (el.id) {
                                        selector +=
                                            '#' +
                                            el.id;

                                        path.unshift(
                                            selector
                                        );

                                        break;
                                    }

                                    let sibling = el;
                                    let nth = 1;

                                    while (
                                        sibling =
                                            sibling.previousElementSibling
                                    ) {
                                        if (
                                            sibling.nodeName.toLowerCase() ===
                                            selector
                                        ) {
                                            nth++;
                                        }
                                    }

                                    if (
                                        nth !== 1
                                    ) {
                                        selector +=
                                            ':nth-of-type(' +
                                            nth +
                                            ')';
                                    }

                                    path.unshift(
                                        selector
                                    );

                                    el =
                                        el.parentNode;
                                }

                                return path.join(
                                    ' > '
                                );
                            };

                        // ====================================================
                        // ELEMENT INFO
                        // ====================================================

                        const getElementInfo =
                            (el) => {
                                let elId =
                                    el.id || '';

                                if (
                                    elId.includes(
                                        '-result-'
                                    ) ||
                                    (
                                        elId.startsWith(
                                            'select2-'
                                        ) &&
                                        elId.includes(
                                            '-result'
                                        )
                                    )
                                ) {
                                    elId = '';
                                }

                                const parentTable =
                                    el.closest
                                        ? el.closest(
                                            'table'
                                        )
                                        : null;

                                const parentTableId =
                                    parentTable &&
                                    parentTable.id
                                        ? parentTable.id
                                        : '';

                                let rowIndex = -1;

                                const rowEl =
                                    el.closest
                                        ? el.closest(
                                            'tr'
                                        )
                                        : null;

                                if (
                                    rowEl &&
                                    rowEl.parentElement
                                ) {
                                    const visibleRows =
                                        Array.from(
                                            rowEl.parentElement.children
                                        ).filter(
                                            ch =>
                                                ch.tagName ===
                                                    'TR' &&
                                                ch.offsetParent !==
                                                    null
                                        );

                                    rowIndex =
                                        visibleRows.indexOf(
                                            rowEl
                                        );
                                }

                                return {
                                    tag:
                                        el.tagName
                                            ? el.tagName.toLowerCase()
                                            : '',

                                    elementId:
                                        elId,

                                    textContent:
                                        normalizeText(
                                            el.innerText ||
                                            ''
                                        ).substring(
                                            0,
                                            100
                                        ),

                                    placeholder:
                                        el.placeholder ||
                                        '',

                                    ariaLabel:
                                        el.getAttribute
                                            ? (
                                                el.getAttribute(
                                                    'aria-label'
                                                ) || ''
                                            )
                                            : '',

                                    name:
                                        el.name ||
                                        '',

                                    cssSelector:
                                        getCssPath(el),

                                    customTestId:
                                        el.getAttribute
                                            ? (
                                                el.getAttribute(
                                                    'data-name'
                                                ) ||
                                                el.getAttribute(
                                                    'data-testid'
                                                ) ||
                                                ''
                                            )
                                            : '',

                                    isDynamicListElement:
                                        el.closest
                                            ? !!el.closest(
                                                'tr'
                                            )
                                            : false,

                                    rowIndex:
                                        rowIndex,

                                    parentTableId:
                                        parentTableId
                                };
                            };

                        // ====================================================
                        // POPOVER SOURCE
                        // ====================================================

                        const findPopoverSource =
                            (
                                element,
                                selectedText
                            ) => {
                                const text =
                                    normalizeText(
                                        selectedText
                                    );

                                if (
                                    element &&
                                    element.closest
                                ) {
                                    const direct =
                                        element.closest(
                                            '[data-toggle="popover"][data-content]'
                                        );

                                    if (
                                        direct
                                    ) {
                                        return direct;
                                    }
                                }

                                if (
                                    lastPopoverTrigger &&
                                    document.documentElement.contains(
                                        lastPopoverTrigger
                                    )
                                ) {
                                    const content =
                                        lastPopoverTrigger.getAttribute(
                                            'data-content'
                                        ) || '';

                                    if (
                                        !text ||
                                        content.includes(
                                            text
                                        )
                                    ) {
                                        return lastPopoverTrigger;
                                    }
                                }

                                const popover =
                                    element &&
                                    element.closest
                                        ? element.closest(
                                            '.popover'
                                        )
                                        : null;

                                if (
                                    popover
                                ) {
                                    if (
                                        popover.id
                                    ) {
                                        const source =
                                            document.querySelector(
                                                `[aria-describedby="${popover.id}"]`
                                            );

                                        if (
                                            source
                                        ) {
                                            return source;
                                        }
                                    }

                                    if (
                                        text
                                    ) {
                                        const candidates =
                                            document.querySelectorAll(
                                                '[data-toggle="popover"][data-content]'
                                            );

                                        let bestCandidate =
                                            null;

                                        let bestScore =
                                            -1;

                                        for (
                                            const candidate
                                            of candidates
                                        ) {
                                            const content =
                                                candidate.getAttribute(
                                                    'data-content'
                                                ) || '';

                                            if (
                                                !content
                                            ) {
                                                continue;
                                            }

                                            const parser =
                                                new DOMParser();

                                            const parsed =
                                                parser.parseFromString(
                                                    content,
                                                    'text/html'
                                                );

                                            const plainText =
                                                normalizeText(
                                                    parsed.body
                                                        ?.innerText ||
                                                    ''
                                                );

                                            if (
                                                !plainText.includes(
                                                    text
                                                )
                                            ) {
                                                continue;
                                            }

                                            let score =
                                                1;

                                            if (
                                                candidate ===
                                                lastPopoverTrigger
                                            ) {
                                                score +=
                                                    100;
                                            }

                                            if (
                                                candidate.offsetParent !==
                                                null
                                            ) {
                                                score += 5;
                                            }

                                            if (
                                                score >
                                                bestScore
                                            ) {
                                                bestScore =
                                                    score;

                                                bestCandidate =
                                                    candidate;
                                            }
                                        }

                                        if (
                                            bestCandidate
                                        ) {
                                            return bestCandidate;
                                        }
                                    }
                                }

                                if (
                                    text
                                ) {
                                    const candidates =
                                        document.querySelectorAll(
                                            '[data-toggle="popover"][data-content]'
                                        );

                                    let bestCandidate =
                                        null;

                                    let bestScore =
                                        -1;

                                    for (
                                        const candidate
                                        of candidates
                                    ) {
                                        const content =
                                            candidate.getAttribute(
                                                'data-content'
                                            ) || '';

                                        if (
                                            !content
                                        ) {
                                            continue;
                                        }

                                        const parser =
                                            new DOMParser();

                                        const parsed =
                                            parser.parseFromString(
                                                content,
                                                'text/html'
                                            );

                                        const plainText =
                                            normalizeText(
                                                parsed.body
                                                    ?.innerText ||
                                                ''
                                            );

                                        if (
                                            !plainText.includes(
                                                text
                                            )
                                        ) {
                                            continue;
                                        }

                                        let score =
                                            1;

                                        if (
                                            candidate ===
                                            lastPopoverTrigger
                                        ) {
                                            score +=
                                                100;
                                        }

                                        if (
                                            candidate.offsetParent !==
                                            null
                                        ) {
                                            score += 5;
                                        }

                                        if (
                                            score >
                                            bestScore
                                        ) {
                                            bestScore =
                                                score;

                                            bestCandidate =
                                                candidate;
                                        }
                                    }

                                    return bestCandidate;
                                }

                                return null;
                            };

                        const getPopoverExtractionInfo = (sourceElement, selectedText) => {
                            if (!sourceElement) return null;

                            const attributeName = sourceElement.hasAttribute('data-content') ? 'data-content' : '';
                            if (!attributeName) return null;

                            const dataContent = sourceElement.getAttribute(attributeName) || '';
                            if (!dataContent) return null;

                            const parser = new DOMParser();
                            const doc = parser.parseFromString(dataContent, 'text/html');
                            const selected = normalizeText(selectedText);
                            const rows = Array.from(doc.querySelectorAll('tr'));
                            
                            let matchedLabel = '';
                            let extMode = '';
                            let matchedLabelIndex = 0; // YENİ: Sıra numarası

                            // 1. Önce DİKEY (Vertical) tabloyu dene
                            for (const row of rows) {
                                const cells = Array.from(row.querySelectorAll('th, td'));
                                if (cells.length < 2) continue;
                                
                                const label = normalizeText(cells[0].textContent || '');
                                const value = normalizeText(cells[1].textContent || '');
                                
                                if (value && (value === selected || value.includes(selected) || selected.includes(value))) {
                                    matchedLabel = label;
                                    extMode = 'PopoverVertical';
                                    
                                    // Aynı etiketten (Kullanıcı Adı) bu satıra kadar kaç tane var sayıyoruz
                                    let count = 0;
                                    for (const r of rows) {
                                        const c = Array.from(r.querySelectorAll('th, td'));
                                        if (c.length > 0 && normalizeText(c[0].textContent || '') === label) {
                                            if (r === row) {
                                                matchedLabelIndex = count;
                                                break;
                                            }
                                            count++;
                                        }
                                    }
                                    break;
                                }
                            }

                            // 2. Bulunamazsa YATAY (Horizontal) tabloyu dene
                            if (!matchedLabel && rows.length > 1) {
                                const headers = Array.from(rows[0].querySelectorAll('th, td')).map(el => normalizeText(el.textContent || ''));
                                for (let i = 1; i < rows.length; i++) {
                                    const cells = Array.from(rows[i].querySelectorAll('th, td'));
                                    for (let j = 0; j < cells.length; j++) {
                                        const value = normalizeText(cells[j].textContent || '');
                                        if (value && (value === selected || value.includes(selected) || selected.includes(value))) {
                                            if (headers[j]) {
                                                matchedLabel = headers[j];
                                                extMode = 'PopoverHorizontal';
                                                matchedLabelIndex = i - 1; // Veri satırı indeksi (ilk veri 0)
                                            }
                                            break;
                                        }
                                    }
                                    if (matchedLabel) break;
                                }
                            }

                            return {
                                attributeName: attributeName,
                                extractionLabel: matchedLabel,
                                extractionMode: matchedLabel ? extMode : 'Attribute',
                                extractionLabelIndex: matchedLabelIndex // YENİ
                            };
                        };

                        // ====================================================
                        // INTERACTION
                        // ====================================================

                        const handleInteraction =
                            (
                                e,
                                target
                            ) => {
                                if (
                                    typeof hoverTimer !==
                                        'undefined' &&
                                    hoverTimer !==
                                        null
                                ) {
                                    clearTimeout(
                                        hoverTimer
                                    );
                                }

                                if (
                                    isPaused ||
                                    !target ||
                                    !e.isTrusted
                                ) {
                                    return;
                                }

                                if (
                                    target.closest &&
                                    target.closest(
                                        '#sr-widget-host'
                                    )
                                ) {
                                    return;
                                }

                                // Popover text selection.
                                if (
                                    target.closest &&
                                    target.closest(
                                        '.popover'
                                    )
                                ) {
                                    const selectedText =
                                        normalizeText(
                                            window
                                                .getSelection()
                                                ?.toString() ||
                                            ''
                                        );

                                    if (
                                        selectedText
                                    ) {
                                        return;
                                    }
                                }

                                // Enter sonrası browser'ın form submit
                                // için oluşturduğu click'i gerçek kullanıcı
                                // click'i olarak kaydetme.
                                if (
                                    e.detail === 0 &&
                                    lastKeyboardAction &&
                                    lastKeyboardAction.key ===
                                        'Enter' &&
                                    Date.now() -
                                        lastKeyboardAction.clientTimestamp <
                                        1000
                                ) {
                                    const interactiveTarget =
                                        target.closest
                                            ? target.closest(
                                                'button, a, [role="button"], [role="link"]'
                                            )
                                            : null;

                                    if (
                                        interactiveTarget
                                    ) {
                                        return;
                                    }
                                }

                                const now =
                                    Date.now();

                                if (
                                    lastActionTarget &&
                                    (
                                        lastActionTarget ===
                                            target ||
                                        lastActionTarget.contains(
                                            target
                                        ) ||
                                        target.contains(
                                            lastActionTarget
                                        )
                                    )
                                ) {
                                    if (
                                        now -
                                            lastActionTime <
                                        800
                                    ) {
                                        return;
                                    }
                                }
                                else {
                                    if (
                                        now -
                                            lastActionTime <
                                        100
                                    ) {
                                        return;
                                    }
                                }

                                lastActionTime =
                                    now;

                                lastActionTarget =
                                    target;

                                const info =
                                    getElementInfo(
                                        target
                                    );

                                // ASSERT
                                if (
                                    isAssertMode
                                ) {
                                    e.preventDefault();
                                    e.stopPropagation();

                                    window.smartRecorderEmit(
                                        JSON.stringify({
                                            actionType:
                                                'Assert',

                                            ...info,

                                            ...getClientEventMetadata(
                                                'Assert'
                                            )
                                        })
                                    );

                                    isAssertMode =
                                        false;

                                    const btn =
                                        document
                                            .getElementById(
                                                'sr-widget-host'
                                            )
                                            ?.shadowRoot
                                            ?.getElementById(
                                                'assertBtn'
                                            );

                                    if (
                                        btn
                                    ) {
                                        btn.style.background =
                                            'rgba(255,255,255,0.1)';

                                        btn.innerHTML =
                                            '🎯 Doğrula';
                                    }

                                    return;
                                }

                                window.smartRecorderEmit(
                                    JSON.stringify({
                                        actionType:
                                            'Click',

                                        ...info,

                                        ...getClientEventMetadata(
                                            'Click'
                                        )
                                    })
                                );
                            };

                        window.addEventListener(
                            'copy',
                            (e) => {
                                const selection = window.getSelection();
                                const text = normalizeText(selection?.toString() || '').trim();

                                if (!text || !selection || selection.rangeCount === 0) {
                                    return;
                                }

                                let node = selection.getRangeAt(0).commonAncestorContainer;
                                let el = node.nodeType === 3 ? node.parentNode : node;

                                // 1. ÖNCE: TAŞAN SEÇİM (COMMON ANCESTOR) KORUMASI 
                                // Eğer kullanıcı dışa taştıysa, en doğru ve dar alt elementi (örn: hücreyi) buluruz.
                                if (el.children && el.children.length > 0) {
                                    const allDescendants = Array.from(el.querySelectorAll('*'));
                                    let bestMatch = null;

                                    for (const child of allDescendants) {
                                        const childText = normalizeText(child.textContent || '').trim();
                                        
                                        // Kusursuz Eşleşme
                                        if (childText === text) {
                                            bestMatch = child;
                                            break;
                                        }
                                        
                                        // Kısmi Eşleşme
                                        if (childText.includes(text)) {
                                            if (!bestMatch) {
                                                bestMatch = child;
                                            } else {
                                                const currentDiff = Math.abs(normalizeText(bestMatch.textContent || '').trim().length - text.length);
                                                const newDiff = Math.abs(childText.length - text.length);
                                                
                                                if (newDiff < currentDiff) {
                                                    bestMatch = child;
                                                }
                                            }
                                        }
                                    }

                                    if (bestMatch) {
                                        el = bestMatch; // Geniş TR yerine, nokta atışı TD'yi hedef element yaptık!
                                    }
                                }

                                // 2. SONRA: AKILLI METİN KIRPMA (SUBSTRING) TESPİTİ
                                // En doğru/dar elementi (el) bulduktan sonra, o elementin içinde 
                                // kullanıcının seçmediği (kalan) fazlalıkları tespit ediyoruz.
                                const fullNodeText = normalizeText(el.textContent || '');
                                let extractPrefix = '';
                                let extractSuffix = '';

                                if (text && fullNodeText && fullNodeText !== text && fullNodeText.includes(text)) {
                                    const startIndex = fullNodeText.indexOf(text);
                                    extractPrefix = fullNodeText.substring(0, startIndex);
                                    extractSuffix = fullNodeText.substring(startIndex + text.length);
                                }

                                const popoverSource = findPopoverSource(el, text);

                                if (popoverSource) {
                                    const popoverInfo = getPopoverExtractionInfo(popoverSource, text);
                                    const sourceInfo = getElementInfo(popoverSource);

                                    if (popoverInfo) {
                                        window.smartRecorderEmit(
                                            JSON.stringify({
                                                actionType: 'Extract',
                                                ...sourceInfo,
                                                extractedValue: text,
                                                extractionMode: popoverInfo.extractionMode,
                                                attributeName: popoverInfo.attributeName,
                                                extractionLabel: popoverInfo.extractionLabel,
                                                extractionLabelIndex: popoverInfo.extractionLabelIndex,
                                                extractPrefix: extractPrefix, // YENİ
                                                extractSuffix: extractSuffix, // YENİ
                                                ...getClientEventMetadata('Extract')
                                            })
                                        );
                                        return;
                                    }
                                }

                                const info = getElementInfo(el);

                                window.smartRecorderEmit(
                                    JSON.stringify({
                                        actionType: 'Extract',
                                        ...info,
                                        extractedValue: text,
                                        extractionMode: 'Text',
                                        attributeName: '',
                                        extractionLabel: '',
                                        extractPrefix: extractPrefix, // YENİ
                                        extractSuffix: extractSuffix, // YENİ
                                        ...getClientEventMetadata('Extract')
                                    })
                                );
                            },
                            {
                                capture: true
                            }
                        );

                        // ====================================================
                        // MOUSEOVER
                        // ====================================================

                        window.addEventListener(
                            'mouseover',
                            (e) => {
                                if (
                                    isPaused ||
                                    !e.target
                                ) {
                                    return;
                                }

                                if (
                                    e.target.closest &&
                                    e.target.closest(
                                        '#sr-widget-host'
                                    )
                                ) {
                                    return;
                                }

                                if (
                                    e.target.closest
                                ) {
                                    const trigger =
                                        e.target.closest(
                                            '[data-toggle="popover"][data-content]'
                                        );

                                    if (
                                        trigger
                                    ) {
                                        lastPopoverTrigger =
                                            trigger;
                                    }
                                }

                                const target =
                                    getLogicalTarget(
                                        e.target
                                    );

                                if (
                                    !target
                                ) {
                                    return;
                                }

                                if (
                                    hoverPendingTarget &&
                                    (
                                        hoverPendingTarget ===
                                            target ||
                                        hoverPendingTarget.contains(
                                            target
                                        ) ||
                                        target.contains(
                                            hoverPendingTarget
                                        )
                                    )
                                ) {
                                    return;
                                }

                                clearTimeout(
                                    hoverTimer
                                );

                                hoverPendingTarget =
                                    target;

                                hoverTimer =
                                    setTimeout(
                                        () => {
                                            if (
                                                lastHoverTarget ===
                                                target
                                            ) {
                                                return;
                                            }

                                            lastHoverTarget =
                                                target;

                                            hoverPendingTarget =
                                                null;

                                            const info =
                                                getElementInfo(
                                                    target
                                                );

                                            const isMeaningful =
                                                (
                                                    info.elementId &&
                                                    info.elementId.length >
                                                        0
                                                ) ||
                                                [
                                                    'a',
                                                    'button',
                                                    'th',
                                                    'td',
                                                    'tr',
                                                    'li',
                                                    'i',
                                                    'label'
                                                ].includes(
                                                    info.tag
                                                );

                                            if (
                                                isMeaningful &&
                                                info.textContent
                                            ) {
                                                window.smartRecorderEmit(
                                                    JSON.stringify({
                                                        actionType:
                                                            'Hover',

                                                        ...info,

                                                        ...getClientEventMetadata(
                                                            'Hover'
                                                        )
                                                    })
                                                );
                                            }
                                        },
                                        500
                                    );
                            },
                            {
                                capture: true,
                                passive: true
                            }
                        );

                        // ====================================================
                        // MOUSEOUT
                        // ====================================================

                        window.addEventListener(
                            'mouseout',
                            (e) => {
                                clearTimeout(
                                    hoverTimer
                                );
                            },
                            {
                                capture: true,
                                passive: true
                            }
                        );

                        // ====================================================
                        // CHANGE
                        // ====================================================

                        window.addEventListener(
                            'change',
                            (e) => {
                                if (
                                    isPaused ||
                                    !e.target
                                ) {
                                    return;
                                }

                                const target =
                                    e.target;

                                const info =
                                    getElementInfo(
                                        target
                                    );

                                if (
                                    target.tagName ===
                                    'SELECT'
                                ) {
                                    window.smartRecorderEmit(
                                        JSON.stringify({
                                            actionType:
                                                'Select',

                                            ...info,

                                            selectedValue:
                                                target.value,

                                            ...getClientEventMetadata(
                                                'Select'
                                            )
                                        })
                                    );
                                }
                                else if (
                                    target.tagName ===
                                        'INPUT' ||
                                    target.tagName ===
                                        'TEXTAREA'
                                ) {
                                    if (
                                        target.type ===
                                            'checkbox' ||
                                        target.type ===
                                            'radio'
                                    ) {
                                        return;
                                    }

                                    window.smartRecorderEmit(
                                        JSON.stringify({
                                            actionType:
                                                'Input',

                                            ...info,

                                            value:
                                                target.value,

                                            ...getClientEventMetadata(
                                                'Input'
                                            )
                                        })
                                    );
                                }
                            },
                            {
                                capture: true,
                                passive: true
                            }
                        );

                        // ====================================================
                        // KEYBOARD
                        // ====================================================

                        window.addEventListener(
                            'keydown',
                            (e) => {
                                if (
                                    isPaused ||
                                    !e.target
                                ) {
                                    return;
                                }

                                lastKeyboardAction = {
                                    key:
                                        e.key,

                                    clientTimestamp:
                                        Date.now()
                                };

                                if (
                                    e.key === 'Enter' ||
                                    e.key === 'Escape'
                                ) {
                                    const target =
                                        e.target;

                                    const info =
                                        getElementInfo(
                                            target
                                        );

                                    if (
                                        e.key === 'Enter' &&
                                        (
                                            target.tagName ===
                                                'INPUT' ||
                                            target.tagName ===
                                                'TEXTAREA'
                                        )
                                    ) {
                                        window.smartRecorderEmit(
                                            JSON.stringify({
                                                actionType:
                                                    'Input',

                                                ...info,

                                                value:
                                                    target.value,

                                                ...getClientEventMetadata(
                                                    'Input'
                                                )
                                            })
                                        );
                                    }

                                    window.smartRecorderEmit(
                                        JSON.stringify({
                                            actionType:
                                                'Keyboard',

                                            key:
                                                e.key,

                                            ...info,

                                            ...getClientEventMetadata(
                                                'Keyboard'
                                            )
                                        })
                                    );
                                }
                            },
                            {
                                capture: true,
                                passive: true
                            }
                        );

                        // ====================================================
                        // TAB ACTIVATION
                        // ====================================================

                        let lastVisibilityState =
                            document.visibilityState;

                        let lastTabActivationTimestamp =
                            0;

                        const emitTabActivation =
                            () => {
                                if (
                                    isPaused ||
                                    document.visibilityState !==
                                        'visible'
                                ) {
                                    return;
                                }

                                const now =
                                    Date.now();

                                if (
                                    now -
                                        lastTabActivationTimestamp <
                                    250
                                ) {
                                    return;
                                }

                                lastTabActivationTimestamp =
                                    now;

                                window.smartRecorderEmit(
                                    JSON.stringify({
                                        actionType:
                                            'TabActivated',

                                        ...getClientEventMetadata(
                                            'TabActivated'
                                        )
                                    })
                                );
                            };

                        document.addEventListener(
                            'visibilitychange',
                            () => {
                                const currentState =
                                    document.visibilityState;

                                if (
                                    lastVisibilityState !==
                                        'visible' &&
                                    currentState ===
                                        'visible'
                                ) {
                                    emitTabActivation();
                                }

                                lastVisibilityState =
                                    currentState;
                            },
                            {
                                capture: true
                            }
                        );

                        // ====================================================
                        // SMART HOISTER
                        // ====================================================

                        const getLogicalTarget =
                            (el) => {
                                if (
                                    !el
                                ) {
                                    return null;
                                }

                                // ------------------------------------------------
                                // Her zaman gerçek interactive ancestor'a yüksel.
                                //
                                // <button>
                                //     <i></i>
                                // </button>
                                //
                                // i'ye tıklanırsa button döndür.
                                // ------------------------------------------------

                                const interactive =
                                    el.closest
                                        ? el.closest(
                                            'button, a, input, select, textarea, [role="button"], [role="link"]'
                                        )
                                        : null;

                                if (
                                    interactive
                                ) {
                                    return interactive;
                                }

                                const cell =
                                    el.closest
                                        ? el.closest(
                                            'td, tr'
                                        )
                                        : null;

                                if (
                                    cell
                                ) {
                                    return cell;
                                }

                                return el;
                            };

                        // ====================================================
                        // MOUSEDOWN
                        // ====================================================

                        window.addEventListener(
                            'mousedown',
                            (e) => {
                                if (
                                    !e.target
                                ) {
                                    return;
                                }

                                if (
                                    e.target.closest &&
                                    e.target.closest(
                                        '.popover'
                                    )
                                ) {
                                    return;
                                }

                                const target =
                                    getLogicalTarget(
                                        e.target
                                    );

                                if (
                                    !target
                                ) {
                                    return;
                                }

                                const tag =
                                    target.tagName
                                        ? target.tagName.toUpperCase()
                                        : '';

                                const isDropdownOrGrid =
                                    tag === 'LI' ||
                                    tag === 'TD' ||
                                    tag === 'TR' ||
                                    tag === 'TH' ||
                                    target.getAttribute(
                                        'role'
                                    ) === 'option' ||
                                    target.getAttribute(
                                        'role'
                                    ) === 'treeitem';

                                if (
                                    isDropdownOrGrid
                                ) {
                                    handleInteraction(
                                        e,
                                        target
                                    );
                                }
                            },
                            {
                                capture: true,
                                passive: true
                            }
                        );

                        // ====================================================
                        // CLICK
                        // ====================================================

                        window.addEventListener(
                            'click',
                            (e) => {
                                const target =
                                    getLogicalTarget(
                                        e.target
                                    );

                                handleInteraction(
                                    e,
                                    target
                                );
                            },
                            {
                                capture: true
                            }
                        );

                        // ====================================================
                        // RECORDER WIDGET
                        // ====================================================

                        setInterval(
                            () => {
                                if (
                                    !document.body
                                ) {
                                    return;
                                }

                                if (
                                    document.getElementById(
                                        'sr-widget-host'
                                    )
                                ) {
                                    return;
                                }

                                const host =
                                    document.createElement(
                                        'div'
                                    );

                                host.id =
                                    'sr-widget-host';

                                host.style.cssText =
                                    'position: fixed; top: 15px; right: 15px; z-index: 2147483647; font-family: Segoe UI, sans-serif;';

                                const shadow =
                                    host.attachShadow({
                                        mode: 'open'
                                    });

                                shadow.innerHTML = `
                                    <style>
                                        .widget-bar {
                                            background: rgba(26, 26, 26, 0.95);
                                            color: #fff;
                                            padding: 8px 14px;
                                            border-radius: 30px;
                                            box-shadow: 0 4px 20px rgba(0,0,0,0.3);
                                            display: flex;
                                            gap: 10px;
                                            border: 1px solid rgba(255,255,255,0.15);
                                            user-select: none;
                                        }

                                        .btn {
                                            background: rgba(255,255,255,0.1);
                                            border: none;
                                            color: #fff;
                                            padding: 5px 10px;
                                            border-radius: 15px;
                                            cursor: pointer;
                                            font-size: 12px;
                                            font-weight: 600;
                                            transition: background 0.2s;
                                        }

                                        .btn:hover {
                                            background: rgba(255,255,255,0.25);
                                        }

                                        .btn-stop {
                                            background: #ef4444;
                                        }

                                        .btn-stop:hover {
                                            background: #dc2626;
                                        }

                                        .sr-tooltip {
                                            position: fixed;
                                            pointer-events: none;
                                            background: #1e1e1e;
                                            color: #4ec9b0;
                                            border: 1px solid #007acc;
                                            padding: 4px 8px;
                                            border-radius: 4px;
                                            font-family: Consolas, monospace;
                                            font-size: 11px;
                                            z-index: 2147483647;
                                            display: none;
                                            box-shadow: 0 2px 8px rgba(0,0,0,0.4);
                                        }
                                    </style>

                                    <div class="widget-bar">
                                        <button
                                            class="btn"
                                            id="assertBtn">
                                            🎯 Doğrula
                                        </button>

                                        <button
                                            class="btn"
                                            id="pauseBtn">
                                            ⏸️ Duraklat
                                        </button>

                                        <button
                                            class="btn btn-stop"
                                            id="stopBtn">
                                            ⏹️ Bitir
                                        </button>
                                    </div>

                                    <div
                                        id="tooltip"
                                        class="sr-tooltip">
                                    </div>
                                `;

                                document.body.appendChild(
                                    host
                                );

                                shadow
                                    .getElementById(
                                        'assertBtn'
                                    )
                                    .addEventListener(
                                        'click',
                                        (e) => {
                                            e.stopPropagation();

                                            isAssertMode =
                                                !isAssertMode;

                                            e.target.style.background =
                                                isAssertMode
                                                    ? '#8b5cf6'
                                                    : 'rgba(255,255,255,0.1)';

                                            e.target.innerHTML =
                                                isAssertMode
                                                    ? '🎯 Seçiliyor...'
                                                    : '🎯 Doğrula';
                                        }
                                    );

                                shadow
                                    .getElementById(
                                        'pauseBtn'
                                    )
                                    .addEventListener(
                                        'click',
                                        (e) => {
                                            e.stopPropagation();

                                            isPaused =
                                                !isPaused;

                                            e.target.innerHTML =
                                                isPaused
                                                    ? '▶️ Devam Et'
                                                    : '⏸️ Duraklat';

                                            e.target.style.background =
                                                isPaused
                                                    ? '#f59e0b'
                                                    : 'rgba(255,255,255,0.1)';
                                        }
                                    );

                                shadow
                                    .getElementById(
                                        'stopBtn'
                                    )
                                    .addEventListener(
                                        'click',
                                        (e) => {
                                            e.stopPropagation();

                                            if (
                                                document.activeElement &&
                                                typeof document
                                                    .activeElement
                                                    .blur ===
                                                    'function'
                                            ) {
                                                document
                                                    .activeElement
                                                    .blur();
                                            }

                                            setTimeout(
                                                () => {
                                                    window.smartRecorderEmit(
                                                        JSON.stringify({
                                                            actionType:
                                                                'StopControl'
                                                        })
                                                    );
                                                },
                                                300
                                            );
                                        }
                                    );

                                window.addEventListener(
                                    'mousemove',
                                    (e) => {
                                        const tooltip =
                                            shadow.getElementById(
                                                'tooltip'
                                            );

                                        if (
                                            isPaused ||
                                            !e.target ||
                                            e.target.id ===
                                                'sr-widget-host' ||
                                            host.contains(
                                                e.target
                                            )
                                        ) {
                                            tooltip.style.display =
                                                'none';

                                            return;
                                        }

                                        const info =
                                            getElementInfo(
                                                e.target
                                            );

                                        let selector =
                                            info.tag;

                                        if (
                                            info.elementId
                                        ) {
                                            selector =
                                                '#' +
                                                info.elementId;
                                        }
                                        else if (
                                            info.placeholder
                                        ) {
                                            selector =
                                                'placeholder=' +
                                                info.placeholder;
                                        }
                                        else if (
                                            info.textContent
                                        ) {
                                            selector =
                                                'text=' +
                                                info.textContent;
                                        }

                                        tooltip.textContent =
                                            selector;

                                        tooltip.style.left =
                                            (
                                                e.clientX +
                                                12
                                            ) + 'px';

                                        tooltip.style.top =
                                            (
                                                e.clientY +
                                                12
                                            ) + 'px';

                                        tooltip.style.display =
                                            'block';
                                    },
                                    {
                                        capture: true,
                                        passive: true
                                    }
                                );
                            },
                            500
                        );
                    }
                """);

                // ============================================================
                // NEW PAGE
                // ============================================================

                _context.Page += async (_, newPage) =>
                {
                    if (_isFirstPage)
                    {
                        _isFirstPage =
                            false;

                        return;
                    }

                    _pageCounter++;

                    string alias =
                        $"page{_pageCounter}";

                    _pageAliases[newPage] =
                        alias;

                    // CDP session'i hemen oluştur.
                    //
                    // Böylece page navigation'ı mümkün olduğunca erken
                    // gözlemleyebiliriz.
                    await AttachEventListenersToPage(
                        newPage,
                        alias);

                    long timestamp =
                        DateTimeOffset.UtcNow
                            .ToUnixTimeMilliseconds();

                    OnActionRecorded?.Invoke(
                        new TabOpenedAction
                        {
                            ActionType =
                                "Tab Opened",

                            PageAlias =
                                alias,

                            Url =
                                newPage.Url,

                            Timestamp =
                                DateTime.Now,

                            ClientTimestamp =
                                timestamp,

                            ClientSequence =
                                0
                        });
                };

                // ============================================================
                // MAIN PAGE
                // ============================================================

                _page =
                    await _context.NewPageAsync();

                _pageAliases.Clear();

                _pageAliases[_page] =
                    "page";

                await AttachEventListenersToPage(
                    _page,
                    "page");

                await _page.GotoAsync(
                    targetUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[HATA] StartRecordingAsync: {ex.Message}");

                await StopRecordingAsync();

                throw;
            }
        }

        // ====================================================================
        // ATTACH PAGE LISTENERS
        // ====================================================================

        private async Task AttachEventListenersToPage(
            IPage page,
            string alias)
        {
            // ================================================================
            // CDP SESSION
            // ================================================================

            try
            {
                if (
                    _context != null &&
                    !_cdpSessions.ContainsKey(page)
                )
                {
                    var cdp =
                        await _context.NewCDPSessionAsync(
                            page);

                    _cdpSessions[page] =
                        cdp;

                    // Page domain'i etkinleştir.
                    await cdp.SendAsync(
                        "Page.enable");

                    // --------------------------------------------------------
                    // frameRequestedNavigation
                    // --------------------------------------------------------
                    //
                    // Renderer tarafından başlatılmış navigation'ın reason
                    // bilgisini al.
                    //
                    // Örn:
                    // anchorClick
                    // formSubmissionPost
                    // scriptInitiated
                    // reload
                    // --------------------------------------------------------

                    cdp
                        .Event("Page.frameRequestedNavigation")
                        .OnEvent += (_, json) =>
                        {
                            try
                            {
                                if (json == null)
                                {
                                    return;
                                }

                                string url = json.Value.TryGetProperty("url", out var urlProperty)
                                    ? (urlProperty.GetString() ?? "")
                                    : "";

                                string reason = json.Value.TryGetProperty("reason", out var reasonProperty)
                                    ? (reasonProperty.GetString() ?? "")
                                    : "";

                                if (string.IsNullOrWhiteSpace(url))
                                {
                                    return;
                                }

                                // ----------------------------------------------------------
                                // TRIGGER SNAPSHOT (senkron, delay YOK)
                                // ----------------------------------------------------------
                                //
                                // frameRequestedNavigation, tetikleyici kullanıcı action'ından
                                // hemen sonra ateşlendiği için burada okunan
                                // _lastBrowserActions[page] güvenilir bir snapshot'tır.
                                // ----------------------------------------------------------

                                long triggerClientSequence = 0;
                                long triggerClientTimestamp = 0;

                                if (
                                    _lastBrowserActions.TryGetValue(
                                        page,
                                        out BrowserActionMetadata? triggeringAction)
                                )
                                {
                                    triggerClientSequence = triggeringAction.ClientSequence;
                                    triggerClientTimestamp = triggeringAction.ClientTimestamp;
                                }

                                _lastNavigationRequests[page] = new NavigationRequestMetadata
                                {
                                    Url = url,
                                    Reason = reason,
                                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                };
                            }
                            catch (Exception ex)
                            {
                                // Tam stack trace görebilmek için ex.ToString() kullanıyoruz.
                                Debug.WriteLine($"[CDP frameRequestedNavigation HATA] {ex}");
                            }
                        };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[CDP SESSION HATA] {ex.Message}");
            }

            // ================================================================
            // PAGE CLOSE
            // ================================================================

            page.Close += (_, _) =>
            {
                _lastBrowserActions.TryRemove(
                    page,
                    out _);

                _lastNavigationRequests.TryRemove(
                    page,
                    out _);

                if (
                    _cdpSessions.TryGetValue(
                        page,
                        out ICDPSession? cdp)
                )
                {
                    _cdpSessions.Remove(
                        page);

                    _ = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await cdp.DetachAsync();
                            }
                            catch
                            {
                            }
                        });
                }

                if (
                    alias ==
                    "page"
                )
                {
                    _ = Task.Run(
                        async () =>
                        {
                            await StopRecordingAsync();

                            OnRecordingStopped?.Invoke();
                        });
                }
            };

            // ================================================================
            // NAVIGATION
            // ================================================================

            page.FrameNavigated += (_, frame) =>
            {
                if (
                    frame !=
                    page.MainFrame
                )
                {
                    return;
                }

                _ = HandleNavigationAsync(
                    page,
                    alias,
                    frame.Url);
            };

            // ================================================================
            // NETWORK
            // ================================================================

            page.Response += (_, response) =>
            {
                if (
                    response.Request.ResourceType ==
                        "fetch" ||
                    response.Request.ResourceType ==
                        "xhr"
                )
                {
                    long timestamp =
                        DateTimeOffset.UtcNow
                            .ToUnixTimeMilliseconds();

                    OnActionRecorded?.Invoke(
                        new NetworkAction
                        {
                            ActionType =
                                "Network Request",

                            Url =
                                response.Url,

                            Method =
                                response.Request.Method,

                            StatusCode =
                                response.Status,

                            PageAlias =
                                alias,

                            Timestamp =
                                DateTime.Now,

                            ClientTimestamp =
                                timestamp,

                            ClientSequence =
                                0
                        });
                }
            };

            // ================================================================
            // DIALOG
            // ================================================================

            page.Dialog += (_, dialog) =>
            {
                long timestamp =
                    DateTimeOffset.UtcNow
                        .ToUnixTimeMilliseconds();

                OnActionRecorded?.Invoke(
                    new DialogAction
                    {
                        ActionType =
                            "Browser Alert",

                        DialogType =
                            dialog.Type,

                        Message =
                            dialog.Message,

                        PageAlias =
                            alias,

                        Timestamp =
                            DateTime.Now,

                        ClientTimestamp =
                            timestamp,

                        ClientSequence =
                            0
                    });

                _ = Task.Run(
                    async () =>
                    {
                        try
                        {
                            await dialog.AcceptAsync();
                        }
                        catch
                        {
                        }
                    });
            };
        }

        // ====================================================================
        // NAVIGATION ANALYSIS
        // ====================================================================

        private async Task HandleNavigationAsync(
            IPage page,
            string alias,
            string url)
        {
            try
            {
                // CDP history kaydının güncellenmesi için kısa bir grace period.
                await Task.Delay(30);

                string transitionType = "";
                string userTypedUrl = "";
                string navigationReason = "";

                // ============================================================
                // 1. CDP REQUESTED NAVIGATION
                // ============================================================

                if (
                    _lastNavigationRequests.TryGetValue(
                        page,
                        out NavigationRequestMetadata? request)
                )
                {
                    long now =
                        DateTimeOffset.UtcNow
                            .ToUnixTimeMilliseconds();

                    long age =
                        now -
                        request.Timestamp;

                    if (
                        age >= 0 &&
                        age <= 5000
                    )
                    {
                        navigationReason =
                            request.Reason;
                    }
                }

                // ============================================================
                // 2. CDP NAVIGATION HISTORY
                // ============================================================

                if (
                    _cdpSessions.TryGetValue(
                        page,
                        out ICDPSession? cdp)
                )
                {
                    try
                    {
                        var response =
                            await cdp.SendAsync(
                                "Page.getNavigationHistory");

                        if (
                            response is { } json)
                        {
                            int currentIndex =
                                json.TryGetProperty(
                                    "currentIndex",
                                    out var currentIndexProperty)
                                    ? currentIndexProperty.GetInt32()
                                    : -1;

                            if (
                                currentIndex >= 0 &&
                                json.TryGetProperty(
                                    "entries",
                                    out var entries) &&
                                entries.ValueKind ==
                                    JsonValueKind.Array &&
                                currentIndex <
                                    entries.GetArrayLength()
                            )
                            {
                                var currentEntry =
                                    entries[currentIndex];

                                transitionType =
                                    currentEntry.TryGetProperty(
                                        "transitionType",
                                        out var transitionProperty)
                                        ? (
                                            transitionProperty.GetString()
                                            ?? ""
                                          )
                                        : "";

                                userTypedUrl =
                                    currentEntry.TryGetProperty(
                                        "userTypedURL",
                                        out var userTypedProperty)
                                        ? (
                                            userTypedProperty.GetString()
                                            ?? ""
                                          )
                                        : "";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[CDP HISTORY HATA] {ex.Message}");
                    }
                }

                // ============================================================
                // 3. LAST BROWSER ACTION
                // ============================================================

                BrowserActionMetadata? lastAction =
                    null;

                _lastBrowserActions.TryGetValue(
                    page,
                    out lastAction);

                bool hasRecentUserAction =
                    false;

                if (
                    lastAction != null &&
                    lastAction.ClientTimestamp > 0
                )
                {
                    long now =
                        DateTimeOffset.UtcNow
                            .ToUnixTimeMilliseconds();

                    long delta =
                        now -
                        lastAction.ClientTimestamp;

                    hasRecentUserAction =
                        delta >= 0 &&
                        delta <= 5000 &&
                        (
                            lastAction.ActionType ==
                                "Click" ||
                            lastAction.ActionType ==
                                "Keyboard" ||
                            lastAction.ActionType ==
                                "Select"
                        );
                }

                // ============================================================
                // 4. CLASSIFY
                // ============================================================

                string navigationKind =
                    ClassifyNavigation(
                        transitionType,
                        navigationReason,
                        hasRecentUserAction,
                        page == _page &&
                            !_initialMainNavigationRecorded);

                // ============================================================
                // 5. INITIAL STATE
                // ============================================================

                if (
                    page == _page &&
                    !_initialMainNavigationRecorded
                )
                {
                    _initialMainNavigationRecorded =
                        true;

                    navigationKind =
                        "Initial";
                }

                // ============================================================
                // 6. TRIGGER SEQUENCE
                // ============================================================

                long triggerSequence = 0;
                long navigationClientTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (navigationKind == "UserAction" && lastAction != null)
                {
                    triggerSequence = lastAction.ClientSequence;

                    if (lastAction.ClientTimestamp > 0)
                    {
                        navigationClientTimestamp = lastAction.ClientTimestamp;
                    }
                }

                // ============================================================
                // 8. EMIT
                // ============================================================

                OnActionRecorded?.Invoke(
                    new NavigationAction
                    {
                        ActionType =
                            "Navigation",

                        Url =
                            url,

                        PageAlias =
                            alias,

                        Timestamp =
                            DateTime.Now,

                        ClientTimestamp =
                            navigationClientTimestamp,

                        ClientSequence =
                            0,

                        NavigationKind =
                            navigationKind,

                        TransitionType =
                            transitionType,

                        NavigationReason =
                            navigationReason,

                        UserTypedUrl =
                            userTypedUrl,

                        NavigationTriggerClientSequence =
                            triggerSequence
                    });

                Debug.WriteLine(
                    $"[NAVIGATION] {alias} | {navigationKind} | transition={transitionType} | reason={navigationReason} | typed={userTypedUrl} | url={url}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[NAVIGATION ANALYSIS HATA] {ex.Message}");
            }
        }

        // ====================================================================
        // NAVIGATION CLASSIFICATION
        // ====================================================================

        private string ClassifyNavigation(
            string transitionType,
            string navigationReason,
            bool hasRecentUserAction,
            bool isInitial)
        {
            if (isInitial)
            {
                return "Initial";
            }

            string transition =
                (
                    transitionType ??
                    ""
                ).Trim().ToLowerInvariant();

            string reason =
                (
                    navigationReason ??
                    ""
                ).Trim().ToLowerInvariant();

            // ================================================================
            // MANUAL ADDRESS BAR
            // ================================================================

            if (
                transition ==
                    "address_bar" ||
                transition ==
                    "typed"
            )
            {
                return "Manual";
            }

            // ================================================================
            // RELOAD
            // ================================================================

            if (
                transition ==
                    "reload" ||
                reason ==
                    "reload"
            )
            {
                return "Reload";
            }

            // ================================================================
            // HISTORY
            // ================================================================

            if (
                transition ==
                    "back_forward"
            )
            {
                return "History";
            }

            // ================================================================
            // DIRECT USER NAVIGATION
            // ================================================================

            if (
                transition ==
                    "link" ||
                transition ==
                    "form_submit" ||
                reason ==
                    "anchorclick" ||
                reason ==
                    "formsubmissionget" ||
                reason ==
                    "formsubmissionpost"
            )
            {
                return "UserAction";
            }

            // ================================================================
            // SCRIPT-INITIATED NAVIGATION AFTER A REAL USER ACTION
            // ================================================================

            if (
                reason ==
                    "scriptinitiated" &&
                hasRecentUserAction
            )
            {
                return "UserAction";
            }

            // ================================================================
            // OTHER / GENERATED
            //
            // Eğer son browser action çok yeniyse bunu da user action
            // navigation olarak değerlendirebiliriz.
            // ================================================================

            if (
                hasRecentUserAction
            )
            {
                return "UserAction";
            }

            // ================================================================
            // AUTOMATIC REDIRECT / OTHER
            // ================================================================

            if (
                transition ==
                    "generated" ||
                transition ==
                    "auto_toplevel" ||
                transition ==
                    "auto_bookmark" ||
                transition ==
                    "other"
            )
            {
                return "Automatic";
            }

            return "Unknown";
        }

        // ====================================================================
        // STOP
        // ====================================================================

        public async Task StopRecordingAsync()
        {
            _lastBrowserActions.Clear();

            _lastNavigationRequests.Clear();

            foreach (
                var cdp in
                _cdpSessions.Values)
            {
                try
                {
                    await cdp.DetachAsync();
                }
                catch
                {
                }
            }

            _cdpSessions.Clear();

            if (
                _context != null
            )
            {
                foreach (
                    var page in
                    _context.Pages)
                {
                    try
                    {
                        await page.EvaluateAsync(
                            """
                            if (
                                document.activeElement &&
                                typeof document.activeElement.blur === 'function'
                            ) {
                                document.activeElement.blur();
                            }
                            """);

                        await Task.Delay(
                            150);
                    }
                    catch
                    {
                    }
                }
            }

            if (
                _page != null
            )
            {
                try
                {
                    await _page.CloseAsync();
                }
                catch
                {
                }

                _page = null;
            }

            if (
                _context != null
            )
            {
                try
                {
                    await _context.CloseAsync();
                }
                catch
                {
                }

                _context = null;
            }

            if (
                _browser != null
            )
            {
                try
                {
                    await _browser.CloseAsync();
                }
                catch
                {
                }

                _browser = null;
            }

            if (
                _playwright != null
            )
            {
                _playwright.Dispose();

                _playwright = null;
            }
        }

        // ====================================================================
        // DISPOSE
        // ====================================================================

        public async ValueTask DisposeAsync()
        {
            await StopRecordingAsync();

            GC.SuppressFinalize(
                this);
        }
    }
}