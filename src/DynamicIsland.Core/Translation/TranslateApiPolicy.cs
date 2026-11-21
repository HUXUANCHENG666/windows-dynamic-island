using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicIsland.Core.Translation;

public enum TranslateApiKind
{
    Default,
    Custom,
    Libre,
    Azure,
    DeepL
}

public readonly record struct TranslateApiOptions(
    TranslateApiKind Kind,
    string? BaseUrl,
    string? Model,
    string? ApiKey,
    string? Region = null);

/// <summary>
/// 实时翻译默认走 MyMemory，不需要密钥。
/// 也可以改成 OpenAI 兼容的 Chat Completions 接口。只允许 https，以及本机的 http。
/// </summary>
public static class TranslateApiPolicy
{
    public const string DefaultSetting = "default";
    public const string CustomSetting = "custom";
    public const int CustomTimeoutSeconds = 15;
    public const string SystemPrompt = "把用户给出的文本译成简体中文。只返回译文，不要解释，不要加引号。";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static TranslateApiKind Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "custom" => TranslateApiKind.Custom, "libre" => TranslateApiKind.Libre,
        "azure" => TranslateApiKind.Azure, "deepl" => TranslateApiKind.DeepL, _ => TranslateApiKind.Default
    };
    public static string ToSetting(TranslateApiKind kind) => kind.ToString().ToLowerInvariant();

    public static TranslateApiOptions From(string? kind, string? url, string? model, string? key) =>
        new(Parse(kind), EmptyToNull(url), EmptyToNull(model), EmptyToNull(key));

    public static bool IsReady(TranslateApiOptions options)
    {
        return Settings.SettingsPagePolicy.ValidateApi(options.Kind, options.BaseUrl, options.Model, options.ApiKey).Length == 0;
    }

    public static bool TryNormalizeEndpoint(string? url, out string endpoint, out string error)
    {
        endpoint = "";
        error = "invalid";
        if (string.IsNullOrWhiteSpace(url))
        {
            error = "missing";
            return false;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "invalid";
            return false;
        }

        bool https = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        bool http = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!https && !(http && uri.IsLoopback))
        {
            error = "scheme";
            return false;
        }

        string path = uri.AbsolutePath.TrimEnd('/');
        if (path == "/")
            path = "";

        string joined;
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            joined = path;
        else if (path.Length == 0 || path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            joined = (path.Length == 0 ? "/v1" : path) + "/chat/completions";
        else
            joined = path + "/v1/chat/completions";

        var builder = new UriBuilder(uri)
        {
            Path = joined,
            Fragment = string.Empty
        };
        endpoint = builder.Uri.AbsoluteUri;
        error = "";
        return true;
    }

    public static string ChatCompletionsBody(string model, string text, string? target = null)
    {
        var payload = new ChatRequest(
            model.Trim(),
            0,
            new[]
            {
                new ChatMessage("system", target?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "Translate the user text into English. Return only the translation, without explanation or quotation marks." : SystemPrompt),
                new ChatMessage("user", text)
            });
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string? ReadChatCompletion(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return null;

            var first = choices[0];
            if (first.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content))
            {
                var text = content.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? EmptyToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("temperature")] int Temperature,
        [property: JsonPropertyName("messages")] ChatMessage[] Messages);

    sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
