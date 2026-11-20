using DynamicIsland.Core.Translation;

namespace DynamicIsland.Core.Settings;

public static class SettingsPagePolicy
{
    public static (double Width, double Height) Fit(double workWidth, double workHeight) =>
        (Math.Max(1, Math.Min(860, workWidth - 24)), Math.Max(1, Math.Min(660, workHeight - 24)));

    // 仅校验，不请求接口；错误文案不包含输入地址、模型或密钥。
    public static string ValidateApi(TranslateApiKind kind, string? url, string? model, string? key)
    {
        if (kind == TranslateApiKind.Default) return "";
        if (kind is TranslateApiKind.Azure or TranslateApiKind.DeepL)
            return string.IsNullOrWhiteSpace(key) ? "请填写此提供商的 API 密钥。" : "";
        if (!TranslateApiPolicy.TryNormalizeEndpoint(url, out _, out _))
            return "请填写有效的 HTTPS 地址；本机服务可使用 HTTP。";
        if (kind == TranslateApiKind.Libre) return ""; // 自建实例可不设key
        if (string.IsNullOrWhiteSpace(model)) return "请填写模型名称。";
        if (string.IsNullOrWhiteSpace(key)) return "请填写 API 密钥。";
        return "";
    }
}
