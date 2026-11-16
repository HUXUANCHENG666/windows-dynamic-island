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
/// 识别出的中文直接显示，不再送去翻译。外语才整理成翻译请求，目标固定是简体中文。
/// </summary>
public static class LiveTranslatePolicy
{
    public const bool EnabledByDefault = false;
    public const bool PersistsAcrossLaunches = false;
    public const int MaxDisplayChars = 96;

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
    public const string CaptureNoDevice = "没有找到正在使用的播放设备。";
    public const string CaptureDenied = "系统没有允许捕获播放声音。请在设置里允许桌面应用使用麦克风后再试。";
    public const string CaptureFormatUnsupported = "播放设备的声音格式暂不支持。";
    public const string CaptureFailedGeneric = "无法捕获系统声音。请检查系统声音，并在设置里允许桌面应用使用麦克风后再试。";
    public const string CaptureOpenFailed = "无法打开系统播放设备。请关掉翻译后再试一次。";

    public static string MessageForCaptureException(Exception ex)
    {
        if (ex is InvalidOperationException && ex.Message == CaptureFormatUnsupported)
            return CaptureFormatUnsupported;

        var text = ex.ToString();
        uint hr = unchecked((uint)ex.HResult);
        if (HasHResult(hr, 0x80070490, 0x80070002) ||
            text.Contains("Element not found", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("NoEndpoint", StringComparison.OrdinalIgnoreCase))
            return CaptureNoDevice;

        if (HasHResult(hr, 0x8889000A, 0x88890010) ||
            text.Contains("DEVICE_IN_USE", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("exclusive", StringComparison.OrdinalIgnoreCase))
            return CaptureFailed;

        if (HasHResult(hr, 0x80070005) ||
            text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("E_ACCESSDENIED", StringComparison.OrdinalIgnoreCase))
            return CaptureDenied;

        if (ex is InvalidCastException || HasHResult(hr, 0x80004002))
            return CaptureOpenFailed;

        return CaptureFailedGeneric;
    }

    static bool HasHResult(uint actual, params uint[] codes)
    {
        foreach (var code in codes)
        {
            if (actual == code)
                return true;
        }

        return false;
    }

    public const string DownloadFailed = "识别模型下载失败。请检查网络后重试。";
    public const string Cancelled = "已取消下载。";
    public const string TranslateFailed = "这一句没有译成。";
    public const string ApiNotConfigured = "请先在设置里填写接口地址、模型和密钥";
    public const string ApiRejected = "这个接口地址不能用。请填写 https 地址，或本机的 http 地址。";
    public const string ModelCorrupt = "识别模型不完整，正在重新下载…";
    public const string ModelLoadFailed = "识别模型无法加载。请关掉翻译后再试一次。";
    public const string NativeLibraryMissing = "识别组件没有装完整。请重新解压整个测试包，让 runtimes 文件夹和程序放在一起。";
    public const string NativeLibraryLoadFailed = "识别组件加载失败。请安装微软 Visual C++ 可再发行程序包（x64）后再试。";
    public const string CpuNotSupported = "这台电脑的处理器暂不支持当前识别组件。";

    /// <summary>
    /// 原生识别库没找到或没加载成功时，不应删模型、也不该再下一次。
    /// </summary>
    public static bool TryNativeLibraryError(Exception ex, out string message)
    {
        var text = ex.ToString();
        if (ex is PlatformNotSupportedException
            || text.Contains("AVX is not supported", StringComparison.OrdinalIgnoreCase))
        {
            message = CpuNotSupported;
            return true;
        }

        if (ex is FileNotFoundException
            || text.Contains("Native Library not found", StringComparison.OrdinalIgnoreCase))
        {
            message = NativeLibraryMissing;
            return true;
        }

        if (ex is DllNotFoundException
            || ex is BadImageFormatException
            || text.Contains("Failed to load native whisper library", StringComparison.OrdinalIgnoreCase))
        {
            message = NativeLibraryLoadFailed;
            return true;
        }

        message = ModelLoadFailed;
        return false;
    }

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
