using DynamicIsland.Core.Translation;

namespace DynamicIsland.Core.LiveTranslate;

public enum LiveTranslateDecision
{
    Empty,
    AlreadyChinese,
    Translate
}

public readonly record struct LiveTranslatePlan(
    LiveTranslateDecision Decision,
    string DisplayText,
    TranslateRequest? Request);

/// <summary>
/// 实时翻译默认关，也不会记住上次有没有开。打开之后才捕获系统正在播放的声音。
/// 识别出的中文直接显示，不再送去翻译。外语才整理成发往 MyMemory 的请求，目标固定是简体中文。
/// </summary>
public static class LiveTranslatePolicy
{
    public const bool EnabledByDefault = false;
    public const bool PersistsAcrossLaunches = false;
    public const int MaxDisplayChars = 80;

    public static bool IsChineseLanguage(string? whisperLanguage)
    {
        if (string.IsNullOrWhiteSpace(whisperLanguage))
            return false;
        var lang = whisperLanguage.Trim().ToLowerInvariant();
        if (lang.StartsWith("zh", StringComparison.Ordinal) || lang.StartsWith("yue", StringComparison.Ordinal))
            return true;
        return lang is "chinese" or "cantonese" or "mandarin";
    }

    public static bool IsUsableTranscript(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var trimmed = Collapse(text);
        if (trimmed.Length == 0)
            return false;
        if (IsWrappedMark(trimmed))
            return false;
        if (trimmed.Contains("BLANK_AUDIO", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("NO SPEECH", StringComparison.OrdinalIgnoreCase))
            return false;
        return TranslatePolicy.ContainsLanguage(trimmed);
    }

    public static bool IsAlreadyChinese(string text, string? whisperLanguage) =>
        IsChineseLanguage(whisperLanguage) || TranslatePolicy.LooksChinese(text);

    public static string MapSource(string? whisperLanguage)
    {
        if (string.IsNullOrWhiteSpace(whisperLanguage))
            return "autodetect";
        var lang = whisperLanguage.Trim().ToLowerInvariant();
        if (lang.Length >= 2 && lang[0] is (>= 'a' and <= 'z') && lang[1] is (>= 'a' and <= 'z'))
            return lang[..2];
        return "autodetect";
    }

    public static LiveTranslatePlan Plan(string? transcript, string? whisperLanguage)
    {
        if (!IsUsableTranscript(transcript))
            return new LiveTranslatePlan(LiveTranslateDecision.Empty, "", null);

        var text = Collapse(transcript!);
        if (IsAlreadyChinese(text, whisperLanguage))
            return new LiveTranslatePlan(LiveTranslateDecision.AlreadyChinese, ForIsland(text), null);

        var request = TranslatePolicy.Build(text, TranslateDirection.EnglishToChinese);
        if (request is null)
            return new LiveTranslatePlan(LiveTranslateDecision.Empty, "", null);

        var shaped = new TranslateRequest(request.Value.Text, MapSource(whisperLanguage), "zh-CN", request.Value.Clipped);
        return new LiveTranslatePlan(LiveTranslateDecision.Translate, ForIsland(text), shaped);
    }

    public static string ForIsland(string? text)
    {
        var trimmed = Collapse(text ?? "");
        if (trimmed.Length <= MaxDisplayChars)
            return trimmed;
        return trimmed[..MaxDisplayChars].TrimEnd() + "…";
    }

    public static string Collapse(string text)
    {
        var chars = new char[text.Length];
        int n = 0;
        bool space = false;
        foreach (var c in text.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space && n > 0)
                chars[n++] = ' ';
            space = false;
            chars[n++] = c;
        }

        return new string(chars, 0, n);
    }

    static bool IsWrappedMark(string text) =>
        text.Length >= 2
        && ((text[0] == '[' && text[^1] == ']') || (text[0] == '(' && text[^1] == ')') || (text[0] == '（' && text[^1] == '）'));
}

public static class LiveTranslateCopy
{
    public const string Title = "实时翻译";
    public const string Chip = "翻译";
    public const string Close = "关闭";
    public const string Cancel = "取消";
    public const string Listening = "正在听系统声音…";
    public const string Recognizing = "正在识别…";
    public const string Translating = "正在翻译…";
    public const string LoadingModel = "正在加载识别模型…";
    public const string Connecting = "正在连接下载源…";
    public const string SwitchingSource = "正在换一个下载源…";
    public const string AlreadyChinese = "已是中文";
    public const string CaptureFailed = "无法捕获系统声音。有的游戏会独占音频设备。";
    public const string DownloadFailed = "识别模型下载失败。请检查网络后重试。";
    public const string Cancelled = "已取消下载。";
    public const string TranslateFailed = "这一句没有译成。";
    public const string ModelCorrupt = "识别模型不完整，请关掉后重新打开，让它再下载一次。";

    public static string Downloading(double progress) => Downloading(progress, 0, null);

    public static string Downloading(double progress, long received, long? total)
    {
        if (progress > 0 && !double.IsNaN(progress))
        {
            int percent = (int)Math.Round(Math.Clamp(progress, 0, 1) * 100);
            return $"正在下载识别模型 {percent}%";
        }

        if (received > 0)
            return $"正在下载识别模型 {FormatMegabytes(received)}";

        return "正在下载识别模型…";
    }

    public static string FormatMegabytes(long bytes)
    {
        double mb = Math.Max(0, bytes) / (1024.0 * 1024.0);
        return $"{mb:0.0} MB";
    }

    public static double Progress(long received, long? total)
    {
        if (total is not > 0 || received <= 0)
            return 0;
        return Math.Clamp(received / (double)total.Value, 0, 1);
    }
}
