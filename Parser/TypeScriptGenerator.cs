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
            sb.AppendLine("// Metni birebir (exact) eşleştiren yardımcı.");
            sb.AppendLine("// ':text-is()' sadece metni DOĞRUDAN taşıyan en küçük elementi eşlediği için,");
            sb.AppendLine("// metni <span>/<i> gibi bir alt elemente saran linklerde eşleşme bulunamıyordu.");
            sb.AppendLine("// hasText + anchor'lı regex hem alt ağacı tarar hem de substring karışıklığını önler.");
            sb.AppendLine(@"const exactText = (value: string): RegExp => {");
            sb.AppendLine(@"    const escaped = value.trim().replace(/[.*+?^${}()|[\]\\]/g, '\\$&').replace(/\s+/g, '\\s+');");
            sb.AppendLine(@"    return new RegExp('^\\s*' + escaped + '\\s*$');");
            sb.AppendLine(@"};");
            sb.AppendLine();
            sb.AppendLine("// Bir işlemi en fazla 'ms' milisaniye bekler. Playwright çağrılarının kendi timeout'ları teorik olarak");
            sb.AppendLine("// yeterli olsa da (SSO yönlendirmesi, beklenmedik bir dialog veya framework'ün kendi instrümantasyonu gibi");
            sb.AppendLine("// öngörülemeyen bir nedenle) beklenenden uzun sürmesi ihtimaline karşı testin akışını sabit bir üst sınırla korur.");
            sb.AppendLine(@"const withHardCap = async (task: () => Promise<void>, ms: number): Promise<void> => {");
            sb.AppendLine(@"    await Promise.race([task(), new Promise<void>(resolve => setTimeout(resolve, ms))]);");
            sb.AppendLine(@"};");
            sb.AppendLine();
            sb.AppendLine("// URL değişimi ya da belirli bir network isteği tamamlanmış olsa bile, ekranın kendisi (SPA render,");
            sb.AppendLine("// tablo/DOM güncellemesi) bu olaylardan biraz sonra tamamlanabiliyor. Bu yüzden 'yenileniyor' olarak");
            sb.AppendLine("// işaretlenen HER aksiyondan sonra, sıradaki adım bir veri okuma olsun ya da olmasın, DOM'un fiilen");
            sb.AppendLine("// durulmasını (belirli bir sessiz pencere boyunca mutasyon olmamasını) bekliyoruz. Sürekli değişen");
            sb.AppendLine("// (canlı saat, polling rozeti vb.) ekranlarda sonsuza kadar beklenmemesi için üst sınır (maxMs) var.");
            sb.AppendLine(@"const waitForDomSettle = async (pg: any, quietMs = 400, maxMs = 8000): Promise<void> => {");
            sb.AppendLine(@"    await pg.evaluate(({ quietMs, maxMs }: { quietMs: number; maxMs: number }) => new Promise<void>((resolve) => {");
            sb.AppendLine(@"        let quietTimer: ReturnType<typeof setTimeout>;");
            sb.AppendLine(@"        const finish = () => { observer.disconnect(); clearTimeout(quietTimer); clearTimeout(hardCapTimer); resolve(); };");
            sb.AppendLine(@"        const observer = new MutationObserver(() => {");
            sb.AppendLine(@"            clearTimeout(quietTimer);");
            sb.AppendLine(@"            quietTimer = setTimeout(finish, quietMs);");
            sb.AppendLine(@"        });");
            sb.AppendLine(@"        observer.observe(document.body, { childList: true, subtree: true, attributes: true, characterData: true });");
            sb.AppendLine(@"        quietTimer = setTimeout(finish, quietMs);");
            sb.AppendLine(@"        const hardCapTimer = setTimeout(finish, maxMs);");
            sb.AppendLine(@"    }), { quietMs, maxMs }).catch(() => {});");
            sb.AppendLine(@"};");
            sb.AppendLine();
            sb.AppendLine("// Bazı veriler (ör. bir popover'ın data-content'i) DataTables gibi kütüphanelerin kendi");
            sb.AppendLine("// redraw döngüsüyle, ilişkili network isteği zaten tamamlanmış olsa bile bir miktar gecikmeyle");
            sb.AppendLine("// DOM'a yansır. Bunu sabit bir süre bekleyip 'umarım gelmiştir' diye tahmin ederek değil,");
            sb.AppendLine("// tarayıcının bize ilk DOM mutasyonunu haber vermesini bekleyerek (ya da hiç mutasyon olmazsa");
            sb.AppendLine("// üst sınırda vazgeçerek) ele alıyoruz; okuma bu olayın hemen ardından tekrar denenir.");
            sb.AppendLine(@"const waitForNextMutation = async (pg: any, maxMs = 3000): Promise<void> => {");
            sb.AppendLine(@"    await pg.evaluate((maxMs: number) => new Promise<void>((resolve) => {");
            sb.AppendLine(@"        const finish = () => { observer.disconnect(); clearTimeout(hardCapTimer); resolve(); };");
            sb.AppendLine(@"        const observer = new MutationObserver(finish);");
            sb.AppendLine(@"        observer.observe(document.body, { childList: true, subtree: true, attributes: true, characterData: true });");
            sb.AppendLine(@"        const hardCapTimer = setTimeout(finish, maxMs);");
            sb.AppendLine(@"    }), maxMs).catch(() => {});");
            sb.AppendLine(@"};");
            sb.AppendLine();
            sb.AppendLine("test('SenseWright Auto-Generated E2E Test', async ({ page, context }) => {");
            sb.AppendLine("    test.setTimeout(600_000); // 10 dakikalık genel timeout");
            sb.AppendLine("    // Varsayılan aksiyon timeout'u: bir element bulunamazsa 10 dakika sessizce beklemek yerine");
            sb.AppendLine("    // 30 saniyede anlamlı bir hata ile düşsün. Bu context'teki tüm sayfalar için geçerlidir.");
            sb.AppendLine("    context.setDefaultTimeout(30_000);");
            sb.AppendLine();

            if (actions.Any(a => a is TabOpenedAction))
            {
                sb.AppendLine("    // Uygulamanın açtığı yeni sekmeyi yakalar. Sonsuz döngü yerine olay tabanlı bekleme:");
                sb.AppendLine("    // sekme bu noktadan önce açıldıysa onu bulur, sonra açılırsa 'page' olayını dinler,");
                sb.AppendLine("    // hiç açılmazsa testi askıya almadan mevcut son sayfa ile devam eder.");
                sb.AppendLine("    const knownPages = new Set(context.pages());");
                sb.AppendLine("    const acquireNewPage = async (alias: string, timeout = 30_000) => {");
                sb.AppendLine("        const alreadyOpen = context.pages().find(candidate => !knownPages.has(candidate));");
                sb.AppendLine("        if (alreadyOpen) {");
                sb.AppendLine("            knownPages.add(alreadyOpen);");
                sb.AppendLine("            return alreadyOpen;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        const opened = await context.waitForEvent('page', { timeout }).catch(() => null);");
                sb.AppendLine("        if (opened) {");
                sb.AppendLine("            knownPages.add(opened);");
                sb.AppendLine("            return opened;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        await reportStepInfo(`${alias} için beklenen yeni sekme ${timeout} ms içinde açılmadı; uygulama aynı sekmede devam etmiş olabilir.`);");
                sb.AppendLine("        const fallback = context.pages()[context.pages().length - 1];");
                sb.AppendLine("        knownPages.add(fallback);");
                sb.AppendLine("        return fallback;");
                sb.AppendLine("    };");
                sb.AppendLine();
            }

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

            // Bir tıklama, ekranı yenileyip bir popover extraction'ın okuyacağı veriyi güncelliyorsa,
            // tıklamadan hemen önce o popover'ın MEVCUT (muhtemelen eski) değerinin anlık görüntüsü alınır.
            // Extraction daha sonra, okuduğu değer bu anlık görüntüyle aynı olduğu sürece "henüz güncellenmedi"
            // sayıp tekrar dener; böylece yenileme tamamlanmadan eski verinin kopyalanması engellenir.
            var popoverSnapshotVars = new Dictionary<ExtractAction, string>();
            int popoverSnapshotCounter = 1;

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
                    sb.AppendLine("        // Uygulamanın açtığı yeni sekme yakalanıyor.");
                    sb.AppendLine($"        const {p} = await acquireNewPage('{p}');");
                    sb.AppendLine($"        await {p}.waitForLoadState('domcontentloaded');");
                    sb.AppendLine($"        await {p}.bringToFront();");

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

                    string popoverSnapshotVar = popoverSnapshotVars.TryGetValue(ext, out string foundSnapshotVar) ? foundSnapshotVar : null;
                    GenerateExtraction(sb, ext, locator, varName, popoverSnapshotVar);
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

                    void EmitInputStatement()
                    {
                        if (dynamicVariables.TryGetValue(cleanInputValue, out string matchedVar))
                        {
                            sb.AppendLine("        // Hafızadaki dinamik değişken alana dolduruluyor");
                            sb.AppendLine($"        await {input.PageAlias}.{locator}.fill({matchedVar});");
                        }
                        else
                        {
                            sb.AppendLine($"        await {input.PageAlias}.{locator}.fill('{Escape(input.Value)}');");
                        }
                    }

                    List<NetworkAction> inputTriggeredNetworks = GetTriggeredNetworkActions(actions, i, input.PageAlias);
                    if (inputTriggeredNetworks.Count > 0)
                    {
                        string netListVar = $"networkWaits_{navigationCounter++}";
                        EmitNetworkWaitStart(sb, p, inputTriggeredNetworks, netListVar);
                        EmitInputStatement();
                        EmitNetworkWaitEnd(sb, p, netListVar);
                    }
                    else
                    {
                        EmitInputStatement();
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

                    string clickLocator = string.Equals(click.Tag, "label", StringComparison.OrdinalIgnoreCase) &&
                                          !string.IsNullOrWhiteSpace(click.ForAttribute)
                        ? $"locator('label[for=\"{EscapeDoubleQuoted(click.ForAttribute)}\"]').first()"
                        : BuildModernLocator(
                            click.Placeholder, click.AriaLabel, click.TextContent, click.ElementId, click.Tag, click.Name,
                            click.CssSelector, click.IsDynamicListElement, click.CustomTestId, "Click", dynamicVariables);

                    // Bu tıklama hemen ardından bir popover extraction'ın okuyacağı ekranı yeniliyorsa, tıklamadan
                    // ÖNCE o popover'ın mevcut değerinin anlık görüntüsünü alıyoruz. Extraction bu değeri okuduğunda
                    // hâlâ aynıysa (yani sayfa henüz güncellenmemişse) veriyi kabul etmeyip yeniden deneyecek.
                    // Not: tıklama ile extraction arasında, bu tıklamanın kendi ürettiği NavigationAction/NetworkAction
                    // kayıtları araya girebiliyor (ör. SPA URL değişimi) - bunlar gerçek bir ayrı adım olmadığından atlanıyor.
                    ExtractAction nextPopoverExt = FindTriggeredPopoverExtraction(actions, i, click.PageAlias);
                    if (nextPopoverExt != null)
                    {
                        string snapshotExtractLocator = BuildModernLocator(
                            nextPopoverExt.Placeholder, nextPopoverExt.AriaLabel, "", nextPopoverExt.ElementId, nextPopoverExt.Tag, nextPopoverExt.Name,
                            nextPopoverExt.CssSelector, nextPopoverExt.IsDynamicListElement, nextPopoverExt.CustomTestId, "Extract", dynamicVariables);
                        string snapshotAttribute = string.IsNullOrWhiteSpace(nextPopoverExt.AttributeName) ? "data-content" : nextPopoverExt.AttributeName;
                        string snapshotVar = $"popoverSnapshot_{popoverSnapshotCounter++}";

                        sb.AppendLine($"        // Yenileme öncesi popover verisinin anlık görüntüsü (staleness kontrolü için)");
                        sb.AppendLine($"        const {snapshotVar} = await {p}.{snapshotExtractLocator}.getAttribute('{Escape(snapshotAttribute)}', {{ timeout: 2000 }}).catch(() => null);");

                        popoverSnapshotVars[nextPopoverExt] = snapshotVar;
                    }

                    List<NetworkAction> triggeredNetworks = GetTriggeredNetworkActions(actions, i, click.PageAlias);

                    // Bu tıklama, aynı sayfadaki bir önceki tıklamanın hemen ardından geliyorsa (ör. bir menü/
                    // dropdown açan tıklama + içindeki bir öğeyi seçen tıklama) ve hedef metne göre aranıyorsa,
                    // bazı uygulamalarda önceki aksiyonun tetiklediği arka plan yenilemesi (navigasyon, network
                    // isteği tetikleyen ya da sade bir tıklama fark etmeksizin) menüyü kapatıp yeniden çiziyor;
                    // ilk denemede öğe henüz görünmeyebilir. Bu durumda açan elementi tekrar tıklayıp yeniden
                    // denemek, tek seferlik bir tıklamadan daha güvenilir. Bu yüzden bu kontrol/sarmalama
                    // tıklamanın hangi dala (navigasyon/network/sade) düştüğünden bağımsız olarak uygulanıyor.
                    bool opensFromPriorClick = i > 0 && actions[i - 1] is ClickAction prevClickForMenu &&
                                                prevClickForMenu.PageAlias == click.PageAlias &&
                                                clickLocator.Contains("filter({ hasText:");
                    string openerLocator = null;
                    if (opensFromPriorClick)
                    {
                        ClickAction openerClick = (ClickAction)actions[i - 1];
                        openerLocator = BuildModernLocator(
                            openerClick.Placeholder, openerClick.AriaLabel, openerClick.TextContent, openerClick.ElementId, openerClick.Tag, openerClick.Name,
                            openerClick.CssSelector, openerClick.IsDynamicListElement, openerClick.CustomTestId, "Click", dynamicVariables);
                    }

                    void EmitClickStatement()
                    {
                        if (opensFromPriorClick)
                        {
                            sb.AppendLine($"        // Menü/dropdown öğesi seçimi: öğe görünmezse açan elementi tekrar tıklayıp yeniden deneniyor.");
                            sb.AppendLine($"        {{");
                            sb.AppendLine($"            let menuItemClicked = false;");
                            sb.AppendLine($"            for (let attempt = 0; attempt < 3 && !menuItemClicked; attempt++) {{");
                            sb.AppendLine($"                try {{");
                            sb.AppendLine($"                    await {p}.{clickLocator}.click({{ timeout: 5000 }});");
                            sb.AppendLine($"                    menuItemClicked = true;");
                            sb.AppendLine($"                }} catch (e) {{");
                            sb.AppendLine($"                    await {p}.{openerLocator}.click().catch(() => {{}});");
                            sb.AppendLine($"                }}");
                            sb.AppendLine($"            }}");
                            sb.AppendLine($"            if (!menuItemClicked) {{");
                            sb.AppendLine($"                await {p}.{clickLocator}.click();");
                            sb.AppendLine($"            }}");
                            sb.AppendLine($"        }}");
                        }
                        else
                        {
                            sb.AppendLine($"        await {p}.{clickLocator}.click();");
                        }
                    }

                    if (followsNavigation)
                    {
                        string promise = $"navigationPromise_{navigationCounter++}";
                        EmitNavigationWaitStart(sb, p, promise);
                        string netListVar = null;
                        if (triggeredNetworks.Count > 0)
                        {
                            netListVar = $"networkWaits_{navigationCounter++}";
                            EmitNetworkWaitStart(sb, p, triggeredNetworks, netListVar);
                        }
                        EmitClickStatement();
                        EmitNavigationWaitEnd(sb, p, promise);
                        if (netListVar != null) EmitNetworkWaitEnd(sb, p, netListVar, includeDomSettle: false);
                    }
                    else if (triggeredNetworks.Count > 0)
                    {
                        string netListVar = $"networkWaits_{navigationCounter++}";
                        EmitNetworkWaitStart(sb, p, triggeredNetworks, netListVar);
                        EmitClickStatement();
                        EmitNetworkWaitEnd(sb, p, netListVar);
                    }
                    else
                    {
                        EmitClickStatement();
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
                    List<NetworkAction> triggeredNetworks = GetTriggeredNetworkActions(actions, i, select.PageAlias);
                    void EmitSelectStatement() => sb.AppendLine($"        await {p}.{locator}.selectOption('{Escape(select.SelectedValue)}');");

                    if (followsNavigation)
                    {
                        string promise = $"navigationPromise_{navigationCounter++}";
                        EmitNavigationWaitStart(sb, p, promise);
                        string netListVar = null;
                        if (triggeredNetworks.Count > 0)
                        {
                            netListVar = $"networkWaits_{navigationCounter++}";
                            EmitNetworkWaitStart(sb, p, triggeredNetworks, netListVar);
                        }
                        EmitSelectStatement();
                        EmitNavigationWaitEnd(sb, p, promise);
                        if (netListVar != null) EmitNetworkWaitEnd(sb, p, netListVar, includeDomSettle: false);
                    }
                    else if (triggeredNetworks.Count > 0)
                    {
                        string netListVar = $"networkWaits_{navigationCounter++}";
                        EmitNetworkWaitStart(sb, p, triggeredNetworks, netListVar);
                        EmitSelectStatement();
                        EmitNetworkWaitEnd(sb, p, netListVar);
                    }
                    else
                    {
                        EmitSelectStatement();
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
                    List<NetworkAction> triggeredNetworks = GetTriggeredNetworkActions(actions, i, keyboard.PageAlias);
                    void EmitKeyboardStatement() => sb.AppendLine($"        await {p}.{locator}.press('{Escape(keyboard.Key)}');");

                    if (followsNavigation)
                    {
                        string promise = $"navigationPromise_{navigationCounter++}";
                        EmitNavigationWaitStart(sb, p, promise);
                        string netListVar = null;
                        if (triggeredNetworks.Count > 0)
                        {
                            netListVar = $"networkWaits_{navigationCounter++}";
                            EmitNetworkWaitStart(sb, p, triggeredNetworks, netListVar);
                        }
                        EmitKeyboardStatement();
                        EmitNavigationWaitEnd(sb, p, promise);
                        if (netListVar != null) EmitNetworkWaitEnd(sb, p, netListVar, includeDomSettle: false);
                    }
                    else if (triggeredNetworks.Count > 0)
                    {
                        string netListVar = $"networkWaits_{navigationCounter++}";
                        EmitNetworkWaitStart(sb, p, triggeredNetworks, netListVar);
                        EmitKeyboardStatement();
                        EmitNetworkWaitEnd(sb, p, netListVar);
                    }
                    else
                    {
                        EmitKeyboardStatement();
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
            sb.AppendLine("    } catch (error: unknown) {");
            sb.AppendLine("        // TypeScript strict modunda catch değişkeni 'unknown' tipindedir; mesaj güvenli şekilde çıkarılıyor.");
            sb.AppendLine("        const errorMessage = error instanceof Error ? error.message : String(error);");
            sb.AppendLine("        await reportStepFail(`Test sırasında bir hata oluştu: ${errorMessage}`);");
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
        
        // Bir aksiyonun ardından, o aksiyonun yan etkisi olarak oluşan TÜM network isteklerini toplar (tek bir
        // istekle sınırlı değil - ör. bir "Çözümle" tıklaması önce bir submit POST'u, ardından ayrı bir özet/
        // detay GET'i tetikleyebilir). Araya giren NavigationAction'lar (SPA URL değişimi) atlanır çünkü onlar da
        // aksiyonun kendi yan etkisidir; asıl bir sonraki kullanıcı adımına (Click/Input/Keyboard/Select/Extract/
        // Hover/Assert) ulaşıldığında tarama durur.
        private List<NetworkAction> GetTriggeredNetworkActions(List<UserAction> actions, int currentIndex, string pageAlias)
        {
            var result = new List<NetworkAction>();
            for (int j = currentIndex + 1; j < actions.Count; j++)
            {
                var next = actions[j];
                if (next is TabOpenedAction || next is TabActivatedAction) continue;
                if (!string.Equals(next.PageAlias, pageAlias, StringComparison.OrdinalIgnoreCase)) break;
                if (next is NavigationAction) continue;
                if (next is NetworkAction netAction) { result.Add(netAction); continue; }
                break;
            }
            return result;
        }

        // Bir aksiyondan ÖNCE, o aksiyonun tetikleyeceği (kayıt sırasında tespit edilmiş) network isteklerini
        // yakalamak için promise'ler kuruluyor. Her istek kendi başına en fazla 25 saniye bekleniyor; hepsi aynı
        // anda (paralel) başlatıldığından toplam bekleme süresi istek sayısıyla ÇARPILMIYOR, en yavaş isteğin
        // süresi kadar sürer. Zaman aşımında sessizce devam edilir ki alakasız/gereksiz bir istek (ör. arka planda
        // sürekli atılan bir heartbeat/polling çağrısı) testi sonsuza kadar kilitlemesin.
        private void EmitNetworkWaitStart(StringBuilder sb, string pageAlias, List<NetworkAction> networkActions, string listVarName)
        {
            sb.AppendLine($"        // Bu aksiyonun tetiklediği, kayıt sırasında tespit edilmiş {networkActions.Count} adet arka plan isteği için");
            sb.AppendLine($"        // önceden promise kuruluyor (her biri bağımsız ve paralel, en fazla 25sn).");
            sb.AppendLine($"        const {listVarName} = [");
            foreach (var na in networkActions)
            {
                string apiPath = Uri.TryCreate(na.Url, UriKind.Absolute, out Uri apiUri) ? apiUri.AbsolutePath : na.Url;
                sb.AppendLine($"            {pageAlias}.waitForResponse(resp => resp.url().includes('{Escape(apiPath)}') && resp.request().method() === '{Escape(na.Method)}', {{ timeout: 25000 }}).catch(() => {{}}),");
            }
            sb.AppendLine($"        ];");
        }

        private void EmitNetworkWaitEnd(StringBuilder sb, string pageAlias, string listVarName, bool includeDomSettle = true)
        {
            sb.AppendLine($"        await Promise.all({listVarName});");
            if (includeDomSettle)
            {
                sb.AppendLine($"        // İstek(ler) tamamlanmış olsa bile DOM'un onu yansıtacak şekilde güncellenmesi biraz sürebilir; bekleniyor.");
                sb.AppendLine($"        await waitForDomSettle({pageAlias});");
            }
        }

        // Bir tıklamanın hemen ardından, o tıklamanın tetiklediği bir popover extraction var mı diye bakar.
        // Aradaki NavigationAction/NetworkAction kayıtları (tıklamanın kendi yan etkisi, ör. SPA URL değişimi
        // ya da AJAX isteği) atlanır; araya başka bir Click/Input/Keyboard/Select/Hover/Assert girerse
        // extraction bu tıklamaya değil ona ait sayılır ve null döner.
        private ExtractAction FindTriggeredPopoverExtraction(List<UserAction> actions, int currentIndex, string pageAlias)
        {
            for (int j = currentIndex + 1; j < actions.Count; j++)
            {
                var next = actions[j];
                if (next is TabOpenedAction || next is TabActivatedAction) continue;
                if (!string.Equals(next.PageAlias, pageAlias, StringComparison.OrdinalIgnoreCase)) return null;
                if (next is NavigationAction || next is NetworkAction) continue;
                if (next is ExtractAction extAction && IsPopoverExtraction(extAction)) return extAction;
                return null;
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

        private void GenerateExtraction(StringBuilder sb, ExtractAction ext, string locator, string varName, string popoverSnapshotVar = null)
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
                GeneratePopoverExtraction(sb, ext, locator, varName, popoverSnapshotVar);
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

        private void GeneratePopoverExtraction(StringBuilder sb, ExtractAction ext, string locator, string varName, string popoverSnapshotVar = null)
        {
            string attributeName = string.IsNullOrWhiteSpace(ext.AttributeName) ? "data-content" : ext.AttributeName;
            string label = string.IsNullOrWhiteSpace(ext.ExtractionLabel) ? "" : ext.ExtractionLabel;
            bool isHorizontal = ext.ExtractionMode.Equals("PopoverHorizontal", StringComparison.OrdinalIgnoreCase);
            int labelIndex = ext.ExtractionLabelIndex;

            sb.AppendLine();
            sb.AppendLine("        // Bu popover'ı yenileyen tıklama zaten kendi tetiklediği network isteğini/isteklerini ve");
            sb.AppendLine("        // ardından DOM'un durulmasını (waitForDomSettle) bekledi. Bazı tablo kütüphaneleri (ör.");
            sb.AppendLine("        // DataTables) satırı komple yeniden oluşturduğu için, veri gelmiş olsa bile bu spesifik");
            sb.AppendLine("        // hücreyi az bir gecikmeyle günceller; bu yüzden locator her denemede YENİDEN sorgulanıyor");
            sb.AppendLine("        // (element değişmiş olsa bile eskisine takılı kalınmaz) ve denemeler arasında sabit bir süre");
            sb.AppendLine("        // beklemek yerine tarayıcının bir sonraki DOM mutasyonunu haber vermesi bekleniyor");
            sb.AppendLine("        // (waitForNextMutation) — veri gelir gelmez hemen tekrar okunur, gelmezse üst sınırda vazgeçilir.");
            sb.AppendLine("        // Ekranı yenileyen aksiyondan önce alınmış anlık görüntüyle karşılaştırılıyor; okunan değer");
            sb.AppendLine("        // o anlık görüntüyle birebir aynıysa (yenileme henüz gerçekleşmemiş demektir) veri 'hazır");
            sb.AppendLine("        // değil' sayılıp yeniden denenecek.");
            sb.AppendLine($"        let {varName} = '';");
            sb.AppendLine($"        const popoverMaxAttempts = 8; // güvenlik ağı: mutasyon olayı gelmezse en fazla ~8 x 3sn bekler");
            sb.AppendLine($"        for (let popoverAttempt = 0; popoverAttempt < popoverMaxAttempts && !{varName}; popoverAttempt++) {{");
            sb.AppendLine($"            if (popoverAttempt > 0) await waitForNextMutation({ext.PageAlias}, 3000);");
            string snapshotArg = string.IsNullOrWhiteSpace(popoverSnapshotVar) ? "null" : popoverSnapshotVar;
            sb.AppendLine($"            {varName} = await {ext.PageAlias}.{locator}.evaluate((el, prevValue) => {{");
            sb.AppendLine($"                const content = el.getAttribute('{Escape(attributeName)}') || '';");
            sb.AppendLine($"                if (!content) return '';");
            sb.AppendLine("                // Popover içeriğinin TAMAMI (ham HTML) tıklamadan önceki anlık görüntüyle birebir");
            sb.AppendLine("                // aynıysa ekran henüz yenilenmemiş demektir; hangi alanın okunacağından bağımsız,");
            sb.AppendLine("                // genel bir 'değişti mi' kontrolü olduğu için tüm extraction türlerinde çalışır.");
            sb.AppendLine("                if (prevValue !== null && content === prevValue) return '';");

            sb.AppendLine("                const parser = new DOMParser();");
            sb.AppendLine("                const doc = parser.parseFromString(content, 'text/html');");

            if (!string.IsNullOrWhiteSpace(label))
            {
                sb.AppendLine("                const rows = Array.from(doc.querySelectorAll('tr'));");
                sb.AppendLine("                if (rows.length === 0) return '';");
                sb.AppendLine();

                if (isHorizontal)
                {
                    sb.AppendLine("                // Yatay (Horizontal) tablo araması");
                    sb.AppendLine("                const headers = Array.from(rows[0].querySelectorAll('th, td')).map(h => (h.textContent || '').replace(/\\s+/g, ' ').trim());");
                    sb.AppendLine($"                const colIndex = headers.indexOf('{Escape(label)}');");
                    sb.AppendLine($"                if (colIndex !== -1 && rows.length > {labelIndex + 1}) {{");
                    sb.AppendLine($"                    const cells = Array.from(rows[{labelIndex + 1}].querySelectorAll('th, td'));");
                    sb.AppendLine("                    const resultText = (cells[colIndex]?.textContent || '').replace(/\\s+/g, ' ').trim();");
                    sb.AppendLine("                    return resultText;");
                    sb.AppendLine("                }");
                    sb.AppendLine("                return '';");
                }
                else
                {
                    sb.AppendLine("                // Dikey (Vertical) tablo araması (Aynı etiketten birden fazla varsa Index ile filtreliyoruz)");
                    sb.AppendLine($"                const matchingRows = rows.filter((r) => {{");
                    sb.AppendLine("                    const cells = Array.from(r.querySelectorAll('th, td'));");
                    sb.AppendLine("                    if (cells.length < 2) return false;");
                    sb.AppendLine("                    const rowLabel = (cells[0].textContent || '').replace(/\\s+/g, ' ').trim();");
                    sb.AppendLine($"                    return rowLabel === '{Escape(label)}';");
                    sb.AppendLine("                });");
                    sb.AppendLine();
                    sb.AppendLine($"                if (matchingRows.length <= {labelIndex}) return '';");
                    sb.AppendLine($"                const cells = Array.from(matchingRows[{labelIndex}].querySelectorAll('th, td'));");
                    sb.AppendLine("                const resultText = (cells[1]?.textContent || '').replace(/\\s+/g, ' ').trim();");
                    sb.AppendLine("                return resultText;");
                }
            }
            else
            {
                sb.AppendLine("                return '';");
            }

            sb.AppendLine($"            }}, {snapshotArg});"); // Evaluate fonksiyonu burada kapanır
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine($"        if (!{varName}) {{");
            sb.AppendLine($"            throw new Error('Popover extraction başarısız (birkaç deneme sonrasında veri gelmedi ya da ekran yenilenmeden önceki değerden farklılaşmadı). Alan: {Escape(label)}');");
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
                // Sondaki bildirim rozeti (örn: "Gelen Kutusu 5", "Bekleyenler 12+") gibi kısa sayısal ekler
                // ya da metnin herhangi bir yerinde geçen uzun (4+ haneli) rakam dizileri (kayıt/talep/bilet
                // numarası, telefon numarası vb.) test koşusundan koşusuna değişen değerlerdir. Bunları literal
                // metin olarak eşleştirmeye çalışmak, kayıt anındaki değeri sabitleyip sonraki koşularda
                // "element bulunamadı" hatasına yol açar; bu yüzden metin eşleşmesi yerine id/CSS selector
                // gibi konum tabanlı bir stratejiye düşülüyor.
                bool hasDynamicBadge = System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"\s+\d+\+?$")
                    || System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"\d{4,}");

                if (!hasDynamicBadge)
                {
                    string procText = ProcessString(text, out bool hasVar);
                    string targetTag = string.IsNullOrWhiteSpace(tag) ? "*:visible" : $"{tag}:visible";

                    // input[type=button|submit] metnini textContent'te değil value attribute'unda taşır;
                    // hasText onları göremediği için text engine ile eşleştirmeye devam ediyoruz.
                    if (string.Equals(tag, "input", StringComparison.OrdinalIgnoreCase) && !hasVar)
                    {
                        return $"locator('{targetTag}:text-is(\"{procText.Replace("\"", "\\\"")}\")').first()";
                    }

                    string quote = hasVar ? "`" : "'";
                    return $"locator('{targetTag}').filter({{ hasText: exactText({quote}{procText}{quote}) }}).first()";
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
            sb.AppendLine($"        // withHardCap: içerideki adımlar (her biri kendi 3sn timeout'una sahip olsa da) beklenmedik bir");
            sb.AppendLine($"        // nedenle uzarsa test 7 saniyeden fazla burada kilitlenmesin diye üstten sabit bir üst sınır konuyor.");
            sb.AppendLine($"        await withHardCap(async () => {{");
            sb.AppendLine($"            try {{");
            sb.AppendLine($"                await {pageAlias}.waitForURL(url =>");
            sb.AppendLine($"                    url.origin === prevUrlObj_{promiseName}.origin &&");
            sb.AppendLine($"                    url.pathname !== prevUrlObj_{promiseName}.pathname,");
            sb.AppendLine($"                {{ waitUntil: 'domcontentloaded', timeout: 3000 }});");
            sb.AppendLine($"            }} catch (e) {{");
            sb.AppendLine($"                await {pageAlias}.waitForLoadState('networkidle', {{ timeout: 3000 }}).catch(() => {{}});");
            sb.AppendLine($"            }}");
            sb.AppendLine($"        }}, 7000);");
            sb.AppendLine($"        // URL/networkidle kontrolü tek başına DOM'un fiilen güncellendiğini garanti etmez; ekranın durulmasını bekliyoruz.");
            sb.AppendLine($"        await waitForDomSettle({pageAlias});");
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
