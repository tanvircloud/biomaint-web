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

    // ------------------------------------------------------------
    // Base loader
    // ------------------------------------------------------------
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

    // ------------------------------------------------------------
    // Case-insensitive JSON helpers
    // ------------------------------------------------------------
    private static bool TryProp(JsonElement e, string name, out JsonElement v)
    {
        foreach (var p in e.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            { v = p.Value; return true; }
        }
        v = default; return false;
    }

    private static string? Str(JsonElement e, string name)
        => TryProp(e, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonElement? Obj(JsonElement e, string name)
        => TryProp(e, name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static IEnumerable<JsonElement> Arr(JsonElement e, string name)
        => TryProp(e, name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static bool Bool(JsonElement e, string name)
        => TryProp(e, name, out var v) && v.ValueKind == JsonValueKind.True;

    // ------------------------------------------------------------
    // Header parsing (new + legacy) → HeaderConfig
    // ------------------------------------------------------------
    private static HeaderConfig ParseNewHeader(JsonElement maybeHeader)
    {
        // Accept {brand,...} or {header:{brand,...}}
        if (TryProp(maybeHeader, "header", out var inner) && inner.ValueKind == JsonValueKind.Object)
            maybeHeader = inner;

        // brand
        var b = Obj(maybeHeader, "brand");
        var brand = new BrandLink(
            Text: b is JsonElement be ? (Str(be, "text") ?? Str(be, "label") ?? "BioMaint") : "BioMaint",
            Href: b is JsonElement be2 ? (Str(be2, "href") ?? "/") : "/"
        );

        // groups
        var groups = new List<NavGroup>();
        IEnumerable<JsonElement> groupSrc = Arr(maybeHeader, "groups");
        if (!groupSrc.Any()) groupSrc = Arr(maybeHeader, "menus"); // legacy alias

        foreach (var g in groupSrc)
        {
            var id    = Str(g, "id");
            var label = Str(g, "label") ?? "";

            var items = Arr(g, "items")
                .Select(i => new NavChild(
                    Label: Str(i, "label") ?? "",
                    Href : Str(i, "href")  ?? "#",
                    External: Bool(i, "external")
                ))
                .ToList();

            if (items.Count > 0)
            {
                groups.Add(new NavGroup(id, label, items, null));     // dropdown
            }
            else
            {
                groups.Add(new NavGroup(id, label, null, Str(g, "href") ?? "#")); // single link
            }
        }

        // auth
        AuthConfig? auth = null;
        var ao = Obj(maybeHeader, "auth");
        if (ao is JsonElement ae)
            auth = new AuthConfig(Str(ae, "loginText") ?? "Log in", Str(ae, "loginHref") ?? "/auth/login");

        // cta
        CtaConfig? cta = null;
        var co = Obj(maybeHeader, "cta");
        if (co is JsonElement ce)
            cta = new CtaConfig(Str(ce, "text") ?? "Try for Free", Str(ce, "href") ?? "#demo");

        return new HeaderConfig(brand, groups, auth, cta);
    }

    private static HeaderConfig MapLegacyHeaderToNew(JsonElement legacyRoot)
    {
        // legacy: brand:"BioMaint", menus:[{label,items:[...]}], pricingHref, loginHref, cta:{label,href}
        var brand = new BrandLink(Str(legacyRoot, "brand") ?? "BioMaint", "/");

        var groups = new List<NavGroup>();
        foreach (var menu in Arr(legacyRoot, "menus"))
        {
            var label = Str(menu, "label") ?? "";
            var items = Arr(menu, "items")
                .Select(i => new NavChild(Str(i, "label") ?? "", Str(i, "href") ?? "#"))
                .ToList();

            groups.Add(new NavGroup(null, label, items, null));
        }

        var pricing = Str(legacyRoot, "pricingHref");
        if (!string.IsNullOrWhiteSpace(pricing))
            groups.Add(new NavGroup("pricing", "Pricing", null, pricing));

        AuthConfig? auth = null;
        var login = Str(legacyRoot, "loginHref");
        if (!string.IsNullOrWhiteSpace(login))
            auth = new AuthConfig("Log in", login);

        CtaConfig? cta = null;
        var c = Obj(legacyRoot, "cta");
        if (c is JsonElement ce)
            cta = new CtaConfig(Str(ce, "label") ?? "Try for Free", Str(ce, "href") ?? "#demo");

        return new HeaderConfig(brand, groups, auth, cta);
    }

    /// <summary>
    /// Loads header from:
    /// 1) nav.header.json  (primary)
    /// 2) header.json      (optional)
    /// 3) landing.json.header (fallback)
    /// 4) a minimal static fallback (never empty)
    /// </summary>
    public async Task<HeaderConfig> GetHeaderConfigAsync(CancellationToken ct = default)
    {
        // 1) primary: nav.header.json
        try
        {
            using var navDoc = await GetAsync<JsonDocument>("nav.header", ct);
            if (navDoc is not null) return ParseNewHeader(navDoc.RootElement);
        }
        catch { /* ignore */ }

        // 2) header.json
        try
        {
            using var headerDoc = await GetAsync<JsonDocument>("header", ct);
            if (headerDoc is not null) return ParseNewHeader(headerDoc.RootElement);
        }
        catch { /* ignore */ }

        // 3) landing.json.header
        try
        {
            using var landing = await GetAsync<JsonDocument>("landing", ct);
            if (landing is not null)
            {
                var root = landing.RootElement;
                if (TryProp(root, "header", out var hdr) && hdr.ValueKind == JsonValueKind.Object)
                    return ParseNewHeader(hdr);
            }
        }
        catch { /* ignore */ }

        // 4) minimal fallback
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

    // ------------------------------------------------------------
    // Footer: nav.footer.json → FooterModel
    // ------------------------------------------------------------
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
                    .Select(l => new LinkItem(Str(l, "label") ?? "", Str(l, "href") ?? "#"))
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

    // ------------------------------------------------------------
    // landing.json → LandingModel (kept as you had it; header filled here too)
    // ------------------------------------------------------------
    public async Task<LandingModel?> GetLandingModelAsync(CancellationToken ct = default)
    {
        JsonDocument? doc = null;
        try
        {
            doc = await GetAsync<JsonDocument>("landing", ct);
            if (doc is null) return null;

            var root = doc.RootElement;

            // Header (prefer embedded; else loader)
            HeaderConfig? header = null;
            try
            {
                if (TryProp(root, "header", out var hdr) && hdr.ValueKind == JsonValueKind.Object)
                    header = ParseNewHeader(hdr);
            }
            catch { /* ignore */ }

            // HERO
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
                    if (s is JsonElement se) { sLabel = Str(se, "label"); sHref = Str(se, "href"); }
                }
            }
            catch { /* ignore */ }

            var heroModel = new HeroModel(
                Eyebrow: null, Title: heroTitle, Subtitle: heroSub,
                CtaText: pLabel, CtaHref: pHref, Image: null, ImageAlt: null,
                SecondaryCtaText: sLabel, SecondaryCtaHref: sHref
            );

            // FEATURES
            var featuresList = new List<Feature>();
            try
            {
                var featuresOpt = Obj(root, "features");
                if (featuresOpt is JsonElement features)
                {
                    foreach (var c in Arr(features, "cards"))
                        featuresList.Add(new Feature(Str(c, "title") ?? "", Str(c, "desc"), Str(c, "icon")));
                }
            }
            catch { }

            // MOBILE / CROSS
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
                        essentials.Add(new Essential(Str(m, "title") ?? "", Str(m, "sub") ?? "", Str(m, "icon") ?? ""));
                }
            }
            catch { }

            var cross = new CrossPlatformModel(crossTitle, crossSub, "", "");

            // ANALYTICS
            string analyticsTitle = "", analyticsSub = "";
            try
            {
                var a = Obj(root, "analytics");
                if (a is JsonElement an) { analyticsTitle = Str(an, "title") ?? ""; analyticsSub = Str(an, "sub") ?? ""; }
            }
            catch { }
            var analytics = new AnalyticsModel(analyticsTitle, analyticsSub);

            // SUPPORT from finalCta text if present
            string supportTitle = "", supportSub = "";
            JsonElement? finalCtaOpt = null;
            try
            {
                finalCtaOpt = Obj(root, "finalCta");
                if (finalCtaOpt is JsonElement fce) { supportTitle = Str(fce, "title") ?? ""; supportSub = Str(fce, "sub") ?? ""; }
            }
            catch { }
            var support = new SupportModel(supportTitle, supportSub);

            // FOOTER placeholder (real footer loaded separately)
            var footer = new FooterModel("", new List<FooterSection>());

            // KPIs
            var kpis = new List<Kpi>();
            try
            {
                var kroot = Obj(root, "kpis");
                if (kroot is JsonElement ke)
                {
                    foreach (var k in Arr(ke, "items"))
                        kpis.Add(new Kpi(Str(k, "value") ?? "", Str(k, "text") ?? "", Str(k, "style")));
                }
            }
            catch { }

            // TESTIMONIALS
            Testimonials? testimonials = null;
            try
            {
                var te = Obj(root, "testimonials");
                if (te is JsonElement t)
                {
                    var items = new List<Testimonial>();
                    foreach (var x in Arr(t, "items"))
                        items.Add(new Testimonial(Str(x, "name") ?? "", Str(x, "role") ?? "", Str(x, "img") ?? "", Str(x, "quote") ?? ""));
                    testimonials = new Testimonials(Str(t, "titleHtml") ?? "", Str(t, "sub") ?? "", items);
                }
            }
            catch { }

            // SOLUTIONS
            Solutions? solutions = null;
            try
            {
                var solRoot = Obj(root, "solutions");
                if (solRoot is JsonElement s)
                {
                    var tabs = new List<SolutionTab>();
                    foreach (var tab in Arr(s, "tabs"))
                    {
                        var cards = new List<Feature>();
                        foreach (var c in Arr(tab, "cards"))
                            cards.Add(new Feature(Str(c, "title") ?? "", Str(c, "desc"), null));
                        tabs.Add(new SolutionTab(Str(tab, "id") ?? "", Str(tab, "label") ?? "", cards));
                    }
                    solutions = new Solutions(Str(s, "title") ?? "", tabs);
                }
            }
            catch { }

            // FINAL CTA
            FinalCta? finalCta = null;
            try
            {
                if (finalCtaOpt is JsonElement fc)
                {
                    var prim = Obj(fc, "primary");
                    var sec  = Obj(fc, "secondary");
                    finalCta = new FinalCta(
                        Title: Str(fc, "title") ?? "",
                        Subtitle: Str(fc, "sub") ?? "",
                        PrimaryText:  prim is JsonElement pe ? Str(pe, "label") ?? "" : "",
                        PrimaryHref:  prim is JsonElement pe2 ? Str(pe2, "href") ?? "#" : "#",
                        SecondaryText: sec is JsonElement se2 ? Str(se2, "label") ?? "" : "",
                        SecondaryHref: sec is JsonElement se3 ? Str(se3, "href") ?? "#" : "#"
                    );
                }
            }
            catch { }

            // If header block missing/invalid, load via header pipeline
            header ??= await GetHeaderConfigAsync(ct);

            return new LandingModel(
                Header: header,
                Hero: heroModel,
                Trust: new TrustModel("", new List<Badge>()),
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
