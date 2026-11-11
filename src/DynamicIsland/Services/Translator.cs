using System.Net.Http;
using System.Text.Json;
using DynamicIsland.Core.Translation;

namespace DynamicIsland.Services;

sealed class Translator : IDisposable
{
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public Translator()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.1");
    }

    public async Task<string> TranslateAsync(TranslateRequest request, CancellationToken cancellation)
    {
        var url = "https://api.mymemory.translated.net/get?q="
            + Uri.EscapeDataString(request.Text)
            + "&langpair="
            + Uri.EscapeDataString(request.Source + "|" + request.Target);
        using var response = await _http.GetAsync(url, cancellation).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("翻译服务暂时连不上。检查网络后再试。");
        return Read(body);
    }

    static string Read(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            int status = root.TryGetProperty("responseStatus", out var statusElement) ? statusElement.GetInt32() : 0;
            string text = "";
            if (root.TryGetProperty("responseData", out var data)
                && data.TryGetProperty("translatedText", out var translated))
                text = translated.GetString() ?? "";
            if (status != 200 || string.IsNullOrWhiteSpace(text) || text.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
            {
                if (body.Contains("QUERY LENGTH", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("这段话太长了，缩短一点再译。");
                throw new InvalidOperationException("没有得到译文。可以过一会儿再试。");
            }

            return text.Trim();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("翻译服务没有返回可用的结果。");
        }
    }

    public void Dispose() => _http.Dispose();
}
