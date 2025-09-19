// Services/ApiClient.cs
namespace WebApp.Services;
using System.Net.Http.Json;

public sealed class ApiClient
{
    private readonly HttpClient _http;
    public ApiClient(HttpClient http) => _http = http;

    public Task<T?> GetAsync<T>(string url, CancellationToken ct = default) =>
        _http.GetFromJsonAsync<T>(url, ct);

    public async Task<HttpResponseMessage> PostAsync<T>(string url, T body, CancellationToken ct = default) =>
        await _http.PostAsJsonAsync(url, body, ct);
}
