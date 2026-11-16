namespace DynamicIsland.Core.LiveTranslate;

public readonly record struct LiveTranslateCaption(
    int Id,
    TimeSpan Elapsed,
    string Timestamp,
    string Primary,
    string? Original,
    bool AlreadyChinese,
    bool TranslationPending,
    bool TranslationFailed);

/// <summary>
/// 岛上最多显示三条字幕。旧的在上，新的在下。
/// 时间戳是本次打开实时翻译后，从开始听系统声音起算的经过时间；
/// 如果还没进入收听就出了第一条结果，则从这条结果起算。
/// 格式为 m:ss，满 10 分钟为 mm:ss，满 1 小时为 h:mm:ss。
/// 关闭或重新打开会清空，并从 0 重新计。
/// 识别一出来先作为预览写入，译文到达后再替换该条的中文；失败则保留识别文字。
/// </summary>
public sealed class LiveTranslateCaptionChain
{
    public const int MaxItems = 3;

    readonly List<LiveTranslateCaption> _items = new();
    DateTime? _origin;
    int _nextId = 1;

    public IReadOnlyList<LiveTranslateCaption> Items => _items;

    public bool HasPending
    {
        get
        {
            foreach (var item in _items)
            {
                if (item.TranslationPending)
                    return true;
            }

            return false;
        }
    }

    public void Reset()
    {
        _items.Clear();
        _origin = null;
        // Keep _nextId moving so an in-flight Complete/Fail cannot touch a new session.
    }

    public void MarkListening(DateTime utcNow) => _origin ??= utcNow;

    public int Preview(string text, DateTime utcNow)
    {
        var caption = Create(text, original: null, alreadyChinese: false, pending: true, failed: false, utcNow);
        Append(caption);
        return caption.Id;
    }

    public int AppendFinal(string text, DateTime utcNow, bool alreadyChinese = false)
    {
        var caption = Create(text, original: null, alreadyChinese, pending: false, failed: false, utcNow);
        Append(caption);
        return caption.Id;
    }

    public bool Complete(int id, string primary, string? original)
    {
        int index = IndexOf(id);
        if (index < 0)
            return false;

        var current = _items[index];
        string? shown = null;
        if (!string.IsNullOrWhiteSpace(original)
            && !string.Equals(original, primary, StringComparison.Ordinal))
            shown = original;

        _items[index] = current with
        {
            Primary = primary,
            Original = shown,
            TranslationPending = false,
            TranslationFailed = false,
            AlreadyChinese = false
        };
        return true;
    }

    public bool Fail(int id)
    {
        int index = IndexOf(id);
        if (index < 0)
            return false;

        var current = _items[index];
        _items[index] = current with
        {
            TranslationPending = false,
            TranslationFailed = true
        };
        return true;
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        int total = (int)elapsed.TotalSeconds;
        int hours = total / 3600;
        int minutes = total / 60 % 60;
        int seconds = total % 60;
        if (hours > 0)
            return $"{hours}:{minutes:00}:{seconds:00}";
        return $"{total / 60}:{seconds:00}";
    }

    void Append(LiveTranslateCaption caption)
    {
        _items.Add(caption);
        while (_items.Count > MaxItems)
            _items.RemoveAt(0);
    }

    LiveTranslateCaption Create(string text, string? original, bool alreadyChinese, bool pending, bool failed, DateTime utcNow)
    {
        _origin ??= utcNow;
        var elapsed = utcNow - _origin.Value;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return new LiveTranslateCaption(
            _nextId++,
            elapsed,
            FormatElapsed(elapsed),
            text,
            original,
            alreadyChinese,
            pending,
            failed);
    }

    int IndexOf(int id)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Id == id)
                return i;
        }

        return -1;
    }
}
