using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlaywrightSmartRecorder.Core.Models;

namespace PlaywrightSmartRecorder.Parser
{
    public class TypeScriptGenerator
    {
        public string Generate(List<UserAction> originalActions)
        {
            // --- AKILLI ÇÖP TEMİZLEME VE SPAM FİLTRESİ ---
            var actions = new List<UserAction>();
            for (int i = 0; i < originalActions.Count; i++)
            {
                var current = originalActions[i];
                
                if (current is ClickAction currClick)
                {
                    // 1. SPAM KORUMASI: Peş peşe birebir aynı yere (Aynı CSS veya aynı Metin ile) tıklandıysa çöpe at!
                    if (actions.LastOrDefault() is ClickAction prevClick)
                    {
                        if (currClick.CssSelector == prevClick.CssSelector && currClick.TextContent == prevClick.TextContent)
                        {
                            continue; // Bu tıklamayı yoksay ve sonrakine geç
                        }
                    }

                    if (i < originalActions.Count - 1 && originalActions[i + 1] is ExtractAction ext)
                    {
                        if (currClick.CssSelector == ext.CssSelector || ext.CssSelector.Contains(currClick.CssSelector))
                        {
                            continue; // Bu tıklamayı yoksay
                        }
                    }

                    // 2. YANLIŞ TIKLAMA (MISCLICK) KORUMASI: Boşluğa tıklayıp sonra butona tıklama durumu
                    if (i < originalActions.Count - 1 && originalActions[i + 1] is ClickAction nextClick)
                    {
                        bool isRelated = false;

                        // Kural A: ID bazlı kapsayıcılık
                        if (!string.IsNullOrEmpty(currClick.ElementId) && nextClick.CssSelector.Contains(currClick.ElementId)) isRelated = true;
                        else if (!string.IsNullOrEmpty(nextClick.ElementId) && currClick.CssSelector.Contains(nextClick.ElementId)) isRelated = true;
                        
                        // Kural B: CSS Hiyerarşisi bazlı kapsayıcılık (Örn: div'e tıklayıp sonra div > button'a tıklamak)
                        else if (nextClick.CssSelector.Contains(currClick.CssSelector) && currClick.CssSelector.Length < nextClick.CssSelector.Length) isRelated = true;

                        if (isRelated)
                        {
                            continue; // Kapsayıcı (boşluk) tıklamasını çöpe at, hedefi daha net olan ikinci (asıl) tıklamaya geç!
                        }
                    }

                    // 3. SEÇİM (HIGHLIGHT) KORUMASI: Metni kopyalamak için seçerken oluşan o gereksiz tıklamayı (Click), 
                    // hemen ardındaki Hover veya Extract işlemiyle çakışıyorsa çöpe at.
                    if (i < originalActions.Count - 1)
                    {
                        var nextAction = originalActions[i + 1];
                        if ((nextAction is HoverAction nHover && nHover.CssSelector == currClick.CssSelector) || 
                            (nextAction is ExtractAction nExt && (nExt.CssSelector == currClick.CssSelector || nExt.CssSelector.Contains(currClick.CssSelector))))
                        {
                            continue; // Bu tıklamayı yoksay ve sadece Hover/Extract eylemlerini tut
                        }
                    }
                }
                
                // Filtrelerden sağlam çıkan aksiyonu listeye ekle
                actions.Add(current);
            }

            var sb = new StringBuilder();
            
            // TS ve Playwright importları
            sb.AppendLine("import { test, expect } from '@playwright/test';\n");
            sb.AppendLine("test('SenseWright Auto-Generated E2E Test', async ({ page, context }) => {");

            var declaredPages = new HashSet<string> { "page" };

            var dynamicVariables = new Dictionary<string, string>();
            int varCounter = 1;

            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                string p = action.PageAlias ?? "page";
                bool isFirstAppearance = !declaredPages.Contains(p);

                if (action is TabOpenedAction tabOpened)
                {
                    sb.AppendLine();
                    sb.AppendLine("    // Uygulamanın açtığı yeni sekmeyi (Pop-up) dinamik olarak yakala");
                    
                    // context.pages() dizisinin uzunluğu artana kadar (sekme oluşana kadar) bekle
                    sb.AppendLine($"    while (context.pages().length <= {declaredPages.Count}) {{");
                    sb.AppendLine($"        await page.waitForTimeout(100);");
                    sb.AppendLine($"    }}");
                    
                    sb.AppendLine($"    const {p} = context.pages()[context.pages().length - 1];");
                    sb.AppendLine($"    await {p}.waitForLoadState('domcontentloaded');");
                    declaredPages.Add(p);
                    continue; // Bu işlem bitti, döngüdeki bir sonraki aksiyona geç
                }

                else if (action is NavigationAction nav)
                {
                    // 1. Eğer bu testin en başındaki ilk sayfa yüklemesi ise mecburen "goto" kullanıyoruz.
                    if (i == 0)
                    {
                        sb.AppendLine($"    await {nav.PageAlias}.goto('{nav.Url}', {{ waitUntil: 'domcontentloaded' }});");
                    }
                    else
                    {
                        // 2. Testin ortasında gerçekleşen yönlendirmeler (Örn: Giriş Yap veya Gönder'e bastıktan sonraki sayfa değişimi)
                        // Playwright'ta butona bastıktan sonra goto kullanılmaz, "waitForURL" ile sayfanın yönlenmesi beklenir.
                        
                        // URL'nin sonunda dinamik bir ID var mı kontrol et (Örn: /APIGWCLIENTDEFINITION/10126585062)
                        var match = System.Text.RegularExpressions.Regex.Match(nav.Url, @"^(.*)/(\d+)[/#?]*$");
                        
                        if (match.Success)
                        {
                            // Dinamik bir numara varsa (Kayıt numarası), URL'nin sadece o numaraya kadar olan (Base) kısmını bekleriz
                            string baseUrl = match.Groups[1].Value;
                            sb.AppendLine($"    // Dinamik kayıt numarası saptandı. Yönlendirmenin tamamlanması bekleniyor...");
                            sb.AppendLine($"    await {nav.PageAlias}.waitForURL(url => url.href.includes('{baseUrl}'), {{ waitUntil: 'domcontentloaded' }});");
                        }
                        else
                        {
                            // Standart bir yönlendirmeyse (Örn: Login sonrası Index'e atması) tam URL'yi bekle
                            sb.AppendLine($"    await {nav.PageAlias}.waitForURL('{nav.Url}', {{ waitUntil: 'domcontentloaded' }});");
                        }
                    }
                }
                else if (action is HoverAction hover)
                {
                    string locator = BuildModernLocator(hover.Placeholder, hover.AriaLabel, hover.TextContent, hover.ElementId, hover.Tag, hover.Name, hover.CssSelector, hover.IsDynamicListElement, hover.CustomTestId);
                    
                    sb.AppendLine($"    // Tooltip/Pop-up açmak için farenin element üzerinde beklemesi (Hover)");
                    sb.AppendLine($"    await {hover.PageAlias}.{locator}.hover();");
                }
                else if (action is ExtractAction ext)
                {
                    string varName = $"dynamicUserVar_{varCounter++}";
                    dynamicVariables[ext.ExtractedValue] = varName; // Kopyalanan değeri hafızaya al (Örn: "TCBOZDEMIRCI" -> dynamicUserVar_1)
                    
                    // ÖNEMLİ: text parametresini zorla "" (boş) gönderiyoruz. 
                    // Çünkü yarın isim TCALIYILMAZ olduğunda Playwright'ın onu Text ile arayıp patlamaması, CSS yolundan bulması gerekir!
                    string locator = BuildModernLocator(ext.Placeholder, ext.AriaLabel, "", ext.ElementId, ext.Tag, ext.Name, ext.CssSelector, ext.IsDynamicListElement, ext.CustomTestId);
                    
                    sb.AppendLine($"\n    // Kullanıcının kopyaladığı metin dinamik olarak değişkene atanıyor");
                    sb.AppendLine($"    const {varName} = (await {ext.PageAlias}.{locator}.innerText()).trim();");
                }
                else if (action is InputAction input)
                {
                    string locator = BuildModernLocator(input.Placeholder, input.AriaLabel, input.TextContent, input.ElementId, input.Tag, input.Name, input.CssSelector, input.IsDynamicListElement, input.CustomTestId);
                    
                    // Kullanıcının yazdığı metin daha önce KOPYALADIĞI bir metin mi?
                    if (dynamicVariables.TryGetValue(input.Value, out string matchedVar))
                    {
                        sb.AppendLine($"    // Hafızadaki dinamik değişken alana dolduruluyor");
                        sb.AppendLine($"    await {input.PageAlias}.{locator}.fill({matchedVar});");
                    }
                    else
                    {
                        // Kopyalanmamış, kullanıcının klavyeden yazdığı normal metin
                        sb.AppendLine($"    await {input.PageAlias}.{locator}.fill('{Escape(input.Value)}');");
                    }
                }
                // 3. Tıklama (Click) Aksiyonları
                else if (action is ClickAction click)
                {
                    // ENTER (HAYALET CLICK) KORUMASI:
                    // Geriye dönük son 2 işleme bak, eğer kullanıcı 'Enter'a basmışsa 
                    // tarayıcının fırlattığı bu otomatik form submit tıklamasını yoksay!
                    bool isGhostClick = false;
                    for (int j = 1; j <= 2 && i - j >= 0; j++)
                    {
                        if (actions[i - j] is KeyboardAction prevKey && prevKey.Key == "Enter")
                        {
                            isGhostClick = true; 
                            break;
                        }
                    }

                    if (!isGhostClick)
                    {
                        string locator = BuildModernLocator(click.Placeholder, click.AriaLabel, click.TextContent, click.ElementId, click.Tag, click.Name, click.CssSelector, click.IsDynamicListElement, click.CustomTestId);
                        sb.AppendLine($"    await {p}.{locator}.click();");
                    }
                }

                // 5. Açılır Menü (Select/Option) Aksiyonları
                else if (action is SelectAction select)
                {
                    string locator = BuildModernLocator(select.Placeholder, select.AriaLabel, select.TextContent, select.ElementId, select.Tag, select.Name, select.CssSelector, select.IsDynamicListElement, select.CustomTestId);
                    sb.AppendLine($"    await {p}.{locator}.selectOption('{Escape(select.SelectedValue)}');");
                }
                // 6. Klavye (Tuş Basımı) Aksiyonları
                else if (action is KeyboardAction keyboard)
                {
                    string locator = BuildModernLocator(keyboard.Placeholder, keyboard.AriaLabel, keyboard.TextContent, keyboard.ElementId, keyboard.Tag, keyboard.Name, keyboard.CssSelector, keyboard.IsDynamicListElement, keyboard.CustomTestId);
                    sb.AppendLine($"    await {p}.{locator}.press('{keyboard.Key}');");
                }
                // 7. Doğrulama (Assert) Aksiyonları
                else if (action is AssertAction assert)
                {
                    string locator = BuildModernLocator(assert.Placeholder, assert.AriaLabel, assert.TextContent, assert.ElementId, assert.Tag, assert.Name, assert.CssSelector, assert.IsDynamicListElement, assert.CustomTestId);
                    sb.AppendLine($"    await expect({p}.{locator}).toBeVisible();");
                }
                
                // NOT: Eğer kendi özel 'NetworkRequestAction' ve 'Promise.all' mantığınız varsa 
                // o if bloğunu buraya (else if olarak) ekleyebilirsiniz.
            }

            sb.AppendLine("});");
            return sb.ToString();
        }

        private string BuildModernLocator(string placeholder, string ariaLabel, string text, string id, string tag, string name, string cssSelector, bool isDynamicListElement, string customTestId)
        {
            // 1. Kurumsal Özel Etiketler (En Yüksek Öncelik)
            if (!string.IsNullOrWhiteSpace(customTestId))
                return $"locator('[data-name=\"{Escape(customTestId)}\"], [data-testid=\"{Escape(customTestId)}\"]').first()";

            // 2. Element ID
            if (!string.IsNullOrWhiteSpace(id)) 
                return $"locator('#{Escape(id)}')";

            // 3. Placeholder (Inputlar için)
            if (!string.IsNullOrWhiteSpace(placeholder)) 
                return $"getByPlaceholder('{Escape(placeholder)}').first()";

            // 4. Aria Label (Erişilebilirlik etiketleri)
            if (!string.IsNullOrWhiteSpace(ariaLabel)) 
                return $"getByLabel('{Escape(ariaLabel)}').first()";

            if (!string.IsNullOrWhiteSpace(name)) 
                return $"locator('{tag}[name=\"{Escape(name)}\"]').first()";

            // 6. Görünür Metin ve DİNAMİK TABLO KORUMASI
            if (!string.IsNullOrWhiteSpace(text))
            {
                // Eğer etkileşime girilen yer bir tablo hücresiyse (td, th, tr) içindeki metni KESİNLİKLE kullanma!
                // Çünkü "TCABPELIT" veya "ONEDESK" gibi veriler dinamiktir, değişirse test patlar.
                // Metni yoksayarak doğrudan 7. adımdaki CSS Koordinatlarına (Örn: 2. Sütun) düşmesini sağlıyoruz.
                if (tag == "td" || tag == "th" || tag == "tr")
                {
                    // Text ile aramayı atla
                }
                else
                {
                    return $"locator('{tag}').filter({{ hasText: '{Escape(text)}' }}).first()";
                }
            }

            // 7. SON ÇARE: CSS Selector (Koordinat bazlı arama)
            return $"locator('{cssSelector}').first()";
        }

        /// <summary>
        /// Metinlerin içindeki tek tırnakları (') kaçış karakteriyle (\\) güvenli hale getirir.
        /// Örn: "O'Brien" -> "O\\'Brien"
        /// </summary>
        private string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("'", "\\'");
        }
    }
}