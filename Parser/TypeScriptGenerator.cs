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
            
            // --- KUSURSUZ ÇÖP TEMİZLEME VE SPAM FİLTRESİ ---
            var actions = new List<UserAction>();
            for (int i = 0; i < originalActions.Count; i++)
            {
                var current = originalActions[i];
                
                if (current is ClickAction currClick)
                {
                    // 1. Spam Koruması (Aynı yere peş peşe tıklama)
                    if (actions.LastOrDefault() is ClickAction prevClick)
                    {
                        if (currClick.CssSelector == prevClick.CssSelector && currClick.TextContent == prevClick.TextContent)
                            continue;
                    }

                    // 2. Kopyalama (Seçim) Tıklaması Koruması
                    if (i < originalActions.Count - 1 && originalActions[i + 1] is ExtractAction ext)
                    {
                        if (currClick.CssSelector == ext.CssSelector || ext.CssSelector.Contains(currClick.CssSelector))
                            continue;
                    }

                    // 3. TABLO ZIRHI: Tablo veya listeye tıklandıysa bu asla bir Misclick (yanlış tıklama) olamaz, her zaman koru!
                    bool isTableElement = currClick.Tag == "td" || currClick.Tag == "tr" || currClick.Tag == "th" || currClick.Tag == "li";
                    
                    // 4. Misclick Koruması (Boşluğa tıklayıp sonra butona tıklama durumu)
                    if (!isTableElement && i < originalActions.Count - 1 && originalActions[i + 1] is ClickAction nextClick)
                    {
                        bool isRelated = false;
                        if (!string.IsNullOrEmpty(currClick.ElementId) && nextClick.CssSelector.Contains(currClick.ElementId)) isRelated = true;
                        else if (!string.IsNullOrEmpty(nextClick.ElementId) && currClick.CssSelector.Contains(nextClick.ElementId)) isRelated = true;
                        else if (nextClick.CssSelector.Contains(currClick.CssSelector) && currClick.CssSelector.Length < nextClick.CssSelector.Length) isRelated = true;

                        if (isRelated) continue; 
                    }
                }
                else if (current is InputAction currInput)
                {
                    // Tekrarlayan Input (Enter yankısı) Koruması
                    var lastInput = actions.LastOrDefault(a => a is InputAction) as InputAction;
                    if (lastInput != null && lastInput.CssSelector == currInput.CssSelector && lastInput.Value == currInput.Value)
                        continue;
                }
                else if (current is HoverAction currHover)
                {
                    // Peş peşe Hover (Ekranda gezinme) Koruması
                    if (i < originalActions.Count - 1 && originalActions[i + 1] is HoverAction)
                        continue;
                    
                    // Hover yapıp ardından tıklama yapıldıysa Hover'ı sil
                    if (i < originalActions.Count - 1 && originalActions[i + 1] is ClickAction nextClick)
                    {
                        if (nextClick.CssSelector == currHover.CssSelector || nextClick.CssSelector.Contains(currHover.CssSelector))
                            continue;
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
                    string locator = BuildModernLocator(hover.Placeholder, hover.AriaLabel, hover.TextContent, hover.ElementId, hover.Tag, hover.Name, hover.CssSelector, hover.IsDynamicListElement, hover.CustomTestId, "Hover");
                    
                    sb.AppendLine($"    // Tooltip/Pop-up açmak için farenin element üzerinde beklemesi (Hover)");
                    sb.AppendLine($"    await {hover.PageAlias}.{locator}.hover();");
                }
                else if (action is ExtractAction ext)
                {
                    string varName = $"dynamicUserVar_{varCounter++}";
                    dynamicVariables[ext.ExtractedValue] = varName; // Kopyalanan değeri hafızaya al (Örn: "TCBOZDEMIRCI" -> dynamicUserVar_1)
                    
                    // ÖNEMLİ: text parametresini zorla "" (boş) gönderiyoruz. 
                    // Çünkü yarın isim TCALIYILMAZ olduğunda Playwright'ın onu Text ile arayıp patlamaması, CSS yolundan bulması gerekir!
                    string locator = BuildModernLocator(ext.Placeholder, ext.AriaLabel, "", ext.ElementId, ext.Tag, ext.Name, ext.CssSelector, ext.IsDynamicListElement, ext.CustomTestId, "Extract");
                    
                    sb.AppendLine($"\n    // Kullanıcının kopyaladığı metin dinamik olarak değişkene atanıyor");
                    sb.AppendLine($"    const {varName} = (await {ext.PageAlias}.{locator}.innerText()).trim();");
                }
                else if (action is InputAction input)
                {
                    string locator = BuildModernLocator(input.Placeholder, input.AriaLabel, input.TextContent, input.ElementId, input.Tag, input.Name, input.CssSelector, input.IsDynamicListElement, input.CustomTestId, "Input");
                    
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
                        string locator = BuildModernLocator(click.Placeholder, click.AriaLabel, click.TextContent, click.ElementId, click.Tag, click.Name, click.CssSelector, click.IsDynamicListElement, click.CustomTestId, "Click");
                        sb.AppendLine($"    await {p}.{locator}.click();");
                    }
                }

                // 5. Açılır Menü (Select/Option) Aksiyonları
                else if (action is SelectAction select)
                {
                    string locator = BuildModernLocator(select.Placeholder, select.AriaLabel, select.TextContent, select.ElementId, select.Tag, select.Name, select.CssSelector, select.IsDynamicListElement, select.CustomTestId, "Select");
                    sb.AppendLine($"    await {p}.{locator}.selectOption('{Escape(select.SelectedValue)}');");
                }
                // 6. Klavye (Tuş Basımı) Aksiyonları
                else if (action is KeyboardAction keyboard)
                {
                    string locator = BuildModernLocator(keyboard.Placeholder, keyboard.AriaLabel, keyboard.TextContent, keyboard.ElementId, keyboard.Tag, keyboard.Name, keyboard.CssSelector, keyboard.IsDynamicListElement, keyboard.CustomTestId, "Keyboard");
                    sb.AppendLine($"    await {p}.{locator}.press('{keyboard.Key}');");
                }
                // 7. Doğrulama (Assert) Aksiyonları
                else if (action is AssertAction assert)
                {
                    string locator = BuildModernLocator(assert.Placeholder, assert.AriaLabel, assert.TextContent, assert.ElementId, assert.Tag, assert.Name, assert.CssSelector, assert.IsDynamicListElement, assert.CustomTestId, "Assert");
                    sb.AppendLine($"    await expect({p}.{locator}).toBeVisible();");
                }
                
                // NOT: Eğer kendi özel 'NetworkRequestAction' ve 'Promise.all' mantığınız varsa 
                // o if bloğunu buraya (else if olarak) ekleyebilirsiniz.
            }

            sb.AppendLine("});");
            return sb.ToString();
        }

        private string BuildModernLocator(string placeholder, string ariaLabel, string text, string id, string tag, string name, string cssSelector, bool isDynamicListElement, string customTestId, string actionType)
        {
            if (!string.IsNullOrWhiteSpace(customTestId))
                return $"locator('[data-name=\"{Escape(customTestId)}\"], [data-testid=\"{Escape(customTestId)}\"]').first()";

            if (!string.IsNullOrWhiteSpace(id)) 
                return $"locator('#{Escape(id)}')";

            if (!string.IsNullOrWhiteSpace(placeholder)) 
                return $"getByPlaceholder('{Escape(placeholder)}').first()";

            if (!string.IsNullOrWhiteSpace(ariaLabel)) 
                return $"getByLabel('{Escape(ariaLabel)}').first()";

            if (!string.IsNullOrWhiteSpace(name)) 
                return $"locator('{tag}[name=\"{Escape(name)}\"]').first()";

            if (!string.IsNullOrWhiteSpace(text))
            {
                // YENİ YAPAY ZEKA KURALI: 
                // Eğer Hover veya Extract yapılıyorsa ve bu bir tablo hücresiyse (Dinamik kullanıcı adları gibi), metne güvenme!
                // Ama Click yapılıyorsa (Arama listesinden servis seçimi gibi), kesinlikle metne güven!
                if ((actionType == "Hover" || actionType == "Extract") && (tag == "td" || tag == "th" || tag == "tr"))
                {
                    // Text'i yoksayarak cssSelector (koordinat) yöntemine düşmesini sağla
                }
                else
                {
                    return $"locator('{tag}').filter({{ hasText: '{Escape(text)}' }}).first()";
                }
            }

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