using System.Text.Json;
using WebApp.Models;
using System.Linq;

namespace WebApp.Services;

public sealed class ContentService
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public ContentService(HttpClient http) => _http = http;

    // ---------------------------------------------------------------------
    // Base loader (unchanged)
    // ---------------------------------------------------------------------
    public async Task<T?> GetAsync<T>(string name, CancellationToken ct = default)
    {
        try
        {
            var url = $"content/{name}.json";
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return default;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct);
        }
        catch
        {
            return default;
        }
    }

    // ---------------------------------------------------------------------
    // Helpers (guarded JSON utilities)
    // ---------------------------------------------------------------------
    private static string? Str(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonElement? Obj(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static IEnumerable<JsonElement> Arr(JsonElement obj, string prop)
        => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    // ---------------------------------------------------------------------
    // Header parsing (new shape + legacy mapping), returned as HeaderConfig
    // ---------------------------------------------------------------------
    private static HeaderConfig ParseNewHeader(JsonElement rootOrHeader)
    {
        // Allow either { brand, groups, ... } or a wrapper { header:{...} }
        var h = rootOrHeader;
        if (h.TryGetProperty("header", out var inner) && inner.ValueKind == JsonValueKind.Object)
            h = inner;

        // brand
        BrandLink brand;
        if (h.TryGetProperty("brand", out var b) && b.ValueKind == JsonValueKind.Object)
        {
            brand = new BrandLink(
                Text: Str(b, "text") ?? Str(b, "label") ?? "BioMaint",
                Href: Str(b, "href") ?? "/"
            );
        }
        else
        {
            brand = new BrandLink("BioMaint", "/");
        }

        // groups
        var groups = new List<NavGroup>();
        foreach (var g in Arr(h, "groups"))
        {
            var id    = Str(g, "id");
            var label = Str(g, "label") ?? "";

            var items = Arr(g, "items")
                .Select(i => new NavChild(
                    Label: Str(i, "label") ?? "",
                    Href : Str(i, "href")  ?? "#",
                    External: false
                ))
                .ToList();

            if (items.Count > 0)
            {
                groups.Add(new NavGroup(id, label, items, null));      // positional
            }
            else
            {
                groups.Add(new NavGroup(id, label, null, Str(g, "href") ?? "#")); // positional
            }
        }

        // auth
        AuthConfig? auth = null;
        var authObj = Obj(h, "auth");
        if (authObj is JsonElement ae)
            auth = new AuthConfig(Str(ae, "loginText"), Str(ae, "loginHref"));

        // cta
        CtaConfig? cta = null;
        var ctaObj = Obj(h, "cta");
        if (ctaObj is JsonElement ce)
            cta = new CtaConfig(Str(ce, "text"), Str(ce, "href"));

        return new HeaderConfig(brand, groups, auth, cta);
    }

    private static HeaderConfig MapLegacyHeaderToNew(JsonElement legacyRoot)
    {
        // legacy: { "brand": "BioMaint", menus:[{label,items:[{label,href},...] }], pricingHref, loginHref, cta:{label,href} }
        var brand = new BrandLink(Str(legacyRoot, "brand") ?? "BioMaint", "/");

        var groups = new List<NavGroup>();
        foreach (var menu in Arr(legacyRoot, "menus"))
        {
            var label = Str(menu, "label") ?? "";
            var items = Arr(menu, "items")
                .Select(i => new NavChild(
                    Label: Str(i, "label") ?? "",
                    Href : Str(i, "href")  ?? "#"
                ))
                .ToList();

            groups.Add(new NavGroup(null, label, items, null)); // positional (fixes CS1739)
        }

        var pricingHref = Str(legacyRoot, "pricingHref");
        if (!string.IsNullOrWhiteSpace(pricingHref))
            groups.Add(new NavGroup("pricing", "Pricing", null, pricingHref)); // positional (fixes CS1739)

        var auth = default(AuthConfig);
        var loginHref = Str(legacyRoot, "loginHref");
        if (!string.IsNullOrWhiteSpace(loginHref))
            auth = new AuthConfig("Log in", loginHref);

        var cta = default(CtaConfig);
        var ctaObj = Obj(legacyRoot, "cta");
        if (ctaObj is JsonElement ce)
            cta = new CtaConfig(Str(ce, "label") ?? "Try for Free", Str(ce, "href") ?? "#demo");

        return new HeaderConfig(brand, groups, auth, cta);
    }

    /// <summary>
    /// Gets the header config from:
    /// 1) landing.json (header block)
    /// 2) header.json (if you split it out later)
    /// 3) legacy nav.header.json (mapped)
    /// 4) a minimal hardcoded fallback
    /// </summary>
    public async Task<HeaderConfig> GetHeaderConfigAsync(CancellationToken ct = default)
    {
        // 1) landing.json -> header
        try
        {
            using var landing = await GetAsync<JsonDocument>("landing", ct);
            if (landing is not null)
            {
                var lr = landing.RootElement;
                if (lr.TryGetProperty("header", out var hdr) && hdr.ValueKind == JsonValueKind.Object)
                    return ParseNewHeader(hdr);
            }
        }
        catch { /* ignore */ }

        // 2) header.json (optional)
        try
        {
            using var header = await GetAsync<JsonDocument>("header", ct);
            if (header is not null)
                return ParseNewHeader(header.RootElement);
        }
        catch { /* ignore */ }

        // 3) legacy nav.header.json
        try
        {
            using var legacy = await GetAsync<JsonDocument>("nav.header", ct);
            if (legacy is not null)
                return MapLegacyHeaderToNew(legacy.RootElement);
        }
        catch { /* ignore */ }

        // 4) minimal fallback (never fail)
        return new HeaderConfig(
            new BrandLink("BioMaint", "/"),
            new List<NavGroup>
            {
                new("product","Product",   null, "#features"),
                new("solutions","Solutions",null,"#solutions"),
                new("resources","Resources",null,"#features"),
                new("pricing","Pricing",   null, "/pricing")
            },
            new AuthConfig("Log in", "/auth/login"),
            new CtaConfig("Try for Free", "#demo")
        );
    }

    // =====================================================================
    // Footer: nav.footer.json → FooterModel (build grouped Sections)
    // =====================================================================
    public async Task<FooterModel?> GetFooterModelAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = await GetAsync<JsonDocument>("nav.footer", ct);
            if (doc is null) return null;

            var root = doc.RootElement;

            var sections = new List<FooterSection>();
            foreach (var col in Arr(root, "columns"))
            {
                var title = Str(col, "title") ?? "";
                var links = Arr(col, "links")
                    .Select(l => new LinkItem(
                        Text: Str(l, "label") ?? "",
                        Href: Str(l, "href")  ?? "#"
                    ))
                    .ToList();

                sections.Add(new FooterSection(title, links));
            }

            return new FooterModel(
                Copyright: Str(root, "copyright") ?? "© 2025 BioMaint",
                Sections: sections
            );
        }
        catch
        {
            return null;
        }
    }

    // =====================================================================
    // landing.json → LandingModel (maps only to existing records; guarded)
    // Also populates Header via the new header parser.
    // =====================================================================
    public async Task<LandingModel?> GetLandingModelAsync(CancellationToken ct = default)
    {
        JsonDocument? doc = null;
        try
        {
            doc = await GetAsync<JsonDocument>("landing", ct);
            if (doc is null) return null;

            var root = doc.RootElement;

            // --- HEADER (new shape preferred) ---
            HeaderConfig? header = null;
            try
            {
                if (root.TryGetProperty("header", out var hdr) && hdr.ValueKind == JsonValueKind.Object)
                    header = ParseNewHeader(hdr);
            }
            catch { /* ignore header block errors */ }

            // --- HERO ---
            string heroTitle = "";
            string? heroSub = null, pLabel = null, pHref = null, sLabel = null, sHref = null;

            try
            {
                var heroOpt = Obj(root, "hero");
                if (heroOpt is JsonElement hero)
                {
                    heroTitle = Str(hero, "title") ?? "";
                    heroSub   = Str(hero, "sub");

                    var p = Obj(hero, "primaryCta");
                    if (p is JsonElement pe) { pLabel = Str(pe, "label"); pHref = Str(pe, "href"); }

                    var s = Obj(hero, "secondaryCta");
                    if (s is JsonElement secEl) { sLabel = Str(secEl, "label"); sHref = Str(secEl, "href"); }
                }
            }
            catch { /* ignore hero errors */ }

            var heroModel = new HeroModel(
                Eyebrow: null,
                Title: heroTitle,
                Subtitle: heroSub,
                CtaText: pLabel,
                CtaHref: pHref,
                Image: null,
                ImageAlt: null,
                SecondaryCtaText: sLabel,
                SecondaryCtaHref: sHref
            );

            // --- FEATURES ---
            var featuresList = new List<Feature>();
            try
            {
                var featuresOpt = Obj(root, "features");
                if (featuresOpt is JsonElement features)
                {
                    foreach (var c in Arr(features, "cards"))
                    {
                        featuresList.Add(new Feature(
                            Title: Str(c, "title") ?? "",
                            Subtitle: Str(c, "desc"),
                            Icon: Str(c, "icon"),
                            Link: null
                        ));
                    }
                }
            }
            catch { /* ignore */ }

            // --- MOBILE / CROSS-PLATFORM ---
            string crossTitle = "", crossSub = "";
            var essentials = new List<Essential>();
            try
            {
                var mobileOpt = Obj(root, "mobile");
                if (mobileOpt is JsonElement mob)
                {
                    crossTitle = Str(mob, "title") ?? "";
                    crossSub   = Str(mob, "sub") ?? "";

                    foreach (var m in Arr(mob, "micro"))
                    {
                        essentials.Add(new Essential(
                            Title: Str(m, "title") ?? "",
                            Subtitle: Str(m, "sub") ?? "",
                            Icon: Str(m, "icon") ?? ""
                        ));
                    }
                }
            }
            catch { /* ignore */ }

            var cross = new CrossPlatformModel(
                Title: crossTitle,
                Subtitle: crossSub,
                DesktopImage: "",
                MobileImage: ""
            );

            // --- ANALYTICS ---
            string analyticsTitle = "", analyticsSub = "";
            try
            {
                var analyticsOpt = Obj(root, "analytics");
                if (analyticsOpt is JsonElement an)
                {
                    analyticsTitle = Str(an, "title") ?? "";
                    analyticsSub   = Str(an, "sub") ?? "";
                }
            }
            catch { /* ignore */ }

            var analytics = new AnalyticsModel(analyticsTitle, analyticsSub);

            // --- SUPPORT (reuse finalCta copy if present) ---
            string supportTitle = "", supportSub = "";
            JsonElement? finalCtaOpt = null;
            try
            {
                finalCtaOpt = Obj(root, "finalCta");
                if (finalCtaOpt is JsonElement fce)
                {
                    supportTitle = Str(fce, "title") ?? "";
                    supportSub   = Str(fce, "sub") ?? "";
                }
            }
            catch { /* ignore */ }

            var support = new SupportModel(supportTitle, supportSub);

            // --- PRICING placeholder ---
            var pricing = new List<PricingPlan>();

            // --- FOOTER placeholder (Footer loads separately) ---
            var footer = new FooterModel(
                "",
                new List<FooterSection>() // EMPTY sections placeholder
            );

            // --- KPIs ---
            var kpis = new List<Kpi>();
            try
            {
                var kpisOpt = Obj(root, "kpis");
                if (kpisOpt is JsonElement kroot)
                {
                    foreach (var k in Arr(kroot, "items"))
                    {
                        kpis.Add(new Kpi(
                            Value: Str(k, "value") ?? "",
                            Text:  Str(k, "text")  ?? "",
                            Style: Str(k, "style")
                        ));
                    }
                }
            }
            catch { /* ignore */ }

            // --- TESTIMONIALS ---
            Testimonials? testimonials = null;
            try
            {
                var testiOpt = Obj(root, "testimonials");
                if (testiOpt is JsonElement te)
                {
                    var items = new List<Testimonial>();
                    foreach (var t in Arr(te, "items"))
                    {
                        items.Add(new Testimonial(
                            Name:  Str(t, "name")  ?? "",
                            Role:  Str(t, "role")  ?? "",
                            Img:   Str(t, "img")   ?? "",
                            Quote: Str(t, "quote") ?? ""
                        ));
                    }

                    testimonials = new Testimonials(
                        TitleHtml: Str(te, "titleHtml") ?? "",
                        Subtitle:  Str(te, "sub") ?? "",
                        Items: items
                    );
                }
            }
            catch { /* ignore */ }

            // --- SOLUTIONS ---
            Solutions? solutions = null;
            try
            {
                var solOpt = Obj(root, "solutions");
                if (solOpt is JsonElement solRoot)
                {
                    var tabs = new List<SolutionTab>();
                    foreach (var tab in Arr(solRoot, "tabs"))
                    {
                        var cards = new List<Feature>();
                        foreach (var c in Arr(tab, "cards"))
                        {
                            cards.Add(new Feature(
                                Title: Str(c, "title") ?? "",
                                Subtitle: Str(c, "desc"),
                                Icon: null,
                                Link: null
                            ));
                        }

                        tabs.Add(new SolutionTab(
                            Id:    Str(tab, "id")    ?? "",
                            Label: Str(tab, "label") ?? "",
                            Cards: cards
                        ));
                    }

                    solutions = new Solutions(Str(solRoot, "title") ?? "", tabs);
                }
            }
            catch { /* ignore */ }

            // --- FINAL CTA ---
            FinalCta? finalCta = null;
            try
            {
                if (finalCtaOpt is JsonElement fc)
                {
                    var prim = Obj(fc, "primary");
                    var sec  = Obj(fc, "secondary");

                    finalCta = new FinalCta(
                        Title:        Str(fc, "title") ?? "",
                        Subtitle:     Str(fc, "sub") ?? "",
                        PrimaryText:  prim is JsonElement pe ? Str(pe, "label") ?? "" : "",
                        PrimaryHref:  prim is JsonElement pe2 ? Str(pe2, "href") ?? "#" : "#",
                        SecondaryText: sec is JsonElement se2 ? Str(se2, "label") ?? "" : "",
                        SecondaryHref: sec is JsonElement se3 ? Str(se3, "href") ?? "#" : "#"
                    );
                }
            }
            catch { /* ignore */ }

            // Fallback header if the block was missing/invalid
            header ??= await GetHeaderConfigAsync(ct);

            // --- Assemble ---
            return new LandingModel(
                Header: header,
                Hero: heroModel,
                Trust: new TrustModel("", new List<Badge>()), // (not used by new design)
                Features: featuresList,
                CrossPlatform: cross,
                HealthcareEssentials: essentials,
                Analytics: analytics,
                Support: support,
                Pricing: new List<PricingPlan>(),
                Footer: footer,
                Kpis: kpis,
                Testimonials: testimonials,
                Solutions: solutions,
                FinalCta: finalCta
            );
        }
        catch
        {
            return null;
        }
        finally
        {
            doc?.Dispose();
        }
    }
}
