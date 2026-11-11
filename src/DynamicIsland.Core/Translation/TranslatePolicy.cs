namespace DynamicIsland.Core.Translation;

public enum TranslateDirection
{
    Auto,
    ChineseToEnglish,
    EnglishToChinese
}

public readonly record struct TranslateRequest(string Text, string Source, string Target, bool Clipped);

/// <summary>
/// 剪贴板变化只允许在本地弹出邀请。网络请求必须经过 Confirm。
/// </summary>
public static class TranslatePolicy
{
    public const int MaxInviteChars = 2000;
    public const int MaxSendChars = 400;
    public const bool BackgroundSendEnabled = false;

    public static TranslateDirection Parse(string? value) => value switch
    {
        "zh-en" => TranslateDirection.ChineseToEnglish,
        "en-zh" => TranslateDirection.EnglishToChinese,
        _ => TranslateDirection.Auto
    };

    public static string ToSetting(TranslateDirection direction) => direction switch
    {
        TranslateDirection.ChineseToEnglish => "zh-en",
        TranslateDirection.EnglishToChinese => "en-zh",
        _ => "auto"
    };

    public static bool ShouldInvite(string? clipboardText, string? previousInviteText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
            return false;
        var trimmed = clipboardText.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxInviteChars)
            return false;
        if (!ContainsLanguage(trimmed))
            return false;
        if (previousInviteText != null && string.Equals(trimmed, previousInviteText.Trim(), StringComparison.Ordinal))
            return false;
        return true;
    }

    public static bool ContainsLanguage(string text)
    {
        foreach (var c in text)
        {
            if (char.IsLetter(c))
                return true;
        }

        return false;
    }

    public static bool LooksChinese(string text)
    {
        int cjk = 0;
        int latin = 0;
        foreach (var c in text)
        {
            if (c is >= '\u4E00' and <= '\u9FFF')
                cjk++;
            else if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'))
                latin++;
        }

        return cjk > 0 && cjk >= latin;
    }

    public static (string Source, string Target) Pair(string text, TranslateDirection direction) => direction switch
    {
        TranslateDirection.ChineseToEnglish => ("zh-CN", "en"),
        TranslateDirection.EnglishToChinese => ("en", "zh-CN"),
        _ => LooksChinese(text) ? ("zh-CN", "en") : ("en", "zh-CN")
    };

    public static TranslateRequest? Build(string? text, TranslateDirection direction)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var trimmed = text.Trim();
        if (!ContainsLanguage(trimmed))
            return null;
        bool clipped = trimmed.Length > MaxSendChars;
        var body = clipped ? trimmed[..MaxSendChars] : trimmed;
        var (source, target) = Pair(body, direction);
        return new TranslateRequest(body, source, target, clipped);
    }
}

public sealed class TranslateSession
{
    public string? PendingText { get; private set; }
    public bool SendAuthorized { get; private set; }
    public TranslateRequest? LastRequest { get; private set; }
    public string? LastResult { get; private set; }
    int _clipboardEcho;

    public void RememberResult(string text) => LastResult = text.Trim();

    public void ForgetResult() => LastResult = null;

    /// <summary>刚把译文写进剪贴板。下一次剪贴板通知是这次复制的回声，不能再弹邀请。</summary>
    public void SuppressClipboardEcho() => _clipboardEcho++;

    public void CancelClipboardEcho()
    {
        if (_clipboardEcho > 0)
            _clipboardEcho--;
    }

    public bool IsOwnResult(string? text) =>
        LastResult != null
        && !string.IsNullOrWhiteSpace(text)
        && string.Equals(text.Trim(), LastResult, StringComparison.Ordinal);

    public bool OfferClipboard(string? text)
    {
        SendAuthorized = false;
        if (_clipboardEcho > 0)
        {
            _clipboardEcho--;
            return false;
        }

        if (IsOwnResult(text))
            return false;
        if (!TranslatePolicy.ShouldInvite(text, PendingText))
            return false;
        PendingText = text!.Trim();
        return true;
    }

    public void SetPending(string? text)
    {
        SendAuthorized = false;
        PendingText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public TranslateRequest? Confirm(TranslateDirection direction)
    {
        var request = TranslatePolicy.Build(PendingText, direction);
        if (request is null)
            return null;
        SendAuthorized = true;
        LastRequest = request;
        return request;
    }
}
