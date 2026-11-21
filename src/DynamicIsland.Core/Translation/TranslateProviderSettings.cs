using DynamicIsland.Core.Settings;
namespace DynamicIsland.Core.Translation;

public static class TranslateProviderSettings
{
    public static TranslateApiOptions Read(AppSettings settings, TranslateApiKind? provider = null)
    {
        var kind = provider ?? TranslateApiPolicy.Parse(settings.TranslateApi);
        if (kind is TranslateApiKind.Custom or TranslateApiKind.Default)
            return new(kind, settings.TranslateApiUrl, settings.TranslateApiModel, settings.TranslateApiKey);
        TranslateProviderProfile? profile = null;
        settings.TranslateProviders?.TryGetValue(TranslateApiPolicy.ToSetting(kind), out profile);
        return new(kind, profile?.Url, null, profile?.Key, profile?.Region);
    }
    public static void Save(AppSettings settings, TranslateApiOptions api)
    {
        settings.TranslateApi = TranslateApiPolicy.ToSetting(api.Kind);
        if (api.Kind == TranslateApiKind.Custom)
        {
            settings.TranslateApiUrl = Empty(api.BaseUrl); settings.TranslateApiModel = Empty(api.Model); settings.TranslateApiKey = Empty(api.ApiKey);
        }
        else if (api.Kind != TranslateApiKind.Default)
        {
            settings.TranslateProviders ??= new();
            settings.TranslateProviders[settings.TranslateApi] = new() { Url = Empty(api.BaseUrl), Key = Empty(api.ApiKey), Region = Empty(api.Region) };
        }
    }
    static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static string Description(TranslateApiKind kind) => kind switch
    {
        TranslateApiKind.Default => "MyMemory：免密钥，匿名约5000字符/日；实时频繁翻译很快耗尽额度。",
        TranslateApiKind.Custom => "保留现有 OpenAI 兼容接口；是否免费由你的提供商决定。",
        TranslateApiKind.Libre => "LibreTranslate 自建：开源软件免费、运行成本自理；填写你信任的实例地址，密钥可选。官方托管站并非免费。",
        TranslateApiKind.Azure => "Azure Translator F0：每月200万字符，需自行申请免费层和密钥；区域填资源区域，global可留空。其他层可能收费。",
        _ => "DeepL Developer：新账户共100万字符一次性额度，不按月重置，需自己的密钥；旧Free账户每月50万。超额不自动升级。"
    };
}
