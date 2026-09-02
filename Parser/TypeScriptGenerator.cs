using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlaywrightSmartRecorder.Core.Models;

namespace PlaywrightSmartRecorder.Parser
{
    public class TypeScriptGenerator
    {
        // ====================================================================
        // GENERATE
        // ====================================================================

        public string Generate(
            List<UserAction> originalActions)
        {
            // ================================================================
            // ORIGINAL ORDER
            // ================================================================

            var actions =
                new List<UserAction>(
                    originalActions ??
                    new List<UserAction>());

            
            // ================================================================
            // EVENT RACE CONDITION FIX
            // ================================================================
            actions = ReorderEventRaceConditions(actions);

            // ================================================================
            // CLEANUP
            // ================================================================
            actions = CleanupActions(actions);

            // ================================================================
            // TYPESCRIPT HEADER
            // ================================================================
            var sb = new StringBuilder();

            sb.AppendLine("import { expect, test, reportStepInfo, reportStepPass, reportStepFail } from '@turkcell/playwright-framework';");
            sb.AppendLine("import dotenv from 'dotenv';");
            sb.AppendLine();
            sb.AppendLine("dotenv.config();");
            sb.AppendLine();
            sb.AppendLine("test('SenseWright Auto-Generated E2E Test', async ({ page, context }) => {");
            sb.AppendLine("    test.setTimeout(600_000); // 10 dakikalık genel timeout");
            sb.AppendLine();
            
            sb.AppendLine("    try {");

            // ================================================================
            // PAGE STATE
            // ================================================================

            var declaredPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "page" };
            var firstNavigationByPage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lastRecordedUrlByPage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string lastGeneratedPageAlias = "page";

            // ================================================================
            // DYNAMIC VARIABLES
            // ================================================================

            var dynamicVariables = new Dictionary<string, string>();
            int varCounter = 1;

            // ================================================================
            // NAVIGATION
            // ================================================================

            int navigationCounter = 1;

            // ================================================================
            // ACTION LOOP
            // ================================================================

            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                string p = string.IsNullOrWhiteSpace(action.PageAlias) ? "page" : action.PageAlias;

                // ============================================================
                // TAB OPENED
                // ============================================================

                if (action is TabOpenedAction tabOpened)
                {
                    sb.AppendLine();
                    sb.AppendLine("        // Uygulamanın açtığı yeni sekmeyi dinamik olarak yakala");
                    sb.AppendLine($"        while (context.pages().length <= {declaredPages.Count}) {{");
                    sb.AppendLine("            await page.waitForTimeout(100);");
                    sb.AppendLine("        }");
                    sb.AppendLine($"        const {p} = context.pages()[context.pages().length - 1];");
                    sb.AppendLine($"        await {p}.waitForLoadState('domcontentloaded');");

                    declaredPages.Add(p);
                    lastGeneratedPageAlias = p;
                    continue;
                }

                // ============================================================
                // TAB ACTIVATED
                // ============================================================

                if (action is TabActivatedAction)
                {
                    if (!string.Equals(lastGeneratedPageAlias, p, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine();
                        sb.AppendLine($"        // Kullanıcı browser sekmeleri arasında {p} sekmesine geçti.");
                        sb.AppendLine($"        await {p}.bringToFront();");
                        lastGeneratedPageAlias = p;
                    }
                    continue;
                }

                // ============================================================
                // PAGE ALIAS CHANGE
                // ============================================================

                if (!string.Equals(lastGeneratedPageAlias, p, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine();
                    sb.AppendLine($"        // Kullanıcı browser sekmeleri arasında {p} sekmesine geçti.");
                    sb.AppendLine($"        await {p}.bringToFront();");
                    lastGeneratedPageAlias = p;
                }

                // ============================================================
                // NAVIGATION
                // ============================================================

                if (action is NavigationAction nav)
                {
                    string navPage = string.IsNullOrWhiteSpace(nav.PageAlias) ? "page" : nav.PageAlias;

                    if (nav.NavigationKind.Equals("Initial", StringComparison.OrdinalIgnoreCase))
                    {
                        string origin = GetOrigin(nav.Url);
                        string relativeUrl = GetRelativeUrl(nav.Url);

                        sb.AppendLine();
                        sb.AppendLine($"        const baseUrl = (process.env.BASE_URL ?? '{Escape(origin)}').replace(/\\/+$/, '');");
                        sb.AppendLine();
                        sb.AppendLine("        // Test başlangıç sayfasına gidiliyor.");
                        sb.AppendLine($"        await {navPage}.goto(new URL('{Escape(relativeUrl)}', baseUrl).toString(), {{ waitUntil: 'load' }});");

                        firstNavigationByPage.Add(navPage);
                        lastRecordedUrlByPage[navPage] = nav.Url;
                        continue;
                    }

                    if (!firstNavigationByPage.Contains(navPage))
                    {
                        firstNavigationByPage.Add(navPage);
                        lastRecordedUrlByPage[navPage] = nav.Url;
                        continue;
                    }

                    if (nav.NavigationKind.Equals("UserAction", StringComparison.OrdinalIgnoreCase) || 
                        nav.NavigationKind.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
                    {
                        lastRecordedUrlByPage[navPage] = nav.Url;
                        continue;
                    }

                    if (nav.NavigationKind.Equals("Reload", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine();
                        sb.AppendLine("        // Kullanıcı sayfayı yeniledi.");
                        sb.AppendLine($"        await {navPage}.reload({{ waitUntil: 'load' }});");
                        lastRecordedUrlByPage[navPage] = nav.Url;
                        continue;
                    }

                    if (nav.NavigationKind.Equals("History", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine();
                        sb.AppendLine("        // Browser history navigation gerçekleşti.");
                        sb.AppendLine($"        await {navPage}.waitForLoadState('load');");
                        lastRecordedUrlByPage[navPage] = nav.Url;
                        continue;
                    }

                    if (nav.NavigationKind.Equals("Manual", StringComparison.OrdinalIgnoreCase))
                    {
                        string manualUrl = !string.IsNullOrWhiteSpace(nav.UserTypedUrl) ? nav.UserTypedUrl : nav.Url;
                        string relativeUrl = GetRelativeUrl(manualUrl);

                        sb.AppendLine();
                        sb.AppendLine("        // Kullanıcı browser adres çubuğundan manuel olarak URL değiştirdi.");
                        sb.AppendLine($"        await {navPage}.goto(new URL('{Escape(relativeUrl)}', {navPage}.url()).toString(), {{ waitUntil: 'load' }});");
                        lastRecordedUrlByPage[navPage] = manualUrl;
                        continue;
                    }

                    sb.AppendLine();
                    sb.AppendLine("        // Navigation kaynağı güvenilir şekilde belirlenemedi; mevcut dokümanın yüklenmesi bekleniyor.");
                    sb.AppendLine($"        await {navPage}.waitForLoadState('load');");
                    lastRecordedUrlByPage[navPage] = nav.Url;
                    continue;
                }

                // ============================================================
                // HOVER
                // ============================================================

                if (action is HoverAction hover)
                {
                    string elemDesc = GetElementDescription(hover.Placeholder, hover.AriaLabel, hover.TextContent, hover.ElementId, hover.CssSelector, hover.Tag);
                    sb.AppendLine();
                    sb.AppendLine($"        await reportStepInfo('Hover: {elemDesc} üzerine geliniyor.');");

                    string locator = BuildModernLocator(
                        hover.Placeholder, hover.AriaLabel, hover.TextContent, hover.ElementId, hover.Tag, hover.Name,
                        hover.CssSelector, hover.IsDynamicListElement, hover.CustomTestId, "Hover", dynamicVariables);

                    sb.AppendLine("        // Tooltip/Pop-up açmak için element üzerinde hover");
                    sb.AppendLine($"        await {hover.PageAlias}.{locator}.hover({{ timeout: 2000 }}).catch(() => {{}});");
                    continue;
                }

                // ============================================================
                // EXTRACT
                // ============================================================

                if (action is ExtractAction ext)
                {
                    string elemDesc = GetElementDescription(ext.Placeholder, ext.AriaLabel, "", ext.ElementId, ext.CssSelector, ext.Tag);
                    sb.AppendLine();
                    sb.AppendLine($"        await reportStepInfo('Veri Okuma: {elemDesc} alanından veri kopyalanıyor.');");

                    string varName = $"dynamicUserVar_{varCounter++}";
                    dynamicVariables[ext.ExtractedValue] = varName;

                    string locator = BuildModernLocator(
                        ext.Placeholder, ext.AriaLabel, "", ext.ElementId, ext.Tag, ext.Name,
                        ext.CssSelector, ext.IsDynamicListElement, ext.CustomTestId, "Extract", dynamicVariables);

                    GenerateExtraction(sb, ext, locator, varName);
                    continue;
                }

                // ============================================================
                // INPUT
                // ============================================================

                if (action is InputAction input)
                {
                    string elemDesc = GetElementDescription(input.Placeholder, input.AriaLabel, input.TextContent, input.ElementId, input.CssSelector, input.Tag);
                    sb.AppendLine();
                    sb.AppendLine($"        await reportStepInfo('Veri Girişi: {elemDesc} alanına veri yazılıyor.');");

                    string locator = BuildModernLocator(
                        input.Placeholder, input.AriaLabel, input.TextContent, input.ElementId, input.Tag, input.Name,
                        input.CssSelector, input.IsDynamicListElement, input.CustomTestId, "Input");

                    string cleanInputValue = input.Value != null ? input.Value.Trim() : "";

                    if (dynamicVariables.TryGetValue(cleanInputValue, out string matchedVar))
                    {
                        sb.AppendLine("        // Hafızadaki dinamik değişken alana dolduruluyor");
                        sb.AppendLine($"        await {input.PageAlias}.{locator}.fill({matchedVar});");
                    }
                    else
                    {
                        sb.AppendLine($"        await {input.PageAlias}.{locator}.fill('{Escape(input.Value)}');");
                    }
                    continue;
                }

                // ============================================================
                // CLICK
                // ============================================================

                if (action is ClickAction click)
                {
                    string elemDesc = GetElementDescription(click.Placeholder, click.AriaLabel, click.TextContent, click.ElementId, click.CssSelector, click.Tag);
                    sb.AppendLine();
                    sb.AppendLine($"        await reportStepInfo('Tıklama: {elemDesc} elementine tıklanıyor.');");

                    bool isSearchDrivenSelection = click.IsDynamicListElement && (click.Tag == "td" || click.Tag == "th") &&
                                                   click.RowIndex >= 0 && WasPrecededBySearchEnter(actions, i);

                    bool followsNavigation = NextActionIsNavigation(actions, i, click.PageAlias);

                    if (isSearchDrivenSelection)
                    {
                        string tableScope = !string.IsNullOrWhiteSpace(click.ParentTableId) ? $"#{Escape(click.ParentTableId)} tbody tr" : "tbody tr";

                        if (followsNavigation)
                        {
                            string promise = $"navigationPromise_{navigationCounter++}";
                            EmitNavigationWaitStart(sb, p, promise);
                            sb.AppendLine($"        // Arama sonrası dinamik listeden pozisyona göre seçim (kaydedilen satır index: {click.RowIndex})");
                            sb.AppendLine($"        await {p}.locator('{tableScope}').nth({click.RowIndex}).click();");
                            EmitNavigationWaitEnd(sb, p, promise);
                        }
                        else
                        {
                            sb.AppendLine($"        // Arama sonrası dinamik listeden pozisyona göre seçim (kaydedilen satır index: {click.RowIndex})");
                            sb.AppendLine($"        await {p}.locator('{tableScope}').nth({click.RowIndex}).click();");
                        }
                        continue;
                    }

                    string clickLocator = BuildModernLocator(
                        click.Placeholder, click.AriaLabel, click.TextContent, click.ElementId, click.Tag, click.Name,
                        click.CssSelector, click.IsDynamicListElement, click.CustomTestId, "Click", dynamicVariables);

                    NetworkAction triggeredNetwork = !followsNavigation ? GetTriggeredNetworkAction(actions, i, click.PageAlias) : null;

                    if (followsNavigation)
                    {
                        string promise = $"navigationPromise_{navigationCounter++}";
                        EmitNavigationWaitStart(sb, p, promise);
                        sb.AppendLine($"        await {p}.{clickLocator}.click();");
                        EmitNavigationWaitEnd(sb, p, promise);
                    }
                    else if (triggeredNetwork != null)
                    {
                        string apiPath = Uri.TryCreate(triggeredNetwork.Url, UriKind.Absolute, out Uri apiUri) ? apiUri.AbsolutePath : triggeredNetwork.Url;
                        string promise = $"networkPromise_{navigationCounter++}";

                        sb.AppendLine($"        // Tıklamanın tetiklediği spesifik API isteğini ({triggeredNetwork.Method} {apiPath}) yakalamak için promise oluşturuluyor.");
                        sb.AppendLine($"        const {promise} = {p}.waitForResponse(resp => resp.url().includes('{Escape(apiPath)}') && resp.request().method() === '{Escape(triggeredNetwork.Method)}', {{ timeout: 25000 }}).catch(() => {{}});");                        
                        sb.AppendLine($"        await {p}.{clickLocator}.click();");
                        sb.AppendLine($"        await {promise};");
                        sb.AppendLine($"        // İşlem sonrası tetiklenen ardışık veri güncellemelerinin bitmesini bekliyoruz.");
                        sb.AppendLine($"        await {p}.waitForLoadState('networkidle', {{ timeout: 25000 }}).catch(() => {{}});");
                        sb.AppendLine($"        // Ön yüzün DOM'u tam çizmesi için esneklik payı");
                        sb.AppendLine($"        await {p}.waitForTimeout(1500);");
                    }
                    else
                    {
                        sb.AppendLine($"        await {p}.{clickLocator}.click();");
                    }
                    continue;
                }

                // ============================================================
                // SELECT
                // ============================================================

                if (action is SelectAction select)
                {
                    string elemDesc = GetElementDescription(select.Placeholder, select.AriaLabel, select.TextContent, select.ElementId, select.CssSelector, select.Tag);
                    sb.AppendLine();
                    sb.AppendLine($"        await reportStepInfo('Seçim: {elemDesc} listesinden işlem yapılıyor.');");

                    string locator = BuildModernLocator(
                        select.Placeholder, select.AriaLabel, select.TextContent, select.ElementId, select.Tag, select.Name,
                        select.CssSelector, select.IsDynamicListElement, select.CustomTestId, "Select", dynamicVariables);

                    bool followsNavigation = NextActionIsNavigation(actions, i, select.PageAlias);

                    if (followsNavigation)
                    {
                        string promise = $"navigationPromise_{navigationCounter++}";
                        EmitNavigationWaitStart(sb, p, promise);
                        sb.AppendLine($"        await {p}.{locator}.selectOption('{Escape(select.SelectedValue)}');");
                        EmitNavigationWaitEnd(sb, p, promise);
                    }
                    else
                    {
                        sb.AppendLine($"        await {p}.{locator}.selectOption('{Escape(select.SelectedValue)}');");
                    }
                    continue;
                }

                // ============================================================
                // KEYBOARD
                // ============================================================

                if (action is KeyboardAction keyboard)
                {
                    string locator = BuildModernLocator(
                        keyboard.Placeholder, keyboard.AriaLabel, keyboard.TextContent, keyboard.ElementId, keyboard.Tag, keyboard.Name,
                        keyboard.CssSelector, keyboard.IsDynamicListElement, keyboard.CustomTestId, "Keyboard", dynamicVariables);

                    bool followsNavigation = NextActionIsNavigation(actions, i, keyboard.PageAlias);

                    if (followsNavigation)
                    {
                        string promise = $"navigationPromise_{navigationCounter++}";
                        EmitNavigationWaitStart(sb, p, promise);
                        sb.AppendLine($"        await {p}.{locator}.press('{Escape(keyboard.Key)}');");
                        EmitNavigationWaitEnd(sb, p, promise);
                    }
                    else
                    {
                        sb.AppendLine($"        await {p}.{locator}.press('{Escape(keyboard.Key)}');");
                    }
                    continue;
                }

                // ============================================================
                // ASSERT
                // ============================================================

                if (action is AssertAction assert)
                {
                    string locator = BuildModernLocator(
                        assert.Placeholder, assert.AriaLabel, assert.TextContent, assert.ElementId, assert.Tag, assert.Name,
                        assert.CssSelector, assert.IsDynamicListElement, assert.CustomTestId, "Assert");

                    sb.AppendLine($"        await expect({assert.PageAlias}.{locator}).toBeVisible();");
                    continue;
                }
            }

            // ================================================================
            // TEST KAPANIŞI VE HATA YÖNETİMİ
            // ================================================================
            sb.AppendLine();
            sb.AppendLine("        // Testroyer için başarılı kapanış raporlaması");
            sb.AppendLine("        await reportStepPass('SenseWright otomatik E2E senaryosu başarıyla tamamlandı.');");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("        await reportStepFail(`Test sırasında bir hata oluştu: ${error.message}`);");
            sb.AppendLine("        throw error;");
            sb.AppendLine("    }");
            sb.AppendLine("});");

            return sb.ToString();
        }

        // ====================================================================
        // RAPORLAMA İÇİN ELEMENT AÇIKLAMASI ÜRETİCİ
        // ====================================================================
        private string GetElementDescription(
            string placeholder, string ariaLabel, string text, string id, string cssSelector, string tag)
        {
            if (!string.IsNullOrWhiteSpace(text)) return Escape(text.Trim());
            if (!string.IsNullOrWhiteSpace(placeholder)) return Escape(placeholder.Trim());
            if (!string.IsNullOrWhiteSpace(ariaLabel)) return Escape(ariaLabel.Trim());
            if (!string.IsNullOrWhiteSpace(id)) return $"ID: {Escape(id)}";
            if (!string.IsNullOrWhiteSpace(cssSelector)) return $"CSS: {Escape(cssSelector)}";
            return Escape(tag ?? "element");
        }
        
        // ====================================================================
        // SIRAYA DAYALI NAVİGASYON TESPİTİ
        // ====================================================================

        private bool NextActionIsNavigation(List<UserAction> actions, int currentIndex, string pageAlias)
        {
            for (int j = currentIndex + 1; j < actions.Count; j++)
            {
                var next = actions[j];
                if (next is TabOpenedAction || next is TabActivatedAction) continue;
                if (!string.Equals(next.PageAlias, pageAlias, StringComparison.OrdinalIgnoreCase)) return false;

                if (next is NavigationAction nav)
                {
                    return nav.NavigationKind.Equals("UserAction", StringComparison.OrdinalIgnoreCase) ||
                           nav.NavigationKind.Equals("Automatic", StringComparison.OrdinalIgnoreCase);
                }

                if (next is ClickAction or InputAction or KeyboardAction or SelectAction or ExtractAction or HoverAction or AssertAction) return false;
            }
            return false;
        }

        // ====================================================================
        // SPA NETWORK (AJAX) TESPİTİ
        // ====================================================================
        
        private NetworkAction GetTriggeredNetworkAction(List<UserAction> actions, int currentIndex, string pageAlias)
        {
            for (int j = currentIndex + 1; j < actions.Count; j++)
            {
                var next = actions[j];
                if (next is TabOpenedAction || next is TabActivatedAction) continue;
                if (!string.Equals(next.PageAlias, pageAlias, StringComparison.OrdinalIgnoreCase)) return null;
                if (next is NetworkAction netAction) return netAction;
                if (next is NavigationAction) return null;
                if (next is ClickAction or InputAction or KeyboardAction or SelectAction or ExtractAction or HoverAction or AssertAction) return null;
            }
            return null;
        }

        // ====================================================================
        // EVENT RACE CONDITION FIX (REORDER)
        // ====================================================================
        
        private List<UserAction> ReorderEventRaceConditions(List<UserAction> source)
        {
            var result = new List<UserAction>(source);
            
            for (int i = 1; i < result.Count; i++)
            {
                if (result[i] is InputAction input)
                {
                    var prev = result[i - 1];
                    if ((prev is ClickAction prevClick && prevClick.CssSelector != input.CssSelector) ||
                        (prev is HoverAction prevHover && prevHover.CssSelector != input.CssSelector))
                    {
                        long timeDelta = Math.Abs(input.ClientTimestamp - prev.ClientTimestamp);
                        if (timeDelta < 1000)
                        {
                            result[i - 1] = input;
                            result[i] = prev;
                            i++; 
                        }
                    }
                }
            }
            return result;
        }
        
        // ====================================================================
        // CLEANUP
        // ====================================================================

        private List<UserAction> CleanupActions(List<UserAction> source)
        {
            var result = new List<UserAction>();

            for (int i = 0; i < source.Count; i++)
            {
                var current = source[i];

                if (current is ClickAction click)
                {
                    if (result.LastOrDefault() is ClickAction previousClick)
                    {
                        long timeDelta = Math.Abs(click.ClientTimestamp - previousClick.ClientTimestamp);
                        if (timeDelta < 500)
                        {
                            if (!string.IsNullOrWhiteSpace(click.TextContent) && 
                                string.Equals(click.TextContent, previousClick.TextContent, StringComparison.OrdinalIgnoreCase))
                            {
                                bool isDropdownSequence = click.Tag == "li" || previousClick.Tag == "li";
                                if (!isDropdownSequence) result.RemoveAt(result.Count - 1);
                            }
                            else if (click.CssSelector == previousClick.CssSelector) continue;
                        }
                    }
                    
                    if (i < source.Count - 1 && source[i + 1] is ExtractAction nextExtract && IsPopoverExtraction(nextExtract)) continue;

                    if (i < source.Count - 1 && source[i + 1] is ExtractAction ext)
                    {
                        bool selectorMatch = (!string.IsNullOrEmpty(click.CssSelector) && !string.IsNullOrEmpty(ext.CssSelector)) && 
                                             (click.CssSelector == ext.CssSelector || ext.CssSelector.Contains(click.CssSelector) || click.CssSelector.Contains(ext.CssSelector));
                        
                        string cleanClickText = click.TextContent != null ? click.TextContent.Trim() : "";
                        string cleanExtText = ext.ExtractedValue != null ? ext.ExtractedValue.Trim() : "";
                        
                        bool textMatch = (!string.IsNullOrEmpty(cleanClickText) && !string.IsNullOrEmpty(cleanExtText)) && 
                                         (cleanClickText.Contains(cleanExtText) || cleanExtText.Contains(cleanClickText));

                        if (selectorMatch || textMatch) continue;
                    }
                }
                else if (current is InputAction input)
                {
                    var lastInput = result.LastOrDefault(a => a is InputAction) as InputAction;
                    if (lastInput != null && lastInput.CssSelector == input.CssSelector && lastInput.Value == input.Value) continue;
                }
                else if (current is HoverAction hover)
                {
                    if (i < source.Count - 1 && source[i + 1] is HoverAction) continue;
                    if (i < source.Count - 1 && source[i + 1] is ExtractAction nextExtract && IsPopoverExtraction(nextExtract)) continue;
                    if (i < source.Count - 1 && source[i + 1] is ClickAction nextClick)
                    {
                        if (nextClick.CssSelector == hover.CssSelector || nextClick.CssSelector.Contains(hover.CssSelector)) continue;
                    }
                }

                result.Add(current);
            }
            return result;
        }

        // ====================================================================
        // EXTRACTION
        // ====================================================================

        private void GenerateExtraction(StringBuilder sb, ExtractAction ext, string locator, string varName)
        {
            string mode = string.IsNullOrWhiteSpace(ext.ExtractionMode) ? "Text" : ext.ExtractionMode;

            if (mode.Equals("Text", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("        // Normal DOM text extraction");
                sb.AppendLine($"        let {varName} = (await {ext.PageAlias}.{locator}.innerText()).trim();");
            }
            else if (mode.Equals("Attribute", StringComparison.OrdinalIgnoreCase))
            {
                string attribute = string.IsNullOrWhiteSpace(ext.AttributeName) ? "value" : ext.AttributeName;
                sb.AppendLine($"        // HTML attribute extraction: {attribute}");
                sb.AppendLine($"        let {varName} = ((await {ext.PageAlias}.{locator}.getAttribute('{Escape(attribute)}')) ?? '').trim();");
                sb.AppendLine($"        if (!{varName}) throw new Error('Attribute extraction başarısız: {Escape(attribute)}');");
            }
            else if (mode.StartsWith("Popover", StringComparison.OrdinalIgnoreCase))
            {
                GeneratePopoverExtraction(sb, ext, locator, varName);
            }
            else
            {
                sb.AppendLine($"        // Bilinmeyen extraction mode '{Escape(mode)}'; Text fallback kullanılıyor");
                sb.AppendLine($"        let {varName} = (await {ext.PageAlias}.{locator}.innerText()).trim();");
            }

            if (!string.IsNullOrEmpty(ext.ExtractPrefix))
            {
                sb.AppendLine("        // Kopyalama sırasında seçilmeyen ÖN EK (Prefix) kısmı temizleniyor");
                sb.AppendLine($"        {varName} = {varName}.replace('{Escape(ext.ExtractPrefix)}', '').trim();");
            }
            
            if (!string.IsNullOrEmpty(ext.ExtractSuffix))
            {
                sb.AppendLine("        // Kopyalama sırasında seçilmeyen SON EK (Suffix) kısmı temizleniyor");
                sb.AppendLine($"        {varName} = {varName}.replace('{Escape(ext.ExtractSuffix)}', '').trim();");
            }

            if (!string.IsNullOrWhiteSpace(ext.ExtractedValue) && ext.ExtractedValue.All(char.IsDigit))
            {
                sb.AppendLine("        // Kullanıcı test kaydında sadece rakam kopyalamıştı, metin içindeki harf ve sembolleri temizliyoruz.");
                sb.AppendLine($"        {varName} = {varName}.replace(/[^0-9]/g, '');");
            }
            else if (!string.IsNullOrWhiteSpace(ext.ExtractedValue))
            {
                sb.AppendLine("        // Elementin sonundaki muhtemel gereksiz karakterleri (örn: iki nokta) temizliyoruz.");
                sb.AppendLine($"        {varName} = {varName}.replace(/[:]/g, '').trim();");
            }
        }

        // ====================================================================
        // POPOVER EXTRACTION
        // ====================================================================

        private void GeneratePopoverExtraction(StringBuilder sb, ExtractAction ext, string locator, string varName)
        {
            string attributeName = string.IsNullOrWhiteSpace(ext.AttributeName) ? "data-content" : ext.AttributeName;
            string label = string.IsNullOrWhiteSpace(ext.ExtractionLabel) ? "" : ext.ExtractionLabel;
            bool isHorizontal = ext.ExtractionMode.Equals("PopoverHorizontal", StringComparison.OrdinalIgnoreCase);
            int labelIndex = ext.ExtractionLabelIndex; 

            sb.AppendLine();
            sb.AppendLine("        // Popover/Tooltip içeriğinin DOM'a yüklenmesi ve animasyonların bitmesi için bekleme");
            sb.AppendLine($"        await {ext.PageAlias}.waitForTimeout(1500);");
            sb.AppendLine();
            
            sb.AppendLine("        // Bootstrap / HTML popover içindeki dinamik veri okunuyor");
            sb.AppendLine($"        let {varName} = await {ext.PageAlias}.{locator}.evaluate((el) => {{");
            sb.AppendLine($"            const content = el.getAttribute('{Escape(attributeName)}') || '';");
            sb.AppendLine($"            if (!content) throw new Error('Popover attribute bulunamadı: {Escape(attributeName)}');");
            
            sb.AppendLine("            const parser = new DOMParser();");
            sb.AppendLine("            const doc = parser.parseFromString(content, 'text/html');");

            if (!string.IsNullOrWhiteSpace(label))
            {
                sb.AppendLine("            const rows = Array.from(doc.querySelectorAll('tr'));");
                sb.AppendLine("            if (rows.length === 0) throw new Error('Popover içinde tablo bulunamadı.');");
                sb.AppendLine();
                
                if (isHorizontal)
                {
                    sb.AppendLine("            // Yatay (Horizontal) tablo araması");
                    sb.AppendLine("            const headers = Array.from(rows[0].querySelectorAll('th, td')).map(h => (h.textContent || '').replace(/\\s+/g, ' ').trim());");
                    sb.AppendLine($"            const colIndex = headers.indexOf('{Escape(label)}');");
                    sb.AppendLine($"            if (colIndex !== -1 && rows.length > {labelIndex + 1}) {{");
                    sb.AppendLine($"                const cells = Array.from(rows[{labelIndex + 1}].querySelectorAll('th, td'));");
                    sb.AppendLine("                return (cells[colIndex]?.textContent || '').replace(/\\s+/g, ' ').trim();");
                    sb.AppendLine("            }");
                    sb.AppendLine($"            throw new Error('Yatay popover içinde \"{Escape(label)}\" sütunu bulunamadı.');");
                }
                else
                {
                    sb.AppendLine("            // Dikey (Vertical) tablo araması (Aynı etiketten birden fazla varsa Index ile filtreliyoruz)");
                    sb.AppendLine($"            const matchingRows = rows.filter((r) => {{");
                    sb.AppendLine("                const cells = Array.from(r.querySelectorAll('th, td'));");
                    sb.AppendLine("                if (cells.length < 2) return false;");
                    sb.AppendLine("                const rowLabel = (cells[0].textContent || '').replace(/\\s+/g, ' ').trim();");
                    sb.AppendLine($"                return rowLabel === '{Escape(label)}';");
                    sb.AppendLine("            });");
                    sb.AppendLine();
                    sb.AppendLine($"            if (matchingRows.length <= {labelIndex}) throw new Error('Dikey popover içinde \"{Escape(label)}\" etiketli {labelIndex + 1}. satır bulunamadı.');");
                    sb.AppendLine($"            const cells = Array.from(matchingRows[{labelIndex}].querySelectorAll('th, td'));");
                    sb.AppendLine("            return (cells[1]?.textContent || '').replace(/\\s+/g, ' ').trim();");
                }
            }
            else
            {
                sb.AppendLine("            throw new Error('Popover extraction için ExtractionLabel bulunamadı.');");
            }

            sb.AppendLine("        });"); // Evaluate fonksiyonu burada kapanır
            sb.AppendLine();
            sb.AppendLine($"        if (!{varName}) {{");
            sb.AppendLine($"            throw new Error('Popover extraction başarısız. Alan: {Escape(label)}');");
            sb.AppendLine("        }");
        }

        private bool IsPopoverExtraction(ExtractAction action)
        {
            return !string.IsNullOrWhiteSpace(action.ExtractionMode) &&
                   action.ExtractionMode.StartsWith("Popover", StringComparison.OrdinalIgnoreCase);
        }

        // ====================================================================
        // SEARCH ENTER
        // ====================================================================
        
        private bool WasPrecededBySearchEnter(List<UserAction> actions, int currentIndex)
        {
            for (int j = currentIndex - 1; j >= 0 && j >= currentIndex - 5; j--)
            {
                var action = actions[j];
                if (action is KeyboardAction keyboard && keyboard.Key.Equals("Enter", StringComparison.OrdinalIgnoreCase)) return true;
                if (action is NavigationAction || action is TabOpenedAction || action is TabActivatedAction) return false;
                if (action is ClickAction otherClick && !(otherClick.Tag == "td" || otherClick.Tag == "tr" || otherClick.Tag == "th" || otherClick.Tag == "li")) return false;
            }
            return false;
        }

        // ====================================================================
        // LOCATOR BUILDER (AKILLI ELEMENT BULUCU)
        // ====================================================================
        
        private string BuildModernLocator(
            string placeholder, string ariaLabel, string text, string id, string tag, string name,
            string cssSelector, bool isDynamicListElement, string customTestId, string actionType, Dictionary<string, string> dynamicVariables = null)
        {
            string ProcessString(string input, out bool hasVar)
            {
                hasVar = false;
                if (string.IsNullOrEmpty(input)) return "";
                
                string result = Escape(input);
                if (dynamicVariables != null && dynamicVariables.Count > 0)
                {
                    foreach (var kvp in dynamicVariables.OrderByDescending(v => v.Key.Length))
                    {
                        if (result.Contains(Escape(kvp.Key)))
                        {
                            result = result.Replace(Escape(kvp.Key), $"${{{kvp.Value}}}");
                            hasVar = true;
                        }
                    }
                }
                return result;
            }

            if (!string.IsNullOrWhiteSpace(customTestId)) return $"locator('[data-name=\"{EscapeDoubleQuoted(customTestId)}\"], [data-testid=\"{EscapeDoubleQuoted(customTestId)}\"]').first()";
            
            bool isGuidId = !string.IsNullOrWhiteSpace(id) && id.Length == 36 && id.Split('-').Length == 5;
            if (!string.IsNullOrWhiteSpace(id) && !isGuidId) return $"locator('[id*=\"{EscapeDoubleQuoted(id)}\"]:visible').last()";

            if (!string.IsNullOrWhiteSpace(placeholder))
            {
                string procPlaceholder = ProcessString(placeholder, out bool hasVar);
                string quote = hasVar ? "`" : "'";
                return $"getByPlaceholder({quote}{procPlaceholder}{quote}).first()";
            }

            if (!string.IsNullOrWhiteSpace(ariaLabel))
            {
                string procAria = ProcessString(ariaLabel, out bool hasVar);
                string quote = hasVar ? "`" : "'";
                return $"getByLabel({quote}{procAria}{quote}).first()";
            }

            string trimmedText = text?.Trim() ?? "";
            bool isGenericMathSymbol = trimmedText == "+" || trimmedText == "-" || trimmedText == "−" || trimmedText == "x" || trimmedText == "X";
            if (isGenericMathSymbol && !string.IsNullOrWhiteSpace(cssSelector)) return $"locator('{EscapeDoubleQuoted(cssSelector)}').first()";

            if (!string.IsNullOrWhiteSpace(text))
            {
                bool hasDynamicBadge = System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"\s+\d+\+?$");

                if (!hasDynamicBadge)
                {
                    string procText = ProcessString(text, out bool hasVar);
                    string targetTag = tag == "button" ? "button:visible" : tag == "a" ? "a:visible" : $"{tag}:visible";

                    if (hasVar)
                    {
                        return $"locator(`{targetTag}`).filter({{ hasText: `{procText}` }}).first()";
                    }
                    else
                    {
                        procText = procText.Replace("\"", "\\\""); 
                        return $"locator('{targetTag}:text-is(\"{procText}\")').first()";
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(name)) return $"locator('[name=\"{EscapeDoubleQuoted(name)}\"]').first()";
            if (!string.IsNullOrWhiteSpace(cssSelector)) return $"locator('{EscapeDoubleQuoted(cssSelector)}').first()";

            return "locator('*').first()";
        }

        // ====================================================================
        // URL ORIGIN & RELATIVE URL
        // ====================================================================

        private string GetOrigin(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return uri.GetLeftPart(UriPartial.Authority);
            return "";
        }

        private string GetRelativeUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                string relative = uri.AbsolutePath;
                if (!string.IsNullOrEmpty(uri.Query)) relative += uri.Query;
                if (!string.IsNullOrEmpty(uri.Fragment)) relative += uri.Fragment;
                return string.IsNullOrEmpty(relative) ? "/" : relative;
            }
            return string.IsNullOrWhiteSpace(url) ? "/" : url;
        }

        // ====================================================================
        // NAVIGATION WAYS
        // ====================================================================

        private void EmitNavigationWaitStart(StringBuilder sb, string pageAlias, string promiseName)
        {
            sb.AppendLine($"        // Aksiyon öncesi URL'nin ana domain (origin) ve path bilgilerini hafızaya alıyoruz.");
            sb.AppendLine($"        const prevUrlObj_{promiseName} = new URL({pageAlias}.url());");
        }

        private void EmitNavigationWaitEnd(StringBuilder sb, string pageAlias, string promiseName)
        {
            sb.AppendLine($"        // Test anındaki dinamik duruma göre URL'nin değişip değişmeyeceği kontrol ediliyor.");
            sb.AppendLine($"        try {{");
            sb.AppendLine($"            await {pageAlias}.waitForURL(url =>");
            sb.AppendLine($"                url.origin === prevUrlObj_{promiseName}.origin &&");
            sb.AppendLine($"                url.pathname !== prevUrlObj_{promiseName}.pathname,");
            sb.AppendLine($"            {{ waitUntil: 'domcontentloaded', timeout: 3000 }});");
            sb.AppendLine($"        }} catch (e) {{");
            sb.AppendLine($"            await {pageAlias}.waitForLoadState('networkidle', {{ timeout: 3000 }}).catch(() => {{}});");
            sb.AppendLine($"        }}");
        }

        // ====================================================================
        // ESCAPE
        // ====================================================================

        private string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private string EscapeDoubleQuoted(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}