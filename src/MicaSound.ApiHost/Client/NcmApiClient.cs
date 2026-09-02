using System.Net;
using System.Text.Json;
using MicaSound.Core.Models;

namespace MicaSound.ApiHost.Client;

/// <summary>
/// 对网易云 API 的轻量 HTTP 桥接。
/// 统一携带登录 Cookie、将 JSON 还原为领域模型。
/// </summary>
public sealed class NcmApiClient
{
    private readonly HttpClient _http;
    private readonly Dictionary<string, string> _cookies = new(StringComparer.OrdinalIgnoreCase);

    public Uri BaseAddress { get; }

    public NcmApiClient(Uri baseAddress)
    {
        BaseAddress = baseAddress;
        _http = new HttpClient(new HttpClientHandler { UseCookies = true })
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(15),
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/122 Safari/537.36");
    }

    /// <summary>设置登录 Cookie(如 MUSIC_U / __csrf)。</summary>
    public void SetCookie(string name, string value)
    {
        lock (_cookies) _cookies[name] = value;
    }

    public async Task<JsonDocument> GetJsonAsync(string path,
        IReadOnlyDictionary<string, string>? query = null,
        CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Get, path, query, null, ct).ConfigureAwait(false);
        return await ParseAsync(resp, ct).ConfigureAwait(false);
    }

    public async Task<JsonDocument> PostJsonAsync(string path,
        IReadOnlyDictionary<string, string>? query = null,
        IReadOnlyDictionary<string, string>? form = null,
        CancellationToken ct = default)
    {
        using var resp = await SendAsync(HttpMethod.Post, path, query, form, ct).ConfigureAwait(false);
        return await ParseAsync(resp, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path,
        IReadOnlyDictionary<string, string>? query, IReadOnlyDictionary<string, string>? form,
        CancellationToken ct)
    {
        var url = new Uri(BaseAddress, BuildRelative(path, query));
        using var req = new HttpRequestMessage(method, url);
        if (form is not null)
            req.Content = new FormUrlEncodedContent(form);

        // 注入登录 Cookie
        string? cookie;
        lock (_cookies)
        {
            cookie = _cookies.Count == 0 ? null
                : string.Join("; ", _cookies.Select(kv => $"{kv.Key}={kv.Value}; Path=/; Domain={BaseAddress.Host}"));
        }
        if (cookie is not null)
            req.Headers.TryAddWithoutValidation("Cookie", cookie);

        return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return JsonDocument.Parse(raw);
    }

    private static string BuildRelative(string path, IReadOnlyDictionary<string, string>? query)
    {
        var q = path.TrimStart('/');
        if (query is { Count: > 0 })
            q += "?" + string.Join("&", query.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return q;
    }

    // ---- 领域方法 ----

    public async Task<IReadOnlyList<SongInfo>> SearchSongsAsync(string keywords, int limit = 30,
        CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("search", new Dictionary<string, string>
        {
            ["keywords"] = keywords, ["type"] = "1", ["limit"] = limit.ToString(),
        }, ct).ConfigureAwait(false);
        return MapSongs(doc);
    }

    public async Task<IReadOnlyList<SongInfo>> SongDetailAsync(IEnumerable<long> ids,
        CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("song/detail",
            new Dictionary<string, string> { ["ids"] = string.Join(",", ids) }, ct).ConfigureAwait(false);
        return MapSongs(doc);
    }

    /// <summary>获取播放地址;未登录或 VIP 受限时返回 null。</summary>
    public async Task<string?> GetSongUrlAsync(long id, int br = 320000,
        CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("song/url",
            new Dictionary<string, string> { ["id"] = id.ToString(), ["br"] = br.ToString() }, ct)
            .ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("data", out var arr) || arr.GetArrayLength() == 0)
            return null;
        var first = arr[0];
        return first.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
            ? u.GetString() : null;
    }

    public async Task<JsonDocument> BannerAsync(CancellationToken ct = default) =>
        await GetJsonAsync("banner", new Dictionary<string, string> { ["type"] = "2" }, ct).ConfigureAwait(false);

    public async Task<string?> CreateQrKeyAsync(CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync("login/qr/key", ct: ct).ConfigureAwait(false);
        var d = doc.RootElement.GetProperty("data");
        return d.TryGetProperty("unikey", out var k) ? k.GetString() : null;
    }

    private static IReadOnlyList<SongInfo> MapSongs(JsonDocument doc)
    {
        var result = new List<SongInfo>();
        var root = doc.RootElement;
        if (!TryGetSongArray(root, out var items)) return result;

        foreach (var item in items.EnumerateArray())
        {
            var song = new SongInfo
            {
                Id = item.GetProperty("id").GetInt64(),
                Name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "",
                DurationMs = item.TryGetProperty("dt", out var dt) && dt.ValueKind == JsonValueKind.Number ? dt.GetInt32()
                    : item.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number ? dur.GetInt32() : 0,
            };

            // 兼容两种字段命名: /search 返回 artists/album, /song/detail 返回 ar/al
            var ar = item.TryGetProperty("ar", out var ara) && ara.ValueKind == JsonValueKind.Array ? ara
                : item.TryGetProperty("artists", out var ata) && ata.ValueKind == JsonValueKind.Array ? ata
                : default;
            if (ar.ValueKind == JsonValueKind.Array)
                song.Artists = ar.EnumerateArray().Select(x => x.GetProperty("name").GetString() ?? "").ToArray();

            var al = item.TryGetProperty("al", out var ala) ? ala
                : item.TryGetProperty("album", out var alb) ? alb : default;
            if (al.ValueKind == JsonValueKind.Object || al.ValueKind == JsonValueKind.Array)
            {
                if (al.ValueKind == JsonValueKind.Array) al = al.GetArrayLength() > 0 ? al[0] : default;
                if (al.ValueKind == JsonValueKind.Object)
                {
                    if (al.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String) song.Album = nm.GetString()!;
                    if (al.TryGetProperty("picUrl", out var pu) && pu.ValueKind == JsonValueKind.String) song.AlbumPicUrl = pu.GetString()!;
                }
            }
            result.Add(song);
        }
        return result;
    }

    private static bool TryGetSongArray(JsonElement root, out JsonElement items)
    {
        items = default;
        // /search 与 /song/detail 结构不同:直接数组(V形)或 result.songs
        if (root.ValueKind == JsonValueKind.Array) { items = root; return true; }
        if (root.TryGetProperty("result", out var r) && r.TryGetProperty("songs", out items)) return true;
        if (root.TryGetProperty("songs", out items)) return true;
        return false;
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}