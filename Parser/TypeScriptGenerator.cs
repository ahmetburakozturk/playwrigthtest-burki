using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlaywrightSmartRecorder.Core.Models;

namespace PlaywrightSmartRecorder.Parser
{
    public class TypeScriptGenerator
    {
        public string Generate(List<UserAction> actions)
        {
            var sb = new StringBuilder();
            
            // TS ve Playwright importları
            sb.AppendLine("import { test, expect } from '@playwright/test';\n");
            sb.AppendLine("test('SenseWright Auto-Generated E2E Test', async ({ page, context }) => {");

            // Başlangıçta ana sayfayı (page) set ediyoruz.
            // Böylece "page1 = context.newPage()" gibi kodlar sadece GERÇEKTEN yeni bir sekme açıldığında üretilir.
            var declaredPages = new HashSet<string> { "page" };

            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                string p = action.PageAlias ?? "page";

                // Eğer aksiyon yeni bir sekmeden geliyorsa ve bu sekmeyi henüz tanımlamadıysak
                if (!declaredPages.Contains(p))
                {
                    sb.AppendLine();
                    sb.AppendLine("    // Yeni Sekme (Pop-up veya Manuel) Algılandı");
                    sb.AppendLine($"    const {p} = await context.newPage();");
                    declaredPages.Add(p);
                }

                // 1. Navigasyon (Sayfa Yönlendirme) Aksiyonları
                if (action is NavigationAction nav)
                {
                    // Kurumsal uygulamalardaki (SPA) sonsuz sayfa yüklenme (network) kilitlenmelerini aşmak için
                    // Playwright'ın varsayılan 'load' stratejisi yerine 'domcontentloaded' kullanıyoruz.
                    sb.AppendLine($"    await {p}.goto('{nav.Url}', {{ waitUntil: 'domcontentloaded' }});");
                }
                // 2. Metin Girişi (Input) Aksiyonları
                else if (action is InputAction input)
                {
                    // Çift Kayıt Filtresi: Araya Enter (KeyboardAction) girse bile geriye dönük 2 adıma bak
                    bool isDuplicate = false;
                    for (int j = 1; j <= 2 && i - j >= 0; j++)
                    {
                        if (actions[i - j] is InputAction prevInput && prevInput.ElementId == input.ElementId && prevInput.Value == input.Value)
                        {
                            isDuplicate = true; break;
                        }
                    }

                    if (!isDuplicate)
                    {
                        string locator = BuildModernLocator(input.Placeholder, input.AriaLabel, input.TextContent, input.ElementId, input.Tag, input.Name, input.CssSelector, input.IsDynamicListElement);
                        sb.AppendLine($"    await {p}.{locator}.fill('{Escape(input.Value)}');");
                    }
                }
                // 3. Tıklama (Click) Aksiyonları
                else if (action is ClickAction click)
                {
                    string locator = BuildModernLocator(click.Placeholder, click.AriaLabel, click.TextContent, click.ElementId, click.Tag, click.Name, click.CssSelector, click.IsDynamicListElement);
                    sb.AppendLine($"    await {p}.{locator}.click();");
                }
                // 4. Hover (Üzerine Gelme) Aksiyonları
                else if (action is HoverAction hover)
                {
                    // Kodu inanılmaz şişirdiği ve Playwright click yaparken otomatik hover yaptığı için bunu yoksayıyoruz.
                    // İsterseniz ileride yoruma alabilirsiniz: sb.AppendLine($"    // await {p}.{locator}.hover();");
                }
                // 5. Açılır Menü (Select/Option) Aksiyonları
                else if (action is SelectAction select)
                {
                    string locator = BuildModernLocator(select.Placeholder, select.AriaLabel, select.TextContent, select.ElementId, select.Tag, select.Name, select.CssSelector, select.IsDynamicListElement);
                    sb.AppendLine($"    await {p}.{locator}.selectOption('{Escape(select.SelectedValue)}');");
                }
                // 6. Klavye (Tuş Basımı) Aksiyonları
                else if (action is KeyboardAction keyboard)
                {
                    string locator = BuildModernLocator(keyboard.Placeholder, keyboard.AriaLabel, keyboard.TextContent, keyboard.ElementId, keyboard.Tag, keyboard.Name, keyboard.CssSelector, keyboard.IsDynamicListElement);
                    sb.AppendLine($"    await {p}.{locator}.press('{keyboard.Key}');");
                }
                // 7. Doğrulama (Assert) Aksiyonları
                else if (action is AssertAction assert)
                {
                    string locator = BuildModernLocator(assert.Placeholder, assert.AriaLabel, assert.TextContent, assert.ElementId, assert.Tag, assert.Name, assert.CssSelector, assert.IsDynamicListElement);
                    sb.AppendLine($"    await expect({p}.{locator}).toBeVisible();");
                }
                
                // NOT: Eğer kendi özel 'NetworkRequestAction' ve 'Promise.all' mantığınız varsa 
                // o if bloğunu buraya (else if olarak) ekleyebilirsiniz.
            }

            sb.AppendLine("});");
            return sb.ToString();
        }
        
        private string BuildModernLocator(string placeholder, string ariaLabel, string text, string id, string tag, string name, string cssSelector, bool isDynamicListElement)
        {
            // 1. Öncelik: ID
            if (!string.IsNullOrWhiteSpace(id)) return $"locator('#{Escape(id)}')";
            // 2. Öncelik: Placeholder
            if (!string.IsNullOrWhiteSpace(placeholder)) return $"getByPlaceholder('{Escape(placeholder)}')";
            // 3. Öncelik: Aria-Label
            if (!string.IsNullOrWhiteSpace(ariaLabel)) return $"getByLabel('{Escape(ariaLabel)}')";
            // 4. Öncelik: Name Attribute
            if (!string.IsNullOrWhiteSpace(name)) return $"locator('[name=\"{Escape(name)}\"]')".Replace("''", "'");

            // --- YENİ EKLENEN DİNAMİK LİSTE MANTIĞI ---
            // 5. Öncelik: Eğer element bir tablo satırı (tr) veya liste (li) İÇİNDEYSE,
            // metin değişken olabileceği için yapısal CSS seçiciyi (nth-of-type) tercih et.
            if (isDynamicListElement && !string.IsNullOrWhiteSpace(cssSelector))
            {
                return $"locator('{Escape(cssSelector)}')";
            }

            // 6. Öncelik: Görünür Metin (Text) - Statik menüler ve butonlar için
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (tag == "button") return $"locator('button').filter({{ hasText: '{Escape(text)}' }}).first()";
                if (tag == "a") return $"locator('a').filter({{ hasText: '{Escape(text)}' }}).first()";
                return $"getByText('{Escape(text)}').first()";
            }

            // 7. Son Çare: Fallback CssSelector
            if (!string.IsNullOrWhiteSpace(cssSelector)) return $"locator('{Escape(cssSelector)}')";

            return $"locator('{tag}').first()";
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