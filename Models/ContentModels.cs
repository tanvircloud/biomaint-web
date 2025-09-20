using System.Collections.Generic;

namespace WebApp.Models
{
    // ---------------------------------------------------------------------
    // (LEGACY) Navigation used by older header implementations
    // ---------------------------------------------------------------------
    public record NavItem(
        string Title,
        string? Href = null,
        bool External = false,
        List<NavItem>? Children = null
    );

    public record NavHeader(
        string Brand,
        string Logo,
        List<NavItem> Links,
        string? Href = "/",
        string? Tagline = null
    );

    // ---------------------------------------------------------------------
    // NEW HEADER (webApp.html) — matches your JSON exactly
    // {
    //   "brand": { "text": "...", "href": "/" },
    //   "groups": [
    //     { "id":"product", "label":"Product", "items":[ { "label":"...", "href":"..."} ] },
    //     { "id":"pricing", "label":"Pricing", "href":"/pricing" }
    //   ],
    //   "auth": { "loginText":"Log in", "loginHref":"/auth/login" },
    //   "cta":  { "text":"Try for Free", "href":"#demo" }
    // }
    // ---------------------------------------------------------------------
    public record BrandLink(string? Text, string? Href);

    public record NavChild( // child item inside a dropdown
        string Label,
        string? Href = null,
        bool External = false
    );

    public record NavGroup( // either a dropdown (Items != null) or a direct link (Href != null)
        string? Id,
        string Label,
        List<NavChild>? Items = null,
        string? Href = null
    );

    public record AuthConfig(string? LoginText, string? LoginHref);
    public record CtaConfig(string? Text, string? Href);

    public record HeaderConfig(
        BrandLink? Brand,
        List<NavGroup>? Groups,
        AuthConfig? Auth,
        CtaConfig? Cta
    );

    // -------------------------------
    // Hero section
    // -------------------------------
    public record HeroModel(
        string? Eyebrow,
        string Title,
        string? Subtitle,
        string? CtaText,
        string? CtaHref,
        string? Image,
        string? ImageAlt = null,
        string? SecondaryCtaText = null,
        string? SecondaryCtaHref = null
    );

    // -------------------------------
    // Features
    // -------------------------------
    public record Feature(
        string Title,
        string? Subtitle,
        string? Icon,
        string? Link = null
    );

    // -------------------------------
    // Trust section
    // -------------------------------
    public record Badge(
        string Icon,
        string Label
    );

    public record TrustModel(
        string Headline,
        List<Badge> Badges
    );

    // -------------------------------
    // Cross-platform
    // -------------------------------
    public record CrossPlatformModel(
        string Title,
        string Subtitle,
        string DesktopImage,
        string MobileImage
    );

    // -------------------------------
    // Healthcare essentials
    // -------------------------------
    public record Essential(
        string Title,
        string Subtitle,
        string Icon
    );

    // -------------------------------
    // Analytics
    // -------------------------------
    public record AnalyticsModel(
        string Title,
        string Subtitle
    );

    // -------------------------------
    // Support
    // -------------------------------
    public record SupportModel(
        string Title,
        string Subtitle
    );

    // -------------------------------
    // Pricing
    // -------------------------------
    public record PricingPlan(
        string Title,
        string Subtitle
    );

    // -------------------------------
    // Footer
    // -------------------------------
    public record LinkItem(
        string Text,
        string Href
    );

    // Grouped footer sections (columns)
    public record FooterSection(
        string Title,
        List<LinkItem> Links
    );

    // App download badge
    public record AppBadge(
        string Alt,
        string Src,
        string Href
    );

    // Social icon link
    public record SocialLink(
        string Icon,
        string Aria,
        string Href
    );

    public record FooterModel(
        string Copyright,
        List<FooterSection> Sections,
        List<AppBadge>? AppBadges = null,
        List<SocialLink>? SocialLinks = null,
        List<LinkItem>? Legal = null,
        string AppHeading = "Get the app",
        string FollowHeading = "Follow"
    );

    // =====================================================================
    // 🔹 NEW SECTIONS (from your newer landing layout)
    // =====================================================================

    // KPIs band
    public record Kpi(
        string Value,
        string Text,
        string? Style = null
    );

    // Testimonials
    public record Testimonial(
        string Name,
        string Role,
        string Img,
        string Quote
    );

    public record Testimonials(
        string TitleHtml,
        string Subtitle,
        List<Testimonial> Items
    );

    // Solutions (tabbed)
    public record SolutionTab(
        string Id,
        string Label,
        List<Feature> Cards
    );

    public record Solutions(
        string Title,
        List<SolutionTab> Tabs
    );

    // Final CTA
    public record FinalCta(
        string Title,
        string Subtitle,
        string PrimaryText,
        string PrimaryHref,
        string SecondaryText,
        string SecondaryHref
    );

    // -------------------------------
    // Landing Page Model (all sections)
    // NOTE: Header switched to the NEW HeaderConfig type.
    // -------------------------------
    public record LandingModel(
        HeaderConfig? Header,
        HeroModel Hero,
        TrustModel Trust,
        List<Feature> Features,
        CrossPlatformModel CrossPlatform,
        List<Essential> HealthcareEssentials,
        AnalyticsModel Analytics,
        SupportModel Support,
        List<PricingPlan> Pricing,
        FooterModel Footer,

        List<Kpi>? Kpis = null,
        Testimonials? Testimonials = null,
        Solutions? Solutions = null,
        FinalCta? FinalCta = null
    );
}
