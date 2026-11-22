using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
namespace DynamicIsland.Core.Translation;

/// <summary>Official documented endpoints only. Exactly one selected provider; safe errors never include bodies/URIs/keys.</summary>
public sealed class TranslationClient : IDisposable
{
    readonly HttpClient _http;
    readonly object _gate = new();
    readonly Dictionary<TranslateApiKind, DateTimeOffset> _cooldowns = new();
    public TranslationClient(HttpMessageHandler? handler = null)
    {
        _http = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(35);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0");
    }
    public async Task<string> TranslateAsync(TranslateRequest request, TranslateApiOptions api, CancellationToken cancellation)
    {
        int budget = api.Kind == TranslateApiKind.Default ? 450 : 5000;
        if (Encoding.UTF8.GetByteCount(request.Text) <= budget) return await TranslateOneAsync(request, api, cancellation).ConfigureAwait(false);
        var output = new List<string>(); var chunk = new StringBuilder(); int bytes = 0;
        foreach (var rune in request.Text.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > budget)
            {
                output.Add(await TranslateOneAsync(request with { Text = chunk.ToString() }, api, cancellation).ConfigureAwait(false));
                chunk.Clear(); bytes = 0;
            }
            chunk.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        if (chunk.Length > 0) output.Add(await TranslateOneAsync(request with { Text = chunk.ToString() }, api, cancellation).ConfigureAwait(false));
        return string.Join(" ", output);
    }
    async Task<string> TranslateOneAsync(TranslateRequest request, TranslateApiOptions api, CancellationToken cancellation)
    {
        if (!TranslateApiPolicy.IsReady(api)) throw new InvalidOperationException("翻译源尚未配置完整。");
        lock (_gate) if (_cooldowns.TryGetValue(api.Kind, out var until) && until > DateTimeOffset.UtcNow)
            throw new InvalidOperationException("翻译源限流或额度不足，请稍后再试；原文已保留。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        // 自定义接口背后多半是大模型，首包比 MyMemory 慢得多，用统一的 12 秒会误判成超时。
        int limitSeconds = api.Kind == TranslateApiKind.Custom ? TranslateApiPolicy.CustomTimeoutSeconds : 12;
        timeout.CancelAfter(TimeSpan.FromSeconds(limitSeconds));
        try
        {
            using var message = Build(request, api);
            using var response = await _http.SendAsync(message, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is 429 or 456 || (int)response.StatusCode >= 500)
            {
                double seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds
                    ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? ((int)response.StatusCode == 456 ? 300 : 10);
                lock (_gate) _cooldowns[api.Kind] = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(seconds, 1, 300));
            }
            if (!response.IsSuccessStatusCode)
            {
                // 把所有非 2xx 都归成"请求失败"会让用户没法判断是密钥、模型名还是额度问题。
                int code = (int)response.StatusCode;
                string hint = code switch
                {
                    400 => "请求被拒绝：多半是模型名不对，或该接口不接受当前参数。",
                    401 or 403 => "API 密钥无效或没有权限。",
                    404 => "接口地址或模型不存在，检查地址和模型名。",
                    429 => "额度不足或请求过于频繁。",
                    _ => "翻译源请求失败；原文与已有译文已保留。"
                };
                throw new InvalidOperationException(hint + $"（HTTP {code}）");
            }
            string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (api.Kind == TranslateApiKind.Default)
            {
                using var memory = JsonDocument.Parse(body);
                bool exhausted = memory.RootElement.TryGetProperty("quotaFinished", out var quota) && quota.ValueKind == JsonValueKind.True;
                if (exhausted || (memory.RootElement.TryGetProperty("responseStatus", out var status) && status.TryGetInt32(out int code) && code == 429))
                {
                    lock (_gate) _cooldowns[api.Kind] = DateTimeOffset.UtcNow.AddMinutes(5);
                    throw new InvalidOperationException("MyMemory 额度已用完；原文保留，请稍后或自行选择其他源。");
                }
            }
            string result = Parse(body, api.Kind);
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("翻译源返回空结果；原文已保留。");
            return result.Trim();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new InvalidOperationException("翻译请求超时；原文已保留。"); }
        catch (HttpRequestException) { throw new InvalidOperationException("翻译网络不可用；原文已保留。"); }
        catch (JsonException) { throw new InvalidOperationException("翻译源响应格式不可用；原文已保留。"); }
    }
    static HttpRequestMessage Build(TranslateRequest request, TranslateApiOptions api)
    {
        string source = request.Source.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : request.Source;
        string target = request.Target.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : request.Target;
        HttpRequestMessage Json(string url, object body) => new(HttpMethod.Post, url)
        { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        switch (api.Kind)
        {
            case TranslateApiKind.Libre:
                var libre = new Uri(api.BaseUrl!.Trim().TrimEnd('/') + "/");
                string endpoint = libre.AbsolutePath.TrimEnd('/').EndsWith("/translate", StringComparison.Ordinal) ? libre.AbsoluteUri.TrimEnd('/') : new Uri(libre, "translate").AbsoluteUri;
                return Json(endpoint, new { q = request.Text, source = source is "autodetect" or "auto" ? "auto" : source, target, format = "text", api_key = api.ApiKey ?? "" });
            case TranslateApiKind.Azure:
                var azure = Json("https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to=" + Uri.EscapeDataString(target == "zh" ? "zh-Hans" : target)
                    + (source is "autodetect" or "auto" ? "" : "&from=" + Uri.EscapeDataString(source == "zh" ? "zh-Hans" : source)), new[] { new { Text = request.Text } });
                azure.Headers.Add("Ocp-Apim-Subscription-Key", api.ApiKey);
                if (!string.IsNullOrWhiteSpace(api.Region) && api.Region != "global") azure.Headers.Add("Ocp-Apim-Subscription-Region", api.Region.Trim());
                return azure;
            case TranslateApiKind.DeepL:
                var deepl = Json(api.ApiKey!.Trim().EndsWith(":fx", StringComparison.Ordinal) ? "https://api-free.deepl.com/v2/translate" : "https://api.deepl.com/v2/translate",
                    new { text = new[] { request.Text }, target_lang = target == "zh" ? "ZH" : target.ToUpperInvariant() });
                deepl.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", api.ApiKey.Trim());
                return deepl;
            case TranslateApiKind.Custom:
                TranslateApiPolicy.TryNormalizeEndpoint(api.BaseUrl, out var url, out _);
                bool deepseek = url.Contains("deepseek", StringComparison.OrdinalIgnoreCase);
                var custom = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(TranslateApiPolicy.ChatCompletionsBody(api.Model!, request.Text, request.Target, deepseek), Encoding.UTF8, "application/json") };
                custom.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.ApiKey!.Trim()); return custom;
            default:
                return new(HttpMethod.Get, "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(request.Text) + "&langpair=" + Uri.EscapeDataString(request.Source + "|" + request.Target));
        }
    }
    static string Parse(string body, TranslateApiKind kind)
    {
        if (kind == TranslateApiKind.Custom) return TranslateApiPolicy.ReadChatCompletion(body) ?? "";
        using var doc = JsonDocument.Parse(body); var root = doc.RootElement;
        try
        {
            string text = kind switch
            {
                TranslateApiKind.Libre => root.GetProperty("translatedText").GetString() ?? "",
                TranslateApiKind.Azure => root[0].GetProperty("translations")[0].GetProperty("text").GetString() ?? "",
                TranslateApiKind.DeepL => root.GetProperty("translations")[0].GetProperty("text").GetString() ?? "",
                _ => root.GetProperty("responseStatus").GetInt32() == 200 ? WebUtility.HtmlDecode(root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "") : ""
            };
            return text.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) ? "" : text;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        { throw new InvalidOperationException("翻译源响应缺少译文；原文已保留。"); }
    }
    public void Dispose() => _http.Dispose();
}
