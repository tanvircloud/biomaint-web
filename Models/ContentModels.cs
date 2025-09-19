namespace WebApp.Models
{
    // -------------------------------
    // Navigation
    // -------------------------------
    public record NavItem(
        string Title,
        string? Href = null,
        bool External = false,
        List<NavItem>? Children = null     // ✅ allow nested items for dropdowns
    );

    public record NavHeader(
        string Brand,
        string Logo,
        List<NavItem> Links,
        string? Href = "/",                // Brand link (default → home)
        string? Tagline = null             // Optional tagline under logo
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
        string? ImageAlt = null,            // accessibility alt text
        string? SecondaryCtaText = null,    // optional secondary button text
        string? SecondaryCtaHref = null     // optional secondary button link
    );

    // -------------------------------
    // Features
    // -------------------------------
    public record Feature(
        string Title,
        string? Subtitle,
        string? Icon,
        string? Link = null                // Optional → "Learn more"
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

    public record FooterModel(
        string Copyright,
        List<LinkItem> Links
    );

    // -------------------------------
    // Landing Page Model (all sections)
    // -------------------------------
    public record LandingModel(
        NavHeader? Header,                       // optional nav (can come from JSON too)
        HeroModel Hero,
        TrustModel Trust,
        List<Feature> Features,
        CrossPlatformModel CrossPlatform,
        List<Essential> HealthcareEssentials,
        AnalyticsModel Analytics,
        SupportModel Support,
        List<PricingPlan> Pricing,
        FooterModel Footer
    );
}
