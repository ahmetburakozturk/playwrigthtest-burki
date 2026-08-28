using System;
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
        private readonly Dictionary<IPage, string> _pageAliases = new();

        public event Action<UserAction>? OnActionRecorded;
        public event Action? OnRecordingStopped;

        public async Task StartRecordingAsync(string targetUrl)
        {
            try
            {
                await StopRecordingAsync();
                _pageCounter = 0;

                // --- 1. CHROMIUM'U UYGULAMANIN İÇİNE (PORTABLE) DAHİL ETME KODU ---
                // Tarayıcıların indirileceği/aranacağı yeri uygulamanın çalıştığı dizindeki "browsers" klasörü yapıyoruz.
                string browserPath = System.IO.Path.Combine(AppContext.BaseDirectory, "browsers");
                Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", browserPath);

                // Klasörde Chromium yoksa indirir (yaklaşık 120MB), varsa hemen geçer.
                int exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
                if (exitCode != 0)
                {
                    throw new Exception("Playwright Chromium tarayıcısı kurulamadı veya bulunamadı.");
                }
                // ------------------------------------------------------------------

                _playwright = await Playwright.CreateAsync();
                
                // Tertemiz başlatma (Özel taşınabilir Chromium'u kullanacak, kurumsal engellere takılmaz)
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions 
                { 
                    Headless = false 
                });
                
                _context = await _browser.NewContextAsync();

                // 2. C# KÖPRÜSÜ
                // 2. C# KÖPRÜSÜ
                await _context.ExposeBindingAsync("smartRecorderEmit", (BindingSource source, string payload) =>
                {
                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        using var doc = JsonDocument.Parse(payload);
                        string actionType = doc.RootElement.GetProperty("actionType").GetString() ?? "";

                        if (actionType == "StopControl")
                        {
                            _ = Task.Run(async () =>
                            {
                                await StopRecordingAsync();
                                OnRecordingStopped?.Invoke();
                            });
                            return;
                        }

                        // --- YENİ GÜVENLİ SEKME EŞLEŞTİRME MANTIĞI ---
                        string pageAlias = "page"; // Varsayılan olarak her zaman ana sayfa
                        
                        if (source.Page != null && _pageAliases.TryGetValue(source.Page, out string alias))
                        {
                            pageAlias = alias;
                        }
                        else if (_context != null && _context.Pages.Count > 1 && source.Page != _page)
                        {
                            // Eğer Playwright Page objesini eşleştiremezse (null gelirse) 
                            // ve ortada birden fazla sekme varsa, mantıksal olarak en son açılan sekmeyi kabul et
                            pageAlias = $"page{_pageCounter}";
                        }

                        // Gelen JSON'ı ilgili aksiyon modeline dönüştür
                        UserAction? action = actionType switch
                        {
                            "Click" => JsonSerializer.Deserialize<ClickAction>(payload, options),
                            "Hover" => JsonSerializer.Deserialize<HoverAction>(payload, options),
                            "Input" => JsonSerializer.Deserialize<InputAction>(payload, options),
                            "Select" => JsonSerializer.Deserialize<SelectAction>(payload, options),
                            "Assert" => JsonSerializer.Deserialize<AssertAction>(payload, options),
                            "Keyboard" => JsonSerializer.Deserialize<KeyboardAction>(payload, options),
                            "Extract" => JsonSerializer.Deserialize<ExtractAction>(payload, options),
                            _ => null
                        };
                        
                        // Alias'ı modele güvenli bir şekilde ata ve arayüze (Event) fırlat
                        if (action != null) 
                        {
                            action = action with { PageAlias = pageAlias }; 
                            OnActionRecorded?.Invoke(action);
                        }
                    }
                    catch (Exception ex) 
                    { 
                        Debug.WriteLine($"[JSON PARSE HATA] {ex.Message}"); 
                    }
                });

                await _context.AddInitScriptAsync("""
                    if (!window.__smartRecorderInitialized) {
                        window.__smartRecorderInitialized = true;
                        
                        let isPaused = false;
                        let isAssertMode = false;
                        
                        let lastActionTime = 0;
                        let lastActionTarget = null;
                        let hoverTimer = null;
                        let lastHoverTarget = null;

                        const getCssPath = (el) => {
                            if (!(el instanceof Element)) return '';
                            let path = [];
                            while (el.nodeType === Node.ELEMENT_NODE) {
                                let selector = el.nodeName.toLowerCase();
                                if (el.id) {
                                    selector += '#' + el.id;
                                    path.unshift(selector);
                                    break;
                                }
                                let sib = el, nth = 1;
                                while (sib = sib.previousElementSibling) {
                                    if (sib.nodeName.toLowerCase() === selector) nth++;
                                }
                                if (nth !== 1) selector += ':nth-of-type(' + nth + ')';
                                path.unshift(selector);
                                el = el.parentNode;
                            }
                            return path.join(' > ');
                        };

                        const getElementInfo = (el) => {
                            let elId = el.id || '';
                            if (elId.includes('-result-') || (elId.startsWith('select2-') && elId.includes('-result'))) {
                                elId = ''; 
                            }

                            return {
                                tag: el.tagName ? el.tagName.toLowerCase() : '',
                                elementId: elId,
                                textContent: (el.innerText || '').replace(/\s+/g, ' ').substring(0, 50).trim(),
                                placeholder: el.placeholder || '',
                                ariaLabel: el.getAttribute ? (el.getAttribute('aria-label') || '') : '',
                                name: el.name || '',
                                cssSelector: getCssPath(el),
                                customTestId: el.getAttribute ? (el.getAttribute('data-name') || el.getAttribute('data-testid') || '') : '',
                                isDynamicListElement: el.closest ? !!el.closest('tr') : false
                            };
                        };

                        const handleInteraction = (e, target) => {
                            if (typeof hoverTimer !== 'undefined' && hoverTimer !== null) {
                                clearTimeout(hoverTimer);
                            }

                            if (isPaused || !target || !e.isTrusted) return;
                            if (target.closest && target.closest('#sr-widget-host')) return;
                            
                            const now = Date.now();
                            
                            if (lastActionTarget && (lastActionTarget === target || lastActionTarget.contains(target) || target.contains(lastActionTarget))) {
                                if (now - lastActionTime < 800) return; 
                            } else {
                                if (now - lastActionTime < 100) return;
                            }
                            
                            lastActionTime = now;
                            lastActionTarget = target;

                            const info = getElementInfo(target);

                            if (isAssertMode) {
                                e.preventDefault(); e.stopPropagation();
                                window.smartRecorderEmit(JSON.stringify({ actionType: 'Assert', ...info }));
                                isAssertMode = false;
                                const btn = document.getElementById('sr-widget-host')?.shadowRoot?.getElementById('assertBtn');
                                if (btn) {
                                    btn.style.background = 'rgba(255,255,255,0.1)';
                                    btn.innerHTML = '🎯 Doğrula';
                                }
                                return;
                            }
                            window.smartRecorderEmit(JSON.stringify({ actionType: 'Click', ...info }));
                        };

                        window.addEventListener('copy', (e) => {
                            const selection = window.getSelection();
                            const text = selection.toString().trim();
                            
                            if (!text || selection.rangeCount === 0) return;
                            
                            let node = selection.getRangeAt(0).commonAncestorContainer;
                            let el = node.nodeType === 3 ? node.parentNode : node;
                            
                            const info = getElementInfo(el);
                            window.smartRecorderEmit(JSON.stringify({ 
                                actionType: 'Extract', 
                                ...info,
                                extractedValue: text 
                            }));
                        }, { capture: true });

                        window.addEventListener('mouseover', (e) => {
                            if (isPaused || !e.target) return;
                            if (e.target.closest && e.target.closest('#sr-widget-host')) return;

                            clearTimeout(hoverTimer);
                            
                            hoverTimer = setTimeout(() => {
                                if (lastHoverTarget === e.target) return; 
                                lastHoverTarget = e.target;
                                
                                const info = getElementInfo(e.target);
                                const tag = info.tag;
                                
                                const isMeaningful = (info.elementId && info.elementId.length > 0) || 
                                                     ['a', 'button', 'th', 'td', 'tr', 'li', 'i', 'label'].includes(tag);
                                
                                if (isMeaningful && info.textContent) {
                                    window.smartRecorderEmit(JSON.stringify({ actionType: 'Hover', ...info }));
                                }
                            }, 800); 
                        }, { capture: true, passive: true });

                        window.addEventListener('mouseout', (e) => {
                            clearTimeout(hoverTimer);
                        }, { capture: true, passive: true });

                        window.addEventListener('change', (e) => {
                            if (isPaused || !e.target) return;
                            const target = e.target;
                            const info = getElementInfo(target);
                            if (target.tagName === 'SELECT') {
                                window.smartRecorderEmit(JSON.stringify({ actionType: 'Select', ...info, selectedValue: target.value }));
                            } else if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') {
                                if (target.type === 'checkbox' || target.type === 'radio') return;
                                window.smartRecorderEmit(JSON.stringify({ actionType: 'Input', ...info, value: target.value }));
                            }
                        }, { capture: true, passive: true });
                        
                        window.addEventListener('keydown', (e) => {
                            if (isPaused || !e.target) return;
                            if (e.key === 'Enter' || e.key === 'Escape') {
                                const target = e.target;
                                const info = getElementInfo(target);
                                if (e.key === 'Enter' && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA')) {
                                    window.smartRecorderEmit(JSON.stringify({ actionType: 'Input', ...info, value: target.value }));
                                }
                                window.smartRecorderEmit(JSON.stringify({ actionType: 'Keyboard', key: e.key, ...info }));
                            }
                        }, { capture: true, passive: true });

                        // YENİ: AKILLI HEDEF YÜKSELTİCİ (Smart Hoister)
                        const getLogicalTarget = (el) => {
                            if (!el) return null;
                            
                            // 1. ZIRH: Tıklanan şey input (arama kutusu), buton veya link ise ASLA müdahale etme!
                            const tag = el.tagName ? el.tagName.toLowerCase() : '';
                            if (['input', 'button', 'a', 'select', 'textarea'].includes(tag)) {
                                return el;
                            }

                            // 2. ZIRH: Tıklanan şey sıradan bir metin ve tablo verisi (td, tr) içindeyse, onu korumak için hücreye yükselt!
                            // Not: 'th' (başlık) ve 'li' (menü) BİLEREK hariç tutuldu ki arama kutusu ve sol menü bozulmasın.
                            const cell = el.closest('td, tr'); 
                            if (cell) return cell;
                            
                            return el;
                        };

                        window.addEventListener('mousedown', (e) => {
                            if (!e.target) return;
                            
                            const target = getLogicalTarget(e.target);
                            if (!target) return;
                            
                            const tag = target.tagName ? target.tagName.toUpperCase() : '';
                            const isDropdownOrGrid = tag === 'LI' || tag === 'TD' || tag === 'TR' || tag === 'TH' ||
                                                     target.getAttribute('role') === 'option' || 
                                                     target.getAttribute('role') === 'treeitem';
                            
                            if (isDropdownOrGrid) {
                                handleInteraction(e, target);
                            }
                        }, { capture: true, passive: true });

                        window.addEventListener('click', (e) => {
                            const target = getLogicalTarget(e.target);
                            handleInteraction(e, target);
                        }, { capture: true });

                        setInterval(() => {
                            if (!document.body) return;
                            if (document.getElementById('sr-widget-host')) return;

                            const host = document.createElement('div');
                            host.id = 'sr-widget-host';
                            host.style.cssText = 'position: fixed; top: 15px; right: 15px; z-index: 2147483647; font-family: Segoe UI, sans-serif;';
                            
                            const shadow = host.attachShadow({ mode: 'open' });
                            shadow.innerHTML = `
                                <style>
                                    .widget-bar { background: rgba(26, 26, 26, 0.95); color: #fff; padding: 8px 14px; border-radius: 30px; box-shadow: 0 4px 20px rgba(0,0,0,0.3); display: flex; gap: 10px; border: 1px solid rgba(255,255,255,0.15); user-select: none; }
                                    .btn { background: rgba(255,255,255,0.1); border: none; color: #fff; padding: 5px 10px; border-radius: 15px; cursor: pointer; font-size: 12px; font-weight: 600; transition: background 0.2s; }
                                    .btn:hover { background: rgba(255,255,255,0.25); }
                                    .btn-stop { background: #ef4444; } .btn-stop:hover { background: #dc2626; }
                                    .sr-tooltip { position: fixed; pointer-events: none; background: #1e1e1e; color: #4ec9b0; border: 1px solid #007acc; padding: 4px 8px; border-radius: 4px; font-family: Consolas, monospace; font-size: 11px; z-index: 2147483647; display: none; box-shadow: 0 2px 8px rgba(0,0,0,0.4); }
                                </style>
                                <div class="widget-bar">
                                    <button class="btn" id="assertBtn">🎯 Doğrula</button>
                                    <button class="btn" id="pauseBtn">⏸️ Duraklat</button>
                                    <button class="btn btn-stop" id="stopBtn">⏹️ Bitir</button>
                                </div>
                                <div id="tooltip" class="sr-tooltip"></div>
                            `;
                            
                            document.body.appendChild(host);

                            shadow.getElementById('assertBtn').addEventListener('click', (e) => { 
                                e.stopPropagation(); isAssertMode = !isAssertMode; 
                                e.target.style.background = isAssertMode ? '#8b5cf6' : 'rgba(255,255,255,0.1)'; 
                                e.target.innerHTML = isAssertMode ? '🎯 Seçiliyor...' : '🎯 Doğrula'; 
                            });
                            
                            shadow.getElementById('pauseBtn').addEventListener('click', (e) => { 
                                e.stopPropagation(); isPaused = !isPaused; 
                                e.target.innerHTML = isPaused ? '▶️ Devam Et' : '⏸️ Duraklat'; 
                                e.target.style.background = isPaused ? '#f59e0b' : 'rgba(255,255,255,0.1)'; 
                            });
                            
                            shadow.getElementById('stopBtn').addEventListener('click', (e) => { 
                                e.stopPropagation(); 
                                if (document.activeElement && typeof document.activeElement.blur === 'function') document.activeElement.blur();
                                setTimeout(() => { window.smartRecorderEmit(JSON.stringify({ actionType: 'StopControl' })); }, 300);
                            });

                            window.addEventListener('mousemove', (e) => {
                                const tooltip = shadow.getElementById('tooltip');
                                if (isPaused || !e.target || e.target.id === 'sr-widget-host' || host.contains(e.target)) { 
                                    tooltip.style.display = 'none'; return; 
                                }
                                const info = getElementInfo(e.target);
                                let selector = info.tag;
                                if (info.elementId) selector = '#' + info.elementId;
                                else if (info.placeholder) selector = 'placeholder=' + info.placeholder;
                                else if (info.textContent) selector = 'text=' + info.textContent;
                                tooltip.textContent = selector;
                                tooltip.style.left = (e.clientX + 12) + 'px';
                                tooltip.style.top = (e.clientY + 12) + 'px';
                                tooltip.style.display = 'block';
                            }, { capture: true, passive: true });
                        }, 500); 
                    }
                """);

                _isFirstPage = true; // Her kayıtta sıfırla

                _context.Page += (_, newPage) =>
                {
                    if (_isFirstPage) 
                    { 
                        _isFirstPage = false; 
                        return; 
                    }

                    _pageCounter++;
                    string alias = $"page{_pageCounter}";
                    _pageAliases[newPage] = alias; 
                    
                    // --- LOG EKLENDİ ---
                    Console.WriteLine($"[C# SENSEWRIGHT] YENİ SEKME YAKALANDI! Alias: {alias}, URL: {newPage.Url}");
                    System.Diagnostics.Debug.WriteLine($"[C# SENSEWRIGHT] YENİ SEKME YAKALANDI! Alias: {alias}");

                    OnActionRecorded?.Invoke(new TabOpenedAction 
                    { 
                        ActionType = "Tab Opened",
                        PageAlias = alias,
                        Timestamp = DateTime.Now
                    }); 
                    
                    AttachEventListenersToPage(newPage, alias); 
                };
                
                _page = await _context.NewPageAsync();
                _pageAliases.Clear();
                _pageAliases[_page] = "page"; // Ana sayfamız her zaman 'page' dir
                AttachEventListenersToPage(_page, "page");

                await _page.GotoAsync(targetUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HATA] StartRecordingAsync: {ex.Message}");
                await StopRecordingAsync();
                throw;
            }
        }

        private void AttachEventListenersToPage(IPage page, string alias)
        {
            page.Close += (_, _) => { 
                if (alias == "page") _ = Task.Run(async () => { await StopRecordingAsync(); OnRecordingStopped?.Invoke(); }); 
            };
            
            page.FrameNavigated += (_, frame) => {
                if (frame == page.MainFrame) 
                    OnActionRecorded?.Invoke(new NavigationAction { ActionType = "Navigation", Url = frame.Url, PageAlias = alias, Timestamp = DateTime.Now });
            };

            page.Response += (_, response) => {
                if (response.Request.ResourceType == "fetch" || response.Request.ResourceType == "xhr")
                    OnActionRecorded?.Invoke(new NetworkAction { ActionType = "Network Request", Url = response.Url, Method = response.Request.Method, StatusCode = response.Status, PageAlias = alias, Timestamp = DateTime.Now });
            };

            page.Dialog += (_, dialog) => {
                OnActionRecorded?.Invoke(new DialogAction { ActionType = "Browser Alert", DialogType = dialog.Type, Message = dialog.Message, PageAlias = alias, Timestamp = DateTime.Now });
                _ = Task.Run(async () => { try { await dialog.AcceptAsync(); } catch { } });
            };
        }

        public async Task StopRecordingAsync()
        {
            if (_context != null)
            {
                foreach (var p in _context.Pages)
                {
                    try 
                    { 
                        await p.EvaluateAsync("if (document.activeElement && typeof document.activeElement.blur === 'function') document.activeElement.blur();"); 
                        await Task.Delay(200); // Verinin C#'a ulaşması için çok kısa bir süre
                    } 
                    catch { /* Sayfa kapalıysa veya JS hatası verirse yoksay */ }
                }
            }
            if (_page != null) { await _page.CloseAsync(); _page = null; }
            if (_context != null) { await _context.CloseAsync(); _context = null; }
            if (_browser != null) { await _browser.CloseAsync(); _browser = null; }
            if (_playwright != null) { _playwright.Dispose(); _playwright = null; }
        }

        public async ValueTask DisposeAsync() { await StopRecordingAsync(); GC.SuppressFinalize(this); }
    }
}