using System.Net.Http.Json;
using System.Text.Json;

namespace WebApp.Services;

public sealed class ContentService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ContentService(HttpClient http) => _http = http;

    // Safe loader: never throws on 404/invalid JSON — returns default instead
    public async Task<T?> GetAsync<T>(string name, CancellationToken ct = default)
    {
        try
        {
            var url = $"content/{name}.json";
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) return default;

            // Read/deserialize explicitly to avoid GetFromJsonAsync throwing on edge cases
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOpts, ct);
        }
        catch
        {
            return default;
        }
    }
}
