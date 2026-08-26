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

                        string pageAlias = source.Page == _page ? "page" : $"page{_pageCounter}";

                        UserAction? action = actionType switch
                        {
                            "Click" => JsonSerializer.Deserialize<ClickAction>(payload, options) switch { var a when a != null => a with { PageAlias = pageAlias }, _ => null },
                            "Hover" => JsonSerializer.Deserialize<HoverAction>(payload, options) switch { var a when a != null => a with { PageAlias = pageAlias }, _ => null },
                            "Input" => JsonSerializer.Deserialize<InputAction>(payload, options) switch { var a when a != null => a with { PageAlias = pageAlias }, _ => null },
                            "Select" => JsonSerializer.Deserialize<SelectAction>(payload, options) switch { var a when a != null => a with { PageAlias = pageAlias }, _ => null },
                            "Assert" => JsonSerializer.Deserialize<AssertAction>(payload, options) switch { var a when a != null => a with { PageAlias = pageAlias }, _ => null },
                            "Keyboard" => JsonSerializer.Deserialize<KeyboardAction>(payload, options) switch { var a when a != null => a with { PageAlias = pageAlias }, _ => null },
                            _ => null
                        };

                        if (action != null) OnActionRecorded?.Invoke(action);
                    }
                    catch (Exception ex) { Debug.WriteLine($"[JSON PARSE HATA] {ex.Message}"); }
                });

                // 3. V8 AJANI (WIDGET, TOOLTIP, ASSERT MODU VE TÜM EVENT DİNLEYİCİLERİ)
                await _context.AddInitScriptAsync("""
                    if (!window.__smartRecorderInitialized) {
                        window.__smartRecorderInitialized = true;
                        
                        let isPaused = false;
                        let isAssertMode = false;

                        // --- YENİ: ELEMENTİN EKRANDAKİ SIRASINI/YAPISINI HESAPLAYAN FONKSİYON ---
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
                            return {
                                tag: el.tagName.toLowerCase(),
                                elementId: el.id || '',
                                textContent: (el.innerText || '').substring(0, 50).trim(),
                                placeholder: el.placeholder || '',
                                ariaLabel: el.getAttribute('aria-label') || '',
                                name: el.name || '',
                                // Yeni eklenen özellikler JS payload'una dahil ediliyor
                                cssSelector: getCssPath(el),
                                isDynamicListElement: !!el.closest('tr, li') // Element bir tablo satırı veya liste içindeyse true döner
                            };
                        };

                        window.addEventListener('DOMContentLoaded', () => {
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
                                    <button class="btn" id="assertBtn" title="Hedef elemanın ekrandaki varlığını doğrular">🎯 Doğrula</button>
                                    <button class="btn" id="pauseBtn">⏸️ Duraklat</button>
                                    <button class="btn btn-stop" id="stopBtn">⏹️ Bitir</button>
                                </div>
                                <div id="tooltip" class="sr-tooltip"></div>
                            `;
                            document.documentElement.appendChild(host);

                            const assertBtn = shadow.getElementById('assertBtn');
                            const pauseBtn = shadow.getElementById('pauseBtn');
                            const tooltip = shadow.getElementById('tooltip');

                            assertBtn.addEventListener('click', (e) => { 
                                e.stopPropagation(); 
                                isAssertMode = !isAssertMode; 
                                assertBtn.style.background = isAssertMode ? '#8b5cf6' : 'rgba(255,255,255,0.1)'; 
                                assertBtn.innerHTML = isAssertMode ? '🎯 Seçiliyor...' : '🎯 Doğrula'; 
                            });
                            
                            pauseBtn.addEventListener('click', (e) => { 
                                e.stopPropagation(); 
                                isPaused = !isPaused; 
                                pauseBtn.innerHTML = isPaused ? '▶️ Devam Et' : '⏸️ Duraklat'; 
                                pauseBtn.style.background = isPaused ? '#f59e0b' : 'rgba(255,255,255,0.1)'; 
                            });
                            
                            shadow.getElementById('stopBtn').addEventListener('click', (e) => { 
                                e.stopPropagation(); 
                                
                                // Aktif bir input varsa, ondan zorla çıkış (blur) yap ki son yazılan veri 'change' eventi ile kayda geçsin!
                                if (document.activeElement && typeof document.activeElement.blur === 'function') { 
                                    document.activeElement.blur(); 
                                }
                                
                                // C# tarafına verinin gitmesi için 300ms süre tanı, ardından kaydı bitir
                                setTimeout(() => {
                                    window.smartRecorderEmit(JSON.stringify({ actionType: 'StopControl' })); 
                                }, 300);
                            });

                            document.addEventListener('mousemove', (e) => {
                                if (isPaused || !e.target) { tooltip.style.display = 'none'; return; }
                                if (e.target.id === 'sr-widget-host' || host.contains(e.target)) { tooltip.style.display = 'none'; return; }
                                
                                const info = getElementInfo(e.target);
                                let selector = info.tag;
                                if (info.elementId) selector = '#' + info.elementId;
                                else if (info.placeholder) selector = 'placeholder=' + info.placeholder;
                                else if (info.textContent) selector = 'text=' + info.textContent;
                                
                                tooltip.textContent = selector;
                                tooltip.style.left = (e.clientX + 12) + 'px';
                                tooltip.style.top = (e.clientY + 12) + 'px';
                                tooltip.style.display = 'block';
                            }, true);
                        });

                        document.addEventListener('change', (e) => {
                            if (isPaused) return;
                            const target = e.target;
                            const info = getElementInfo(target);
                            
                            if (target.tagName === 'SELECT') {
                                window.smartRecorderEmit(JSON.stringify({ actionType: 'Select', ...info, selectedValue: target.value }));
                            } else if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') {
                                if (target.type === 'checkbox' || target.type === 'radio') return;
                                window.smartRecorderEmit(JSON.stringify({ actionType: 'Input', ...info, value: target.value }));
                            }
                        }, true);
                        
                        // KLAVYE (ENTER / ESCAPE) DİNLEYİCİSİ
                        document.addEventListener('keydown', (e) => {
                            if (isPaused) return;
                            
                            if (e.key === 'Enter' || e.key === 'Escape') {
                                const target = e.target;
                                const info = getElementInfo(target);
                                
                                // Kullanıcı Input içindeyken Enter'a basarsa, önce yazdığı değeri kaydet!
                                if (e.key === 'Enter' && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA')) {
                                    window.smartRecorderEmit(JSON.stringify({ 
                                        actionType: 'Input', 
                                        ...info, 
                                        value: target.value 
                                    }));
                                }
                                
                                // Sonra tuş basımını kaydet
                                window.smartRecorderEmit(JSON.stringify({ 
                                    actionType: 'Keyboard', 
                                    key: e.key, 
                                    ...info 
                                }));
                            }
                        }, true);

                        document.addEventListener('click', (e) => {
                            if (isPaused || e.target.closest('#sr-widget-host')) return;
                            const info = getElementInfo(e.target);

                            if (isAssertMode) {
                                e.preventDefault(); e.stopPropagation();
                                window.smartRecorderEmit(JSON.stringify({ actionType: 'Assert', ...info }));
                                isAssertMode = false;
                                document.getElementById('sr-widget-host').shadowRoot.getElementById('assertBtn').style.background = 'rgba(255,255,255,0.1)';
                                document.getElementById('sr-widget-host').shadowRoot.getElementById('assertBtn').innerHTML = '🎯 Doğrula';
                                return;
                            }
                            window.smartRecorderEmit(JSON.stringify({ actionType: 'Click', ...info }));
                        }, true);

                        document.addEventListener('mouseenter', (e) => {
                            if (isPaused || e.target.closest('#sr-widget-host')) return;
                            const target = e.target;
                            const isMenu = target.tagName === 'A' || target.tagName === 'BUTTON' || target.classList.contains('dropdown');
                            if (isMenu) {
                                const info = getElementInfo(target);
                                window.smartRecorderEmit(JSON.stringify({ actionType: 'Hover', ...info }));
                            }
                        }, true);
                    }
                """);

                _isFirstPage = true; // Her kayıtta sıfırla

                // 4. YENİ SEKMELERİ (POP-UP / YENİ TAB) YAKALAMA
                _context.Page += (_, newPage) =>
                {
                    // Eğer bu açılan ilk ana sayfaysa, onu "Yeni Sekme" olarak sayma ve atla!
                    if (_isFirstPage) 
                    { 
                        _isFirstPage = false; 
                        return; 
                    }

                    _pageCounter++;
                    string alias = $"page{_pageCounter}";
                    AttachEventListenersToPage(newPage, alias);
                };
                
                _page = await _context.NewPageAsync();
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
            if (_page != null) { await _page.CloseAsync(); _page = null; }
            if (_context != null) { await _context.CloseAsync(); _context = null; }
            if (_browser != null) { await _browser.CloseAsync(); _browser = null; }
            if (_playwright != null) { _playwright.Dispose(); _playwright = null; }
        }

        public async ValueTask DisposeAsync() { await StopRecordingAsync(); GC.SuppressFinalize(this); }
    }
}