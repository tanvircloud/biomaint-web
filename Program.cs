using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Http.Resilience; // requires Microsoft.Extensions.Http.Resilience
using WebApp;
using WebApp.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ---- Logging
builder.Logging.SetMinimumLevel(
    builder.HostEnvironment.IsDevelopment() ? LogLevel.Information : LogLevel.Warning);

// ---- Shared JSON options (if you inject JsonSerializerOptions anywhere)
builder.Services.AddSingleton(new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = false
});

// ---- Static HttpClient for files under wwwroot (content/*.json, css/js, etc.)
builder.Services.AddHttpClient("static", client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

// Make that the default HttpClient so services taking HttpClient get the "static" one
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("static"));

// ---- API HttpClient (optional, for your backend)
var apiBase = builder.Configuration["ApiBaseUrl"] ?? "https://biomaint.com/";
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBase);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
})
.AddHttpMessageHandler(sp => new AuthMessageHandler(sp.GetRequiredService<AuthService>()))
.AddStandardResilienceHandler(); // retry/timeout circuit defaults

// ---- App services
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<ContentService>();   // uses default HttpClient => "static"
builder.Services.AddScoped<TelemetryService>();
builder.Services.AddSingleton<FeatureFlags>();

await builder.Build().RunAsync();

// ===== Auth handler for the API client =====
public sealed class AuthMessageHandler : DelegatingHandler
{
    private readonly AuthService _auth;
    public AuthMessageHandler(AuthService auth) => _auth = auth;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _auth.GetAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, ct);
    }
}
