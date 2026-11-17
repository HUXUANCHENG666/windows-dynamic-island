namespace DynamicIsland.Core.LiveTranslate;

public readonly record struct LiveTranslateCaption(
    int Id,
    TimeSpan Elapsed,
    string Timestamp,
    string Primary,
    string? Original,
    bool AlreadyChinese,
    bool TranslationPending,
    bool TranslationFailed,
    bool Open);

public readonly record struct CaptionUpdate(int Id, string Recognized, bool Merged);

/// <summary>
/// 本次打开实时翻译后，最多记住 40 条字幕。旧的在上，新的在下。
/// 岛上大约能同时看见三句，更早的可以滑回去看。关闭或重新打开会清空。
/// 时间戳从开始听系统声音起算；还没进入收听就出了第一条时，从这条起算。
/// 格式为 m:ss，满 10 分钟为 mm:ss，满 1 小时为 h:mm:ss。
/// 说话过程中同一句话会持续更新同一条字幕（Open），像会议记录一样先出字；
/// 这句话结束后再去翻译，译文到达后替换该条的中文，失败则保留识别文字。
/// 若上一句已经收束但没有句末标点，很短的后续碎片仍会接到上一句。
/// </summary>
public sealed class LiveTranslateCaptionChain
{
    public const int MaxItems = 40;
    public const int MaxMergeWords = 2;

    static readonly char[] SentenceStops = ['.', '。', '!', '?', '！', '？', '…'];

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

    public CaptionUpdate Preview(string text, DateTime utcNow) =>
        Accept(text, utcNow, final: true, alreadyChinese: false);

    public CaptionUpdate AppendFinal(string text, DateTime utcNow, bool alreadyChinese = false) =>
        Accept(text, utcNow, final: true, alreadyChinese: alreadyChinese);

    /// <summary>
    /// 写入或更新当前这句话。说话过程中就会去翻译，不必等这句停稳；final 为 true 时收束这一行。
    /// 当前句还开着时，用最新的整句识别结果替换，而不是把字再拼到后面。
    /// 若这行已经有译文，先保留它，等新的译文到了再换。
    /// </summary>
    public CaptionUpdate Accept(string text, DateTime utcNow, bool final, bool alreadyChinese = false)
    {
        var display = LiveTranslatePolicy.ForIsland(text);
        if (_items.Count > 0)
        {
            int index = _items.Count - 1;
            var last = _items[index];
            var recognized = string.IsNullOrWhiteSpace(last.Original) ? last.Primary : last.Original;
            if (last.Open || ShouldMerge(recognized, display))
            {
                var combined = last.Open ? display : Combine(recognized, display);
                combined = LiveTranslatePolicy.ForIsland(combined);
                Write(index, last, combined, final, alreadyChinese || last.AlreadyChinese);
                return new CaptionUpdate(last.Id, combined, true);
            }
        }

        var caption = Create(
            display,
            original: null,
            alreadyChinese,
            pending: !alreadyChinese,
            failed: false,
            open: !final,
            utcNow);
        Append(caption);
        return new CaptionUpdate(caption.Id, caption.Primary, false);
    }

    void Write(int index, LiveTranslateCaption last, string combined, bool final, bool alreadyChinese)
    {
        if (alreadyChinese)
        {
            _items[index] = last with
            {
                Primary = combined,
                Original = null,
                AlreadyChinese = true,
                TranslationPending = false,
                TranslationFailed = false,
                Open = !final
            };
            return;
        }

        bool hadTranslation = !string.IsNullOrWhiteSpace(last.Original);
        if (!final)
        {
            _items[index] = last with
            {
                Primary = hadTranslation ? last.Primary : combined,
                Original = hadTranslation ? combined : null,
                AlreadyChinese = false,
                TranslationPending = true,
                TranslationFailed = false,
                Open = true
            };
            return;
        }

        _items[index] = last with
        {
            Primary = hadTranslation ? last.Primary : combined,
            Original = hadTranslation ? combined : null,
            AlreadyChinese = false,
            TranslationPending = true,
            TranslationFailed = false,
            Open = false
        };
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

    public string? RecognizedText(int id)
    {
        int index = IndexOf(id);
        if (index < 0)
            return null;
        var item = _items[index];
        return string.IsNullOrWhiteSpace(item.Original) ? item.Primary : item.Original;
    }

    public static bool ShouldMerge(string? previous, string incoming)
    {
        if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(incoming))
            return false;
        if (EndsWithSentenceStop(previous))
            return false;
        return WordCount(incoming) > 0 && WordCount(incoming) <= MaxMergeWords;
    }

    public static string Combine(string previous, string incoming)
    {
        var left = LiveTranslatePolicy.Collapse(previous);
        var right = LiveTranslatePolicy.Collapse(incoming);
        if (left.Length == 0)
            return right;
        if (right.Length == 0)
            return left;
        if (NeedsSpace(left, right))
            return left + " " + right;
        return left + right;
    }

    public static bool EndsWithSentenceStop(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0)
            return false;
        if (trimmed.EndsWith("...", StringComparison.Ordinal) || trimmed.EndsWith("……", StringComparison.Ordinal))
            return true;
        return SentenceStops.Contains(trimmed[^1]);
    }

    public static int WordCount(string text)
    {
        var trimmed = LiveTranslatePolicy.Collapse(text);
        if (trimmed.Length == 0)
            return 0;
        if (trimmed.Contains(' '))
            return trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        foreach (var c in trimmed)
        {
            if (IsLatinLetter(c))
                return 1;
        }

        return (trimmed.Length + 1) / 2;
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

    LiveTranslateCaption Create(string text, string? original, bool alreadyChinese, bool pending, bool failed, bool open, DateTime utcNow)
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
            failed,
            open);
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

    static bool NeedsSpace(string left, string right)
    {
        char a = left[^1];
        char b = right[0];
        if (char.IsWhiteSpace(a) || char.IsWhiteSpace(b))
            return false;
        return IsLatinLetter(a) || IsLatinLetter(b) || (char.IsDigit(a) && char.IsDigit(b));
    }

    static bool IsLatinLetter(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
}
