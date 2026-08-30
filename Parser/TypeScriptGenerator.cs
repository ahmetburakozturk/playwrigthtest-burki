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
            //
            // GLOBAL TIMESTAMP SORTING YOK.
            //
            // Recorder artık navigation türünü kendisi belirliyor.
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

            actions =
                CleanupActions(
                    actions);

            // ================================================================
            // TYPESCRIPT HEADER
            // ================================================================

            var sb =
                new StringBuilder();

            sb.AppendLine(
                "import { test, expect } from '@playwright/test';");

            sb.AppendLine();

            sb.AppendLine(
                "test('SenseWright Auto-Generated E2E Test', async ({ page, context }) => {");

            // ================================================================
            // PAGE STATE
            // ================================================================

            var declaredPages =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    "page"
                };

            var firstNavigationByPage =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            var lastRecordedUrlByPage =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);

            string lastGeneratedPageAlias =
                "page";

            // ================================================================
            // DYNAMIC VARIABLES
            // ================================================================

            var dynamicVariables =
                new Dictionary<string, string>();

            int varCounter = 1;

            // ================================================================
            // NAVIGATION
            // ================================================================

            int navigationCounter = 1;

            // ================================================================
            // ACTION LOOP
            // ================================================================

            for (
                int i = 0;
                i < actions.Count;
                i++)
            {
                var action =
                    actions[i];

                string p =
                    string.IsNullOrWhiteSpace(
                        action.PageAlias)
                        ? "page"
                        : action.PageAlias;

                // ============================================================
                // TAB OPENED
                // ============================================================

                if (
                    action is TabOpenedAction tabOpened
                )
                {
                    sb.AppendLine();

                    sb.AppendLine(
                        "// Uygulamanın açtığı yeni sekmeyi dinamik olarak yakala");

                    sb.AppendLine(
                        $"while (context.pages().length <= {declaredPages.Count}) {{");

                    sb.AppendLine(
                        "    await page.waitForTimeout(100);");

                    sb.AppendLine(
                        "}");

                    sb.AppendLine(
                        $"const {p} = context.pages()[context.pages().length - 1];");

                    sb.AppendLine(
                        $"await {p}.waitForLoadState('domcontentloaded');");

                    declaredPages.Add(
                        p);

                    lastGeneratedPageAlias =
                        p;

                    continue;
                }

                // ============================================================
                // TAB ACTIVATED
                // ============================================================

                if (
                    action is TabActivatedAction
                )
                {
                    if (
                        !string.Equals(
                            lastGeneratedPageAlias,
                            p,
                            StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        sb.AppendLine();

                        sb.AppendLine(
                            $"// Kullanıcı browser sekmeleri arasında {p} sekmesine geçti.");

                        sb.AppendLine(
                            $"await {p}.bringToFront();");

                        lastGeneratedPageAlias =
                            p;
                    }

                    continue;
                }

                // ============================================================
                // PAGE ALIAS CHANGE
                // ============================================================
                //
                // TabActivatedAction kaydı gelmese bile PageAlias değişiminden
                // gerçek page switch'i anlayabiliriz.
                // ============================================================

                if (
                    !string.Equals(
                        lastGeneratedPageAlias,
                        p,
                        StringComparison.OrdinalIgnoreCase)
                )
                {
                    sb.AppendLine();

                    sb.AppendLine(
                        $"// Kullanıcı browser sekmeleri arasında {p} sekmesine geçti.");

                    sb.AppendLine(
                        $"await {p}.bringToFront();");

                    lastGeneratedPageAlias =
                        p;
                }

                // ============================================================
                // NAVIGATION
                // ============================================================

                if (
                    action is NavigationAction nav
                )
                {
                    string navPage =
                        string.IsNullOrWhiteSpace(
                            nav.PageAlias)
                                ? "page"
                                : nav.PageAlias;

                    // --------------------------------------------------------
                    // INITIAL
                    // --------------------------------------------------------

                    if (
                        nav.NavigationKind.Equals(
                            "Initial",
                            StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        string origin =
                            GetOrigin(
                                nav.Url);

                        string relativeUrl =
                            GetRelativeUrl(
                                nav.Url);

                        sb.AppendLine();

                        sb.AppendLine(
                            $"const baseUrl = (process.env.BASE_URL ?? '{Escape(origin)}').replace(/\\/+$/, '');");

                        sb.AppendLine();

                        sb.AppendLine(
                            "// Test başlangıç sayfasına gidiliyor.");

                        sb.AppendLine(
                            $"await {navPage}.goto(new URL('{Escape(relativeUrl)}', baseUrl).toString(), {{ waitUntil: 'load' }});");

                        firstNavigationByPage.Add(
                            navPage);

                        lastRecordedUrlByPage[
                            navPage] =
                            nav.Url;

                        continue;
                    }

                    // --------------------------------------------------------
                    // FIRST NAVIGATION OF NEW TAB
                    // --------------------------------------------------------
                    //
                    // TabOpenedAction zaten yeni page'i yakalıyor.
                    // İlk navigation'ı tekrar üretme.
                    // --------------------------------------------------------

                    if (
                        !firstNavigationByPage.Contains(
                            navPage)
                    )
                    {
                        firstNavigationByPage.Add(
                            navPage);

                        lastRecordedUrlByPage[
                            navPage] =
                            nav.Url;

                        continue;
                    }

                    // --------------------------------------------------------
                    // USER ACTION
                    // --------------------------------------------------------
                    //
                    // Click / Enter / Select kendi navigation wait'ini
                    // zaten üretir.
                    // --------------------------------------------------------

                    if (
                        nav.NavigationKind.Equals(
                            "UserAction",
                            StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        lastRecordedUrlByPage[
                            navPage] =
                            nav.Url;

                        continue;
                    }

                    // --------------------------------------------------------
                    // AUTOMATIC
                    // --------------------------------------------------------
                    //
                    // Login redirect / server redirect / SPA automatic
                    // navigation.
                    //
                    // URL ile tekrar goto yapma.
                    // --------------------------------------------------------

                    if (
                        nav.NavigationKind.Equals(
                            "Automatic",
                            StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        lastRecordedUrlByPage[
                            navPage] =
                            nav.Url;

                        continue;
                    }

                    // --------------------------------------------------------
                    // RELOAD
                    // --------------------------------------------------------

                    if (
                        nav.NavigationKind.Equals(
                            "Reload",
                            StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        sb.AppendLine();

                        sb.AppendLine(
                            "// Kullanıcı sayfayı yeniledi.");

                        sb.AppendLine(
                            $"await {navPage}.reload({{ waitUntil: 'load' }});");

                        lastRecordedUrlByPage[
                            navPage] =
                            nav.Url;

                        continue;
                    }

                    // --------------------------------------------------------
                    // HISTORY
                    // --------------------------------------------------------

                    if (
                        nav.NavigationKind.Equals(
                            "History",
                            StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        // Bu action modelinde back/forward yönü ayrıca
                        // tutulmuyor. NavigationHistory index'i eklenene kadar
                        // güvenli davranış olarak mevcut dokümanın yüklenmesini
                        // bekliyoruz.
                        sb.AppendLine();

                        sb.AppendLine(
                            "// Browser history navigation gerçekleşti.");

                        sb.AppendLine(
                            $"await {navPage}.waitForLoadState('load');");

                        lastRecordedUrlByPage[
                            navPage] =
                            nav.Url;

                        continue;
                    }

                    // --------------------------------------------------------
                    // MANUAL URL
                    // --------------------------------------------------------

                    if (
                        nav.NavigationKind.Equals(
                            "Manual",
                            StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        string manualUrl =
                            !string.IsNullOrWhiteSpace(
                                nav.UserTypedUrl)
                                    ? nav.UserTypedUrl
                                    : nav.Url;

                        // ----------------------------------------------------
                        // Manual URL absolute ise path/query/fragment al.
                        //
                        // Böylece generated test environment host'una
                        // mümkün olduğunca bağımlı kalmaz.
                        // ----------------------------------------------------

                        string relativeUrl =
                            GetRelativeUrl(
                                manualUrl);

                        sb.AppendLine();

                        sb.AppendLine(
                            "// Kullanıcı browser adres çubuğundan manuel olarak URL değiştirdi.");

                        sb.AppendLine(
                            $"await {navPage}.goto(new URL('{Escape(relativeUrl)}', {navPage}.url()).toString(), {{ waitUntil: 'load' }});");

                        lastRecordedUrlByPage[
                            navPage] =
                            manualUrl;

                        continue;
                    }

                    // --------------------------------------------------------
                    // UNKNOWN
                    // --------------------------------------------------------

                    sb.AppendLine();

                    sb.AppendLine(
                        "// Navigation kaynağı güvenilir şekilde belirlenemedi; mevcut dokümanın yüklenmesi bekleniyor.");

                    sb.AppendLine(
                        $"await {navPage}.waitForLoadState('load');");

                    lastRecordedUrlByPage[
                        navPage] =
                            nav.Url;

                    continue;
                }

                // ============================================================
                // HOVER
                // ============================================================

                if (
                    action is HoverAction hover
                )
                {
                    string locator =
                        BuildModernLocator(
                            hover.Placeholder,
                            hover.AriaLabel,
                            hover.TextContent,
                            hover.ElementId,
                            hover.Tag,
                            hover.Name,
                            hover.CssSelector,
                            hover.IsDynamicListElement,
                            hover.CustomTestId,
                            "Hover",
                            dynamicVariables);

                    sb.AppendLine();

                    sb.AppendLine(
                        "// Tooltip/Pop-up açmak için element üzerinde hover");

                    sb.AppendLine(
                        $"await {hover.PageAlias}.{locator}.hover();");

                    continue;
                }

                // ============================================================
                // EXTRACT
                // ============================================================

                if (
                    action is ExtractAction ext
                )
                {
                    string varName =
                        $"dynamicUserVar_{varCounter++}";

                    dynamicVariables[
                        ext.ExtractedValue] =
                        varName;

                    string locator =
                        BuildModernLocator(
                            ext.Placeholder,
                            ext.AriaLabel,
                            "",
                            ext.ElementId,
                            ext.Tag,
                            ext.Name,
                            ext.CssSelector,
                            ext.IsDynamicListElement,
                            ext.CustomTestId,
                            "Extract",
                            dynamicVariables);

                    GenerateExtraction(
                        sb,
                        ext,
                        locator,
                        varName);

                    continue;
                }

                // ============================================================
                // INPUT
                // ============================================================

                if (
                    action is InputAction input
                )
                {
                    string locator =
                        BuildModernLocator(
                            input.Placeholder,
                            input.AriaLabel,
                            input.TextContent,
                            input.ElementId,
                            input.Tag,
                            input.Name,
                            input.CssSelector,
                            input.IsDynamicListElement,
                            input.CustomTestId,
                            "Input");

                    if (
                        dynamicVariables.TryGetValue(
                            input.Value,
                            out string matchedVar)
                    )
                    {
                        sb.AppendLine();

                        sb.AppendLine(
                            "// Hafızadaki dinamik değişken alana dolduruluyor");

                        sb.AppendLine(
                            $"await {input.PageAlias}.{locator}.fill({matchedVar});");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"await {input.PageAlias}.{locator}.fill('{Escape(input.Value)}');");
                    }

                    continue;
                }

                // ============================================================
                // CLICK
                // ============================================================

                if (
                    action is ClickAction click
                )
                {
                    bool isTableOrListElement =
                        click.Tag == "td" ||
                        click.Tag == "tr" ||
                        click.Tag == "th" ||
                        click.Tag == "li";

                    bool isSearchDrivenSelection =
                        click.IsDynamicListElement &&
                        (
                            click.Tag == "td" ||
                            click.Tag == "th"
                        ) &&
                        click.RowIndex >= 0 &&
                        WasPrecededBySearchEnter(
                            actions,
                            i);

                    // ========================================================
                    // NAVIGATION TRIGGER
                    // ========================================================

                    bool followsNavigation =
                        NextActionIsNavigation(actions, i, click.PageAlias);

                    // ========================================================
                    // DYNAMIC TABLE SELECTION
                    // ========================================================

                    if (
                        isSearchDrivenSelection
                    )
                    {
                        string tableScope =
                            !string.IsNullOrWhiteSpace(
                                click.ParentTableId)
                                    ? $"#{Escape(click.ParentTableId)} tbody tr"
                                    : "tbody tr";

                        if (
                            followsNavigation
                        )
                        {
                            string promise =
                                $"navigationPromise_{navigationCounter++}";

                            EmitNavigationWaitStart(
                                sb,
                                p,
                                promise);

                            sb.AppendLine();

                            sb.AppendLine(
                                $"// Arama sonrası dinamik listeden pozisyona göre seçim (kaydedilen satır index: {click.RowIndex})");

                            sb.AppendLine(
                                $"await {p}.locator('{tableScope}').nth({click.RowIndex}).click();");

                            EmitNavigationWaitEnd(
                                sb,
                                p,
                                promise);
                        }
                        else
                        {
                            sb.AppendLine();

                            sb.AppendLine(
                                $"// Arama sonrası dinamik listeden pozisyona göre seçim (kaydedilen satır index: {click.RowIndex})");

                            sb.AppendLine(
                                $"await {p}.locator('{tableScope}').nth({click.RowIndex}).click();");
                        }

                        continue;
                    }

                    // ========================================================
                    // NORMAL CLICK
                    // ========================================================

                    string clickLocator =
                        BuildModernLocator(
                            click.Placeholder,
                            click.AriaLabel,
                            click.TextContent,
                            click.ElementId,
                            click.Tag,
                            click.Name,
                            click.CssSelector,
                            click.IsDynamicListElement,
                            click.CustomTestId,
                            "Click",
                            dynamicVariables);

                    // EKLENDİ: Bu tıklama bir ağ isteği (API/AJAX) tetikliyor mu?
                    NetworkAction triggeredNetwork = !followsNavigation ? GetTriggeredNetworkAction(actions, i, click.PageAlias) : null;

                    if (followsNavigation)
                    {
                        string promise = $"navigationPromise_{navigationCounter++}";
                        EmitNavigationWaitStart(sb, p, promise);
                        sb.AppendLine($"await {p}.{clickLocator}.click();");
                        EmitNavigationWaitEnd(sb, p, promise);
                    }
                    else if (triggeredNetwork != null)
                    {
                        // Host/Query değişikliklerinden etkilenmemek için URL'den sadece Path kısmını alıyoruz.
                        string apiPath = "";
                        if (Uri.TryCreate(triggeredNetwork.Url, UriKind.Absolute, out Uri apiUri)) {
                            apiPath = apiUri.AbsolutePath; 
                        } else {
                            apiPath = triggeredNetwork.Url;
                        }

                        string promise = $"networkPromise_{navigationCounter++}";

                        sb.AppendLine();
                        sb.AppendLine($"// Tıklamanın tetiklediği spesifik API isteğini ({triggeredNetwork.Method} {apiPath}) yakalamak için promise oluşturuluyor.");
                        sb.AppendLine($"const {promise} = {p}.waitForResponse(resp => resp.url().includes('{Escape(apiPath)}') && resp.request().method() === '{Escape(triggeredNetwork.Method)}', {{ timeout: 15000 }}).catch(() => {{}});");
                        
                        sb.AppendLine($"await {p}.{clickLocator}.click();");
                        
                        sb.AppendLine($"await {promise};");

                        // YENİ EKLENEN KISIM: Artçı İstekleri (GET) ve UI Kilitlerini (Spinner) Bekleme
                        sb.AppendLine($"// İşlem sonrası tetiklenen ardışık veri güncellemelerinin (GET vb.) bitmesini bekliyoruz.");
                        sb.AppendLine($"await {p}.waitForLoadState('networkidle', {{ timeout: 10000 }}).catch(() => {{}});");

                        sb.AppendLine($"// Ön yüzün (React/Vue vb.) DOM'u tam çizmesi için kısa bir esneklik payı");
                        sb.AppendLine($"await {p}.waitForTimeout(500);");
                    }
                    else
                    {
                        sb.AppendLine($"await {p}.{clickLocator}.click();");
                    }
                    continue;
                }

                // ============================================================
                // SELECT
                // ============================================================

                if (
                    action is SelectAction select
                )
                {
                    string locator =
                        BuildModernLocator(
                            select.Placeholder,
                            select.AriaLabel,
                            select.TextContent,
                            select.ElementId,
                            select.Tag,
                            select.Name,
                            select.CssSelector,
                            select.IsDynamicListElement,
                            select.CustomTestId,
                            "Select",
                            dynamicVariables);

                    bool followsNavigation =
                        NextActionIsNavigation(actions, i, select.PageAlias);

                    if (
                        followsNavigation
                    )
                    {
                        string promise =
                            $"navigationPromise_{navigationCounter++}";

                        EmitNavigationWaitStart(
                            sb,
                            p,
                            promise);

                        sb.AppendLine(
                            $"await {p}.{locator}.selectOption('{Escape(select.SelectedValue)}');");

                        EmitNavigationWaitEnd(
                            sb,
                            p,
                            promise);
                    }
                    else
                    {
                        sb.AppendLine(
                            $"await {p}.{locator}.selectOption('{Escape(select.SelectedValue)}');");
                    }

                    continue;
                }

                // ============================================================
                // KEYBOARD
                // ============================================================

                if (
                    action is KeyboardAction keyboard
                )
                {
                    string locator =
                        BuildModernLocator(
                            keyboard.Placeholder,
                            keyboard.AriaLabel,
                            keyboard.TextContent,
                            keyboard.ElementId,
                            keyboard.Tag,
                            keyboard.Name,
                            keyboard.CssSelector,
                            keyboard.IsDynamicListElement,
                            keyboard.CustomTestId,
                            "Keyboard",
                            dynamicVariables);

                    bool isEnter =
                        keyboard.Key.Equals(
                            "Enter",
                            StringComparison.OrdinalIgnoreCase);

                    bool followsNavigation =
                        NextActionIsNavigation(actions, i, keyboard.PageAlias);

                    if (
                        followsNavigation
                    )
                    {
                        string promise =
                            $"navigationPromise_{navigationCounter++}";

                        EmitNavigationWaitStart(
                            sb,
                            p,
                            promise);

                        sb.AppendLine(
                            $"await {p}.{locator}.press('{Escape(keyboard.Key)}');");

                        EmitNavigationWaitEnd(
                            sb,
                            p,
                            promise);
                    }
                    else
                    {
                        sb.AppendLine(
                            $"await {p}.{locator}.press('{Escape(keyboard.Key)}');");
                    }

                    continue;
                }

                // ============================================================
                // ASSERT
                // ============================================================

                if (
                    action is AssertAction assert
                )
                {
                    string locator =
                        BuildModernLocator(
                            assert.Placeholder,
                            assert.AriaLabel,
                            assert.TextContent,
                            assert.ElementId,
                            assert.Tag,
                            assert.Name,
                            assert.CssSelector,
                            assert.IsDynamicListElement,
                            assert.CustomTestId,
                            "Assert");

                    sb.AppendLine(
                        $"await expect({assert.PageAlias}.{locator}).toBeVisible();");

                    continue;
                }
            }

            sb.AppendLine(
                "});");

            return sb.ToString();
        }

        // ====================================================================
        // SIRAYA DAYALI NAVİGASYON TESPİTİ
        // ====================================================================
        //
        // ClientSequence eşleştirmesi yerine: bu action'dan sonra, aynı sayfada,
        // başka bir "gerçek" kullanıcı action'ı gelmeden önce bir NavigationAction
        // geliyor mu diye bakıyoruz. Recorder action'ları zaten oluş sırasına göre
        // tek listede topladığı için bu, cross-thread ID eşleştirmesinden çok
        // daha güvenilir.
        // ====================================================================

        private bool NextActionIsNavigation(
            List<UserAction> actions,
            int currentIndex,
            string pageAlias)
        {
            for (int j = currentIndex + 1; j < actions.Count; j++)
            {
                var next = actions[j];

                // Tab açma/geçiş action'ları araya girebilir, aramaya devam et.
                if (next is TabOpenedAction || next is TabActivatedAction)
                {
                    continue;
                }

                if (!string.Equals(
                        next.PageAlias,
                        pageAlias,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (next is NavigationAction nav)
                {
                    return
                        nav.NavigationKind.Equals("UserAction", StringComparison.OrdinalIgnoreCase) ||
                        nav.NavigationKind.Equals("Automatic", StringComparison.OrdinalIgnoreCase);
                }

                // Aynı sayfada başka bir gerçek action geldiyse, bu click/keyboard
                // navigasyon tetiklememiş demektir.
                if (next is ClickAction or InputAction or KeyboardAction or
                    SelectAction or ExtractAction or HoverAction or AssertAction)
                {
                    return false;
                }
            }

            return false;
        }

        // ====================================================================
        // SPA NETWORK (AJAX) TESPİTİ
        // ====================================================================
        private NetworkAction GetTriggeredNetworkAction(
            List<UserAction> actions,
            int currentIndex,
            string pageAlias)
        {
            for (int j = currentIndex + 1; j < actions.Count; j++)
            {
                var next = actions[j];
                if (next is TabOpenedAction || next is TabActivatedAction) continue;
                if (!string.Equals(next.PageAlias, pageAlias, StringComparison.OrdinalIgnoreCase)) return null;

                // Tıklamadan hemen sonra bir API isteği yakalandıysa, o aksiyonu döndür
                if (next is NetworkAction netAction) return netAction;
                
                if (next is NavigationAction) return null;
                if (next is ClickAction or InputAction or KeyboardAction or SelectAction or ExtractAction or HoverAction or AssertAction) return null;
            }
            return null;
        }

        // ====================================================================
        // EVENT RACE CONDITION FIX (REORDER)
        // ====================================================================

        private List<UserAction> ReorderEventRaceConditions(
            List<UserAction> source)
        {
            var result = new List<UserAction>(source);
            bool swapped = true;
            
            while (swapped)
            {
                swapped = false;
                for (int i = 1; i < result.Count; i++)
                {
                    if (result[i] is InputAction input)
                    {
                        var prev = result[i - 1];
                        
                        // Eğer bir önceki işlem farklı bir elemente (örn: dropdown butonu) ait bir Tıklama veya Hover ise
                        if ((prev is ClickAction prevClick && prevClick.CssSelector != input.CssSelector) ||
                            (prev is HoverAction prevHover && prevHover.CssSelector != input.CssSelector))
                        {
                            // Input (fill) işlemini mantıksal olarak Click'ten öncesine (yukarı) kaydırıyoruz.
                            result[i - 1] = input;
                            result[i] = prev;
                            swapped = true;
                        }
                    }
                }
            }
            
            return result;
        }
        
        // ====================================================================
        // CLEANUP
        // ====================================================================

        private List<UserAction> CleanupActions(
            List<UserAction> source)
        {
            var result =
                new List<UserAction>();

            for (
                int i = 0;
                i < source.Count;
                i++)
            {
                var current =
                    source[i];

                // ============================================================
                // CLICK
                // ============================================================

                if (current is ClickAction click)
                {
                    // Listedeki EN SON aksiyonu alıyoruz.
                    // Eğer araya NetworkAction veya NavigationAction girdiyse bu kural TETİKLENMEZ.
                    if (result.LastOrDefault() is ClickAction previousClick)
                    {
                        // 1. ARAYA HİÇBİR İŞLEM GİRMEDEN AYNI METNE TIKLAMA (Boşa tıklama / Iskalama)
                        if (!string.IsNullOrWhiteSpace(click.TextContent) && 
                            string.Equals(click.TextContent, previousClick.TextContent, StringComparison.OrdinalIgnoreCase))
                        {
                            // Araya istek girmediği için ilk tıklama işlevsizdir, sil.
                            // Yenisi (doğru olan) birazdan döngü sonunda listeye eklenecek.
                            result.RemoveAt(result.Count - 1);
                        }
                        // 2. TAMAMEN AYNI CSS SELECTOR'A ÇİFT TIKLAMA
                        else if (click.CssSelector == previousClick.CssSelector)
                        {
                            continue;
                        }
                    }

                    // Popover extraction click'i... (Aşağısı aynı kalacak)
                    if (
                        i < source.Count - 1 &&
                        source[i + 1] is ExtractAction nextExtract &&
                        IsPopoverExtraction(nextExtract)
                    )
                    {
                        continue;
                    }

                    // Normal extraction click'i.
                    if (
                        i < source.Count - 1 &&
                        source[i + 1] is ExtractAction ext
                    )
                    {
                        if (
                            click.CssSelector == ext.CssSelector ||
                            ext.CssSelector.Contains(click.CssSelector)
                        )
                        {
                            continue;
                        }
                    }
                }

                // ============================================================
                // INPUT
                // ============================================================

                else if (
                    current is InputAction input
                )
                {
                    var lastInput =
                        result.LastOrDefault(
                            a =>
                                a is InputAction)
                        as InputAction;

                    if (
                        lastInput != null &&
                        lastInput.CssSelector ==
                            input.CssSelector &&
                        lastInput.Value ==
                            input.Value
                    )
                    {
                        continue;
                    }
                }

                // ============================================================
                // HOVER
                // ============================================================

                else if (
                    current is HoverAction hover
                )
                {
                    if (
                        i <
                            source.Count - 1 &&
                        source[i + 1]
                            is HoverAction
                    )
                    {
                        continue;
                    }

                    if (
                        i <
                            source.Count - 1 &&
                        source[i + 1]
                            is ExtractAction nextExtract &&
                        IsPopoverExtraction(
                            nextExtract)
                    )
                    {
                        continue;
                    }

                    if (
                        i <
                            source.Count - 1 &&
                        source[i + 1]
                            is ClickAction nextClick
                    )
                    {
                        if (
                            nextClick.CssSelector ==
                                hover.CssSelector ||
                            nextClick.CssSelector.Contains(
                                hover.CssSelector)
                        )
                        {
                            continue;
                        }
                    }
                }

                result.Add(
                    current);
            }

            return result;
        }

        // ====================================================================
        // NAVIGATION TRIGGER LOOKUP
        // ====================================================================

        private bool NavigationIsTriggeredBy(
            List<UserAction> actions,
            string pageAlias,
            long clientSequence)
        {
            if (
                clientSequence <= 0
            )
            {
                return false;
            }

            return actions.Any(
                action =>
                    action is NavigationAction navigation &&
                    navigation.NavigationKind.Equals(
                        "UserAction",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        navigation.PageAlias,
                        pageAlias,
                        StringComparison.OrdinalIgnoreCase) &&
                    navigation.NavigationTriggerClientSequence ==
                        clientSequence
            );
        }

        // ====================================================================
        // EXTRACTION
        // ====================================================================

        private void GenerateExtraction(
            StringBuilder sb,
            ExtractAction ext,
            string locator,
            string varName)
        {
            string mode = string.IsNullOrWhiteSpace(ext.ExtractionMode) ? "Text" : ext.ExtractionMode;

            if (mode.Equals("Text", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine("// Normal DOM text extraction");
                
                // const yerine let kullanıyoruz ki temizlik yapabilelim
                sb.AppendLine($"let {varName} = (await {ext.PageAlias}.{locator}.innerText()).trim();");

                // YENİ MANTIK: TextContent olmadığı için sadece ExtractedValue'ya bakıyoruz.
                // Eğer kopyalanan veri sadece rakamlardan oluşuyorsa, çalışma anında elementteki diğer tüm karakterleri temizle.
                if (!string.IsNullOrWhiteSpace(ext.ExtractedValue) && 
                    ext.ExtractedValue.All(char.IsDigit))
                {
                    sb.AppendLine();
                    sb.AppendLine($"// Kullanıcı test kaydında sadece rakam kopyalamıştı, metin içindeki harf ve sembolleri ( : vb.) temizliyoruz.");
                    sb.AppendLine($"{varName} = {varName}.replace(/[^0-9]/g, '');");
                }
                else if (!string.IsNullOrWhiteSpace(ext.ExtractedValue))
                {
                    sb.AppendLine();
                    sb.AppendLine($"// Elementin sonundaki muhtemel gereksiz karakterleri (örn: iki nokta) temizliyoruz.");
                    sb.AppendLine($"{varName} = {varName}.replace(/[:]/g, '').trim();");
                }

                return;
            }

            if (
                mode.Equals(
                    "Attribute",
                    StringComparison.OrdinalIgnoreCase)
            )
            {
                string attribute =
                    string.IsNullOrWhiteSpace(
                        ext.AttributeName)
                        ? "value"
                        : ext.AttributeName;

                sb.AppendLine();

                sb.AppendLine(
                    $"// HTML attribute extraction: {attribute}");

                sb.AppendLine(
                    $"const {varName} = ((await {ext.PageAlias}.{locator}.getAttribute('{Escape(attribute)}')) ?? '').trim();");

                sb.AppendLine();

                sb.AppendLine(
                    $"if (!{varName}) throw new Error('Attribute extraction başarısız: {Escape(attribute)}');");

                return;
            }

            if (mode.StartsWith("Popover", StringComparison.OrdinalIgnoreCase))
            {
                GeneratePopoverExtraction(sb, ext, locator, varName);
                return;
            }

            sb.AppendLine();

            sb.AppendLine(
                $"// Bilinmeyen extraction mode '{Escape(mode)}'; Text fallback kullanılıyor");

            sb.AppendLine(
                $"const {varName} = (await {ext.PageAlias}.{locator}.innerText()).trim();");
        }

        // ====================================================================
        // POPOVER EXTRACTION
        // ====================================================================

        private void GeneratePopoverExtraction(
            StringBuilder sb,
            ExtractAction ext,
            string locator,
            string varName)
        {
            string attributeName = string.IsNullOrWhiteSpace(ext.AttributeName) ? "data-content" : ext.AttributeName;
            string label = string.IsNullOrWhiteSpace(ext.ExtractionLabel) ? "" : ext.ExtractionLabel;
            bool isHorizontal = ext.ExtractionMode.Equals("PopoverHorizontal", StringComparison.OrdinalIgnoreCase);

            sb.AppendLine();
            sb.AppendLine("// Bootstrap / HTML popover içindeki dinamik veri okunuyor");
            sb.AppendLine($"const {varName} = await {ext.PageAlias}.{locator}.evaluate((el) => {{");
            sb.AppendLine($"    const content = el.getAttribute('{Escape(attributeName)}') || '';");
            sb.AppendLine($"    if (!content) throw new Error('Popover attribute bulunamadı: {Escape(attributeName)}');");
            
            sb.AppendLine("    const parser = new DOMParser();");
            sb.AppendLine("    const doc = parser.parseFromString(content, 'text/html');");

            if (!string.IsNullOrWhiteSpace(label))
            {
                sb.AppendLine("    const rows = Array.from(doc.querySelectorAll('tr'));");
                sb.AppendLine("    if (rows.length === 0) throw new Error('Popover içinde tablo bulunamadı.');");
                sb.AppendLine();
                
                if (isHorizontal)
                {
                    sb.AppendLine("    // Yatay (Horizontal) tablo araması");
                    sb.AppendLine("    const headers = Array.from(rows[0].querySelectorAll('th, td')).map(h => (h.textContent || '').replace(/\\s+/g, ' ').trim());");
                    sb.AppendLine($"    const colIndex = headers.indexOf('{Escape(label)}');");
                    sb.AppendLine("    if (colIndex !== -1 && rows.length > 1) {");
                    sb.AppendLine("        const cells = Array.from(rows[1].querySelectorAll('th, td'));");
                    sb.AppendLine("        return (cells[colIndex]?.textContent || '').replace(/\\s+/g, ' ').trim();");
                    sb.AppendLine("    }");
                    sb.AppendLine($"    throw new Error('Yatay popover içinde \"{Escape(label)}\" sütunu bulunamadı.');");
                }
                else
                {
                    sb.AppendLine("    // Dikey (Vertical) tablo araması");
                    sb.AppendLine("    const row = rows.find((r) => {");
                    sb.AppendLine("        const cells = Array.from(r.querySelectorAll('th, td'));");
                    sb.AppendLine("        if (cells.length < 2) return false;");
                    sb.AppendLine("        const rowLabel = (cells[0].textContent || '').replace(/\\s+/g, ' ').trim();");
                    sb.AppendLine($"        return rowLabel === '{Escape(label)}';");
                    sb.AppendLine("    });");
                    sb.AppendLine();
                    sb.AppendLine($"    if (!row) throw new Error('Dikey popover içinde \"{Escape(label)}\" satırı bulunamadı.');");
                    sb.AppendLine("    const cells = Array.from(row.querySelectorAll('th, td'));");
                    sb.AppendLine("    return (cells[1]?.textContent || '').replace(/\\s+/g, ' ').trim();");
                }
            }
            else
            {
                sb.AppendLine("    throw new Error('Popover extraction için ExtractionLabel bulunamadı.');");
            }

            sb.AppendLine("});");
            sb.AppendLine();
            sb.AppendLine($"if (!{varName}) {{");
            sb.AppendLine($"    throw new Error('Popover extraction başarısız. Alan: {Escape(label)}');");
            sb.AppendLine("}");
        }

        // ====================================================================
        // POPOVER CHECK
        // ====================================================================

        private bool IsPopoverExtraction(ExtractAction action)
        {
            return !string.IsNullOrWhiteSpace(action.ExtractionMode) &&
                   action.ExtractionMode.StartsWith("Popover", StringComparison.OrdinalIgnoreCase);
        }

        // ====================================================================
        // SEARCH ENTER
        // ====================================================================

        private bool WasPrecededBySearchEnter(
            List<UserAction> actions,
            int currentIndex)
        {
            for (
                int j = currentIndex - 1;
                j >= 0 &&
                j >= currentIndex - 5;
                j--
            )
            {
                var action =
                    actions[j];

                if (
                    action is KeyboardAction keyboard &&
                    keyboard.Key.Equals(
                        "Enter",
                        StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }

                if (
                    action is NavigationAction ||
                    action is TabOpenedAction ||
                    action is TabActivatedAction
                )
                {
                    return false;
                }

                if (
                    action is ClickAction otherClick &&
                    !(
                        otherClick.Tag == "td" ||
                        otherClick.Tag == "tr" ||
                        otherClick.Tag == "th" ||
                        otherClick.Tag == "li"
                    )
                )
                {
                    return false;
                }
            }

            return false;
        }

        // ====================================================================
        // LOCATOR
        // ====================================================================

        private string BuildModernLocator(
            string placeholder,
            string ariaLabel,
            string text,
            string id,
            string tag,
            string name,
            string cssSelector,
            bool isDynamicListElement,
            string customTestId,
            string actionType,
            Dictionary<string, string> dynamicVariables = null) // EKLENDİ: Dinamik değişkenler parametresi
        {
            if (!string.IsNullOrWhiteSpace(customTestId))
            {
                return $"locator('[data-name=\"{EscapeDoubleQuoted(customTestId)}\"], [data-testid=\"{EscapeDoubleQuoted(customTestId)}\"]').first()";
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                return $"locator('#{Escape(id)}')";
            }

            if (!string.IsNullOrWhiteSpace(placeholder))
            {
                return $"getByPlaceholder('{Escape(placeholder)}').first()";
            }

            if (!string.IsNullOrWhiteSpace(ariaLabel))
            {
                return $"getByLabel('{Escape(ariaLabel)}').first()";
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                return $"locator('{Escape(tag)}[name=\"{EscapeDoubleQuoted(name)}\"]').first()";
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                bool isTableCell = tag == "td" || tag == "th";
                
                // YENİ MANTIK: Eğer metin içinde dinamik değişken değeri geçiyorsa, onu TypeScript değişken formatına çevir.
                string textLiteral = $"'{Escape(text)}'";
                if (dynamicVariables != null)
                {
                    foreach (var kvp in dynamicVariables)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Key) && text.Contains(kvp.Key))
                        {
                            string replacedText = Escape(text).Replace(Escape(kvp.Key), $"${{{kvp.Value}}}");
                            textLiteral = $"`{replacedText}`"; // TypeScript backtick string interpolasyonu
                            break;
                        }
                    }
                }

                if ((actionType == "Hover" || actionType == "Extract") && (isTableCell || tag == "tr"))
                {
                    // CSS fallback.
                }
                else if (isTableCell)
                {
                    return $"getByRole('cell', {{ name: {textLiteral}, exact: true }}).first()";
                }
                else
                {
                    // Gizli kopyalar için :visible kuralımız yerinde duruyor
                    return $"locator('{Escape(tag)}:visible').filter({{ hasText: {textLiteral} }}).first()";
                }
            }

            return $"locator('{Escape(cssSelector)}').first()";
        }

        // ====================================================================
        // URL ORIGIN
        // ====================================================================

        private string GetOrigin(
            string url)
        {
            if (
                Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out Uri? uri)
            )
            {
                return uri.GetLeftPart(
                    UriPartial.Authority);
            }

            return "";
        }

        // ====================================================================
        // RELATIVE URL
        // ====================================================================

        private string GetRelativeUrl(
            string url)
        {
            if (
                Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out Uri? uri)
            )
            {
                string relative =
                    uri.AbsolutePath;

                if (
                    !string.IsNullOrEmpty(
                        uri.Query)
                )
                {
                    relative +=
                        uri.Query;
                }

                if (
                    !string.IsNullOrEmpty(
                        uri.Fragment)
                )
                {
                    relative +=
                        uri.Fragment;
                }

                return string.IsNullOrEmpty(
                    relative)
                        ? "/"
                        : relative;
            }

            return
                string.IsNullOrWhiteSpace(
                    url)
                        ? "/"
                        : url;
        }

        // ====================================================================
        // NAVIGATION WAIT START
        // ====================================================================

        private void EmitNavigationWaitStart(
            StringBuilder sb,
            string pageAlias,
            string promiseName)
        {
            sb.AppendLine();
            sb.AppendLine($"// Aksiyon öncesi URL'nin ana domain (origin) ve path bilgilerini hafızaya alıyoruz.");
            sb.AppendLine($"const prevUrlObj_{promiseName} = new URL({pageAlias}.url());");
        }

        // ====================================================================
        // NAVIGATION WAIT END
        // ====================================================================

        private void EmitNavigationWaitEnd(
            StringBuilder sb,
            string pageAlias,
            string promiseName)
        {
            sb.AppendLine();
            sb.AppendLine($"// Başarılı olan manuel scriptinizdeki 'waitForURL' mantığının dinamik versiyonu:");
            sb.AppendLine($"// Dış SSO adreslerini atlar, kendi domainimize dönüldüğünde ve path değiştiğinde beklemeyi bitirir.");
            sb.AppendLine($"try {{");
            sb.AppendLine($"    await {pageAlias}.waitForURL(url =>");
            sb.AppendLine($"        url.origin === prevUrlObj_{promiseName}.origin &&");
            sb.AppendLine($"        url.pathname !== prevUrlObj_{promiseName}.pathname,");
            sb.AppendLine($"    {{ waitUntil: 'domcontentloaded', timeout: 15000 }});");
            sb.AppendLine($"}} catch (e) {{");
            sb.AppendLine($"    // Kategoriye tıklama gibi SPA içi URL değişmeyen ekran yenilenmelerinde hatayı yut ve devam et.");
            sb.AppendLine($"}}");
        }

        // ====================================================================
        // ESCAPE
        // ====================================================================

        private string Escape(
            string value)
        {
            if (
                string.IsNullOrEmpty(
                    value)
            )
            {
                return "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private string EscapeDoubleQuoted(
            string value)
        {
            if (
                string.IsNullOrEmpty(
                    value)
            )
            {
                return "";
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }
    }
}