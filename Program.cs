using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Http.Resilience;
using WebApp;
using WebApp.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Logging
builder.Logging.SetMinimumLevel(
    builder.HostEnvironment.IsDevelopment() ? LogLevel.Information : LogLevel.Warning);

// JSON options
builder.Services.AddSingleton(new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = false
});

// Static HttpClient
builder.Services.AddHttpClient("static", client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("static"));

// API HttpClient
var apiBase = builder.Configuration["ApiBaseUrl"] ?? "https://biomaint.com/";
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBase);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
})
.AddHttpMessageHandler(sp => new AuthMessageHandler(sp.GetRequiredService<AuthService>()))
.AddStandardResilienceHandler();

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<TelemetryService>();
builder.Services.AddSingleton<FeatureFlags>();

await builder.Build().RunAsync();

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
