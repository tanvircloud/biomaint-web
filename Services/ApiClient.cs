using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebApp.Services
{
    public sealed class ApiClient
    {
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json;

        public event Action? OnTokenExpired;
        public event Action? OnUnauthorized;

        public ApiClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));

            _json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
        }

        public void SetBearerToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentNullException(nameof(token));

            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        public void ClearBearerToken()
        {
            _http.DefaultRequestHeaders.Authorization = null;
        }

        // ---------- Generic JSON (any shape) ----------
        public async Task<JsonElement> GetJsonAsync(
            string endpoint,
            Dictionary<string, string>? query = null,
            CancellationToken ct = default)
        {
            var uri = BuildUri(endpoint, query);
            using var resp = await _http.GetAsync(uri, ct);
            await HandleAuthAsync(resp);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.Clone();
        }

        public async Task<JsonElement> PostJsonAsync<TReq>(
            string endpoint,
            TReq body,
            CancellationToken ct = default)
        {
            using var content = Serialize(body);
            using var resp = await _http.PostAsync(endpoint, content, ct);
            await HandleAuthAsync(resp);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement.Clone();
        }

        // ---------- Typed helpers ----------
        public async Task<T?> GetAsync<T>(
            string endpoint,
            Dictionary<string, string>? query = null,
            CancellationToken ct = default,
            int retries = 1)
        {
            var uri = BuildUri(endpoint, query);
            for (var attempt = 0; ; attempt++)
            {
                using var resp = await _http.GetAsync(uri, ct);
                await HandleAuthAsync(resp);

                if (resp.IsSuccessStatusCode)
                    return await DeserializeAsync<T>(resp, ct);

                if (IsTransient(resp.StatusCode) && attempt < retries)
                {
                    await Task.Delay(GetBackoffDelay(attempt), ct);
                    continue;
                }

                throw await ApiHttpException.FromResponseAsync(resp, ct);
            }
        }

        public async Task<PagedResult<T>> GetPagedAsync<T>(
            string endpoint,
            Dictionary<string, string>? query = null,
            CancellationToken ct = default)
        {
            var uri = BuildUri(endpoint, query);
            using var resp = await _http.GetAsync(uri, ct);
            await HandleAuthAsync(resp);

            if (!resp.IsSuccessStatusCode)
                throw await ApiHttpException.FromResponseAsync(resp, ct);

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            // Case 1: root is array
            if (root.ValueKind == JsonValueKind.Array)
            {
                var list = JsonSerializer.Deserialize<List<T>>(root.GetRawText(), _json) ?? new List<T>();
                return new PagedResult<T>(list, list.Count);
            }

            // Case 2: root is object -> dynamically find the "best" array and a nearby total
            if (root.ValueKind == JsonValueKind.Object)
            {
                var array = FindBestArray(root, out var owner);
                if (array.ValueKind != JsonValueKind.Undefined)
                {
                    var list = JsonSerializer.Deserialize<List<T>>(array.GetRawText(), _json) ?? new List<T>();

                    // Prefer a numeric sibling in the same object that is >= list.Count (smallest qualifying value)
                    var total = TryFindNearestTotal(owner, list.Count);
                    if (total is null)
                    {
                        // fallback: search the whole root for a qualifying numeric
                        total = TryFindNearestTotal(root, list.Count);
                    }

                    return new PagedResult<T>(list, total ?? list.Count);
                }
            }

            throw new ApiHttpException("No array found in JSON response.", HttpStatusCode.OK);
        }

        public async Task<TRes?> PostAsync<TReq, TRes>(
            string endpoint,
            TReq body,
            CancellationToken ct = default)
        {
            using var content = Serialize(body);
            using var resp = await _http.PostAsync(endpoint, content, ct);
            await HandleAuthAsync(resp);

            if (!resp.IsSuccessStatusCode)
                throw await ApiHttpException.FromResponseAsync(resp, ct);

            return await DeserializeAsync<TRes>(resp, ct);
        }

        public async Task<TRes?> PutAsync<TReq, TRes>(
            string endpoint,
            TReq body,
            CancellationToken ct = default)
        {
            using var content = Serialize(body);
            using var resp = await _http.PutAsync(endpoint, content, ct);
            await HandleAuthAsync(resp);

            if (!resp.IsSuccessStatusCode)
                throw await ApiHttpException.FromResponseAsync(resp, ct);

            return await DeserializeAsync<TRes>(resp, ct);
        }

        public async Task DeleteAsync(
            string endpoint,
            Dictionary<string, string>? query = null,
            CancellationToken ct = default)
        {
            var uri = BuildUri(endpoint, query);
            using var resp = await _http.DeleteAsync(uri, ct);
            await HandleAuthAsync(resp);

            if (!resp.IsSuccessStatusCode)
                throw await ApiHttpException.FromResponseAsync(resp, ct);
        }

        public async Task<HttpResponseMessage> PostRawAsync(
            string endpoint,
            HttpContent content,
            CancellationToken ct = default)
        {
            var resp = await _http.PostAsync(endpoint, content, ct);
            await HandleAuthAsync(resp);
            return resp;
        }

        // ---------- Internals ----------
        private async Task HandleAuthAsync(HttpResponseMessage resp)
        {
            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (resp.Headers.Contains("Token-Expired"))
                    OnTokenExpired?.Invoke();
                else
                    OnUnauthorized?.Invoke();

                await Task.CompletedTask;
            }
        }

        private static bool IsTransient(HttpStatusCode code)
        {
            return code == HttpStatusCode.RequestTimeout ||
                   (int)code == 429 ||
                   (int)code >= 500;
        }

        private static TimeSpan GetBackoffDelay(int attempt)
        {
            var ms = (int)Math.Min(2000, 250 * Math.Pow(2, attempt));
            return TimeSpan.FromMilliseconds(ms);
        }

        private StringContent Serialize<T>(T value)
        {
            var json = JsonSerializer.Serialize(value, _json);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        private async Task<T?> DeserializeAsync<T>(HttpResponseMessage resp, CancellationToken ct)
        {
            if (resp.Content == null)
                return default;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            if (resp.Content.Headers.ContentLength.HasValue &&
                resp.Content.Headers.ContentLength.Value == 0)
                return default;

            if (typeof(T) == typeof(string))
            {
                var s = await resp.Content.ReadAsStringAsync(ct);
                return (T)(object)s;
            }

            if (typeof(T) == typeof(JsonElement))
            {
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var clone = doc.RootElement.Clone();
                return (T)(object)clone;
            }

            return await JsonSerializer.DeserializeAsync<T>(stream, _json, ct);
        }

        private Uri BuildUri(string endpoint, Dictionary<string, string>? query)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentNullException(nameof(endpoint));

            Uri baseUri = _http.BaseAddress ?? new Uri("/", UriKind.Relative);
            var uri = new Uri(baseUri, endpoint);

            if (query is null || query.Count == 0)
                return uri;

            var sb = new StringBuilder();
            foreach (var kv in query)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key))
                  .Append('=')
                  .Append(Uri.EscapeDataString(kv.Value));
            }

            var ub = new UriBuilder(uri) { Query = sb.ToString() };
            return ub.Uri;
        }

        // ---------- Dynamic discovery helpers ----------
        // Picks the "best" array in the object graph: prefer larger arrays; tie-break by shallower depth.
        private static JsonElement FindBestArray(JsonElement root, out JsonElement owner)
        {
            owner = default;
            var best = default(JsonElement);
            var bestLen = -1;
            var bestDepth = int.MaxValue;

            var stack = new Stack<(JsonElement node, JsonElement parent, int depth)>();
            stack.Push((root, default, 0));

            while (stack.Count > 0)
            {
                var (node, parent, depth) = stack.Pop();

                if (node.ValueKind == JsonValueKind.Array)
                {
                    var len = node.GetArrayLength();
                    if (len > bestLen || (len == bestLen && depth < bestDepth))
                    {
                        best = node;
                        owner = parent;
                        bestLen = len;
                        bestDepth = depth;
                    }

                    // Explore array items (objects) to find nested arrays too
                    foreach (var item in node.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                            stack.Push((item, parent, depth + 1));
                    }
                }
                else if (node.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in node.EnumerateObject())
                    {
                        var v = prop.Value;
                        stack.Push((v, node, depth + 1));
                    }
                }
            }

            return best;
        }

        // Looks for a numeric sibling (in the same object) that is >= arrayLen; picks the smallest qualifying value.
        // If 'container' isn't an object or has none, returns null.
        private static int? TryFindNearestTotal(JsonElement container, int arrayLen)
        {
            if (container.ValueKind != JsonValueKind.Object) return null;

            int? best = null;

            foreach (var prop in container.EnumerateObject())
            {
                var v = prop.Value;
                if (v.ValueKind == JsonValueKind.Number)
                {
                    if (v.TryGetInt64(out var l))
                    {
                        if (l >= arrayLen)
                        {
                            var n = l > int.MaxValue ? int.MaxValue : (int)l;
                            if (best is null || n < best.Value) best = n;
                        }
                    }
                    else if (v.TryGetDouble(out var d))
                    {
                        var n = (int)Math.Round(d);
                        if (n >= arrayLen)
                        {
                            if (best is null || n < best.Value) best = n;
                        }
                    }
                }
            }

            return best;
        }
    }

    public sealed class ApiHttpException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public string? ResponseBody { get; }

        public ApiHttpException(string message, HttpStatusCode statusCode, string? body = null, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ResponseBody = body;
        }

        public static async Task<ApiHttpException> FromResponseAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            string? body = null;
            try
            {
                body = await resp.Content.ReadAsStringAsync(ct);
                var message = TryExtractMessage(body) ?? resp.ReasonPhrase ?? "HTTP error";
                return new ApiHttpException(message, resp.StatusCode, body);
            }
            catch
            {
                return new ApiHttpException(resp.ReasonPhrase ?? "HTTP error", resp.StatusCode);
            }
        }

        private static string? TryExtractMessage(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                        return m.GetString();
                    if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                        return e.GetString();
                    if (root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                        return t.GetString();
                    if (root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String)
                        return d.GetString();
                }
            }
            catch { }
            return null;
        }
    }

    public readonly record struct PagedResult<T>(IReadOnlyList<T> Data, int TotalCount);
}
