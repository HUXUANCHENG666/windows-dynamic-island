using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Translation;
namespace DynamicIsland.Core.Tests;

public class TranslationProviderTests
{
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return send(request, token); }
    }
    static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    static TranslateRequest Request(string text = "Hello world") => new(text, "en", "zh-CN", false);

    [Theory]
    [InlineData(TranslateApiKind.Default, "api.mymemory.translated.net", "{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"你好\"}}")]
    [InlineData(TranslateApiKind.Custom, "model.invalid", "{\"choices\":[{\"message\":{\"content\":\"你好\"}}]}")]
    [InlineData(TranslateApiKind.Libre, "localhost", "{\"translatedText\":\"你好\"}")]
    [InlineData(TranslateApiKind.Azure, "api.cognitive.microsofttranslator.com", "[{\"translations\":[{\"text\":\"你好\"}]}]")]
    [InlineData(TranslateApiKind.DeepL, "api.deepl.com", "{\"translations\":[{\"text\":\"你好\"}]}")]
    public async Task ActualHttpAdapterUsesExactlySelectedOfficialProtocolAndParsesResponse(TranslateApiKind kind, string host, string response)
    {
        var handler = new Handler(async (message, _) =>
        {
            Assert.Equal(host, message.RequestUri!.Host);
            string body = message.Content == null ? "" : await message.Content.ReadAsStringAsync();
            if (kind == TranslateApiKind.Custom) Assert.Equal("Bearer", message.Headers.Authorization?.Scheme);
            if (kind == TranslateApiKind.Azure) { Assert.Equal("FAKE-KEY", message.Headers.GetValues("Ocp-Apim-Subscription-Key").Single()); Assert.Contains("zh-Hans", message.RequestUri.Query); }
            if (kind == TranslateApiKind.DeepL) { Assert.Equal("DeepL-Auth-Key", message.Headers.Authorization?.Scheme); Assert.Contains("ZH", body); }
            if (kind == TranslateApiKind.Libre) { Assert.Equal("/translate", message.RequestUri.AbsolutePath); Assert.Contains("Hello world", body); }
            return Json(response);
        });
        using var client = new TranslationClient(handler);
        string? url = kind == TranslateApiKind.Libre ? "http://localhost:5000" : "https://model.invalid";
        Assert.Equal("你好", await client.TranslateAsync(Request(), new(kind, url, "model", "FAKE-KEY", "eastasia"), default));
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task ExistingDeepLFreeSuffixUsesDocumentedFreeHostNotProEndpoint()
    {
        var handler = new Handler((message, _) => { Assert.Equal("api-free.deepl.com", message.RequestUri!.Host); return Task.FromResult(Json("{\"translations\":[{\"text\":\"好\"}]}")); });
        using var client = new TranslationClient(handler);
        await client.TranslateAsync(Request(), new(TranslateApiKind.DeepL, null, null, "FAKE:fx"), default);
    }
    [Fact]
    public async Task LibreDoesNotRequireKeyAndNeverInheritsCustomAuthorization()
    {
        var handler = new Handler(async (message, _) => { Assert.Null(message.Headers.Authorization); Assert.Contains("\"api_key\":\"\"", await message.Content!.ReadAsStringAsync()); return Json("{\"translatedText\":\"好\"}"); });
        using var client = new TranslationClient(handler);
        await client.TranslateAsync(Request(), new(TranslateApiKind.Libre, "http://localhost:5000/prefix/translate", null, null), default);
    }
    [Theory]
    [InlineData(429)]
    [InlineData(456)]
    [InlineData(503)]
    public async Task QuotaAndTransientFailuresBackOffWithoutRetryFanout(int status)
    {
        var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("PRIVATE-RESPONSE-DO-NOT-LOG") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60)); return Task.FromResult(response);
        });
        using var client = new TranslationClient(handler);
        var api = new TranslateApiOptions(TranslateApiKind.Azure, null, null, "FAKE-SECRET");
        for (int i = 0; i < 20; i++)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.TranslateAsync(Request(), api, default));
            Assert.DoesNotContain("PRIVATE", error.ToString()); Assert.DoesNotContain("FAKE-SECRET", error.ToString());
        }
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task MyMemorySplitsActualUtf8BytesWithoutLosingTextOrBreakingUnicode()
    {
        var chunks = new List<string>();
        var handler = new Handler((message, _) =>
        {
            string part = message.RequestUri!.Query.TrimStart('?').Split('&').Single(p => p.StartsWith("q="))[2..];
            string text = Uri.UnescapeDataString(part); chunks.Add(text);
            Assert.InRange(Encoding.UTF8.GetByteCount(text), 1, 450); Assert.DoesNotContain('\uFFFD', text);
            return Task.FromResult(Json("{\"responseStatus\":200,\"responseData\":{\"translatedText\":\"好\"}}"));
        });
        using var client = new TranslationClient(handler);
        string input = string.Concat(Enumerable.Repeat("Hello漢字😀 ", 200));
        await client.TranslateAsync(Request(input), new(TranslateApiKind.Default, null, null, null), default);
        Assert.Equal(input, string.Concat(chunks)); Assert.True(chunks.Count > 1);
    }
    [Fact]
    public async Task RequestCancellationPropagatesWithoutConvertingItToUserFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Json("{}"); });
        using var client = new TranslationClient(handler); using var cts = new CancellationTokenSource();
        var work = client.TranslateAsync(Request(), new(TranslateApiKind.Default, null, null, null), cts.Token);
        await entered.Task; cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"translatedText\":\"\"}")]
    [InlineData("not-json-PRIVATE-BODY")]
    public async Task InvalidResponsesNeverBecomeBlankCaptionsOrLeakyErrors(string body)
    {
        using var client = new TranslationClient(new Handler((_, _) => Task.FromResult(Json(body))));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.TranslateAsync(Request(), new(TranslateApiKind.Libre, "http://localhost:5000", null, null), default));
        Assert.DoesNotContain("PRIVATE-BODY", error.ToString());
    }
    [Fact]
    public void ProviderProfilesPreserveLegacyCustomFieldsAndRoundTripWithoutChangingSelectionOnRead()
    {
        var settings = new AppSettings { TranslateApi = "custom", TranslateApiUrl = "https://existing.invalid", TranslateApiModel = "original-model", TranslateApiKey = "FAKE-LEGACY" };
        var original = JsonSerializer.Serialize(settings);
        foreach (var kind in Enum.GetValues<TranslateApiKind>()) _ = TranslateProviderSettings.Read(settings, kind);
        Assert.Equal(original, JsonSerializer.Serialize(settings));
        TranslateProviderSettings.Save(settings, new(TranslateApiKind.Azure, null, null, "FAKE-AZURE", "eastasia"));
        TranslateProviderSettings.Save(settings, new(TranslateApiKind.Libre, "http://localhost:5000", null, null));
        Assert.Equal("FAKE-LEGACY", settings.TranslateApiKey); Assert.Equal("original-model", settings.TranslateApiModel);
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("FAKE-AZURE", TranslateProviderSettings.Read(restored, TranslateApiKind.Azure).ApiKey);
        Assert.Equal("eastasia", TranslateProviderSettings.Read(restored, TranslateApiKind.Azure).Region);
        Assert.Equal("libre", restored.TranslateApi);
    }
    [Fact]
    public async Task MyMemoryHttpSuccessWithInnerQuotaErrorAlsoBacksOff()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json("{\"responseStatus\":429,\"quotaFinished\":true}")));
        using var client = new TranslationClient(handler);
        for (int i = 0; i < 10; i++) await Assert.ThrowsAsync<InvalidOperationException>(() => client.TranslateAsync(Request(), new(TranslateApiKind.Default, null, null, null), default));
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public void CustomClipboardDirectionUsesEnglishPromptWithoutChangingLiveDefaultChinese()
    {
        using var en = JsonDocument.Parse(TranslateApiPolicy.ChatCompletionsBody("model", "你好", "en"));
        using var zh = JsonDocument.Parse(TranslateApiPolicy.ChatCompletionsBody("model", "hello"));
        Assert.Contains("English", en.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(TranslateApiPolicy.SystemPrompt, zh.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

}
