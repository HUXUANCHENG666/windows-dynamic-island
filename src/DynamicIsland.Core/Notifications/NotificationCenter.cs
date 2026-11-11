using System.Globalization;

namespace DynamicIsland.Core.Notifications;

public enum NotificationAccess
{
    Unknown = 0,
    Allowed = 1,
    Denied = 2,
    Unspecified = 3,
    Unavailable = 4
}

public enum NotificationLaunchKind
{
    None = 0,
    Protocol = 1,
    App = 2
}

public enum NotificationLogEvent
{
    Listening,
    Events,
    Poll,
    Opened,
    OpenFailed,
    Stopped,
    Baseline
}

/// <summary>
/// 一条已经在通知中心出现过的消息，裁剪成岛上能放下的三行。
/// </summary>
public sealed record NotificationCard(
    uint Id,
    string AppName,
    string Title,
    string Summary,
    NotificationLaunch Launch);

public readonly record struct NotificationLaunch(NotificationLaunchKind Kind, string Target, string? Arguments)
{
    public static NotificationLaunch None { get; } = new(NotificationLaunchKind.None, "", null);
}

/// <summary>
/// 监听层读到的原始字段。正文只留在这个对象里，用来映射，不进日志。
/// </summary>
public readonly record struct NotificationSource(
    uint Id,
    string? AppName,
    string? Title,
    IReadOnlyList<string>? Details,
    string? Launch,
    string? AppUserModelId);

public static class NotificationAccessPolicy
{
    public const bool EnabledByDefault = false;

    /// <summary>只有用户打开了开关，并且系统已经允许，才可以去读通知中心。</summary>
    public static bool MayListen(bool userEnabled, NotificationAccess access) =>
        userEnabled && access == NotificationAccess.Allowed;
}

public static class NotificationPolicy
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromMilliseconds(800);
}

public static class NotificationCopy
{
    public const string Title = "通知中心";

    public const string FirstUse =
        "接下来会请求读取通知。微信、QQ 还要在自己的设置里打开系统通知。";

    public const string Allowed =
        "已打开。微信、QQ 还要打开系统通知，消息进了通知中心才会出现。";

    public const string Denied =
        "请打开「设置 → 隐私和安全性 → 通知」，允许灵动岛读取通知。";

    public const string Unspecified =
        "还没有允许读取通知。需要允许之后才会显示通知中心。";

    public const string Unavailable =
        "这台系统暂时提供不了通知中心。";

    public const string Closed =
        "已关闭岛上的通知中心。";

    public const string OpenFailed =
        "系统没有打开对应的应用。";
}

public static class NotificationPrivacy
{
    public const bool UploadsContent = false;

    public static bool MayTransmit(string? title, string? summary, string? body)
    {
        _ = title;
        _ = summary;
        _ = body;
        return false;
    }
}

public static class NotificationLog
{
    public static string Access(NotificationAccess access) =>
        "notification-center access " + access.ToString().ToLowerInvariant();

    public static string Arrival(NotificationCard card)
    {
        _ = card;
        return "notification-center arrived";
    }

    public static string Failed(int hresult) =>
        "notification-center failed " + hresult.ToString(CultureInfo.InvariantCulture);

    public static string Line(NotificationLogEvent kind) => kind switch
    {
        NotificationLogEvent.Listening => "notification-center listening",
        NotificationLogEvent.Events => "notification-center events",
        NotificationLogEvent.Poll => "notification-center poll",
        NotificationLogEvent.Opened => "notification-center opened",
        NotificationLogEvent.OpenFailed => "notification-center open failed",
        NotificationLogEvent.Stopped => "notification-center stopped",
        NotificationLogEvent.Baseline => "notification-center baseline",
        _ => "notification-center"
    };

    public static bool Exposes(string? line, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(line))
            return false;
        foreach (var secret in secrets)
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 2)
                continue;
            if (line.Contains(secret, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

public static class NotificationText
{
    public const int MaxAppName = 32;
    public const int MaxTitle = 48;
    public const int MaxSummary = 80;

    public static NotificationCard? Map(NotificationSource source)
    {
        string app = Clip(source.AppName, MaxAppName);
        string title = Clip(source.Title, MaxTitle);
        string summary = Clip(Join(source.Details), MaxSummary);
        if (app.Length == 0 && title.Length == 0 && summary.Length == 0)
            return null;

        if (title.Length == 0 && summary.Length > 0)
        {
            title = Clip(summary, MaxTitle);
            summary = "";
        }

        if (title.Length == 0)
            title = "新通知";
        if (app.Length == 0)
            app = "通知";
        if (string.Equals(summary, title, StringComparison.Ordinal))
            summary = "";

        return new NotificationCard(
            source.Id,
            app,
            title,
            summary,
            NotificationLaunchPlanner.Plan(source.Launch, source.AppUserModelId));
    }

    public static string Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || max <= 0)
            return "";

        var buffer = new System.Text.StringBuilder(Math.Min(value.Length, max + 8));
        bool pendingSpace = false;
        foreach (char ch in value.Trim())
        {
            if (char.IsControl(ch) || ch == '\u200b' || char.IsWhiteSpace(ch))
            {
                pendingSpace = buffer.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                buffer.Append(' ');
                pendingSpace = false;
            }

            buffer.Append(ch);
        }

        var text = buffer.ToString().Trim();
        if (text.Length <= max)
            return text;
        if (max == 1)
            return "…";
        return text[..(max - 1)].TrimEnd() + "…";
    }

    static string Join(IReadOnlyList<string>? lines)
    {
        if (lines == null || lines.Count == 0)
            return "";

        var buffer = new System.Text.StringBuilder();
        int count = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (count >= 4 || buffer.Length >= 160)
                break;
            if (buffer.Length > 0)
                buffer.Append(' ');
            buffer.Append(line.Trim());
            count++;
        }

        return buffer.ToString();
    }
}

public static class NotificationLaunchPlanner
{
    static readonly HashSet<string> BlockedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "file", "shell", "javascript", "vbscript", "ms-msdt", "script", "about", "data", "cmd", "powershell"
    };

    public static NotificationLaunch Plan(string? launch, string? appUserModelId)
    {
        if (TryProtocol(launch, out var uri))
            return new NotificationLaunch(NotificationLaunchKind.Protocol, uri, null);
        if (!IsSafeAppId(appUserModelId))
            return NotificationLaunch.None;

        string? arguments = string.IsNullOrWhiteSpace(launch) ? null : NotificationText.Clip(launch, 180);
        return new NotificationLaunch(NotificationLaunchKind.App, appUserModelId!.Trim(), arguments);
    }

    public static bool IsSafeAppId(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
            return false;
        var text = appUserModelId.Trim();
        if (text.Length is 0 or > 200)
            return false;
        foreach (char c in text)
        {
            if (char.IsControl(c))
                return false;
            if (c is '\\' or '/' or '&' or '|' or '<' or '>' or '"' or '\'' or '%')
                return false;
        }

        return true;
    }

    public static bool IsAllowedProtocol(string? value) => TryProtocol(value, out _);

    static bool TryProtocol(string? launch, out string uri)
    {
        uri = "";
        if (string.IsNullOrWhiteSpace(launch))
            return false;
        var text = launch.Trim();
        if (text.Length > 512 || text.IndexOfAny(['\r', '\n']) >= 0)
            return false;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || !parsed.IsAbsoluteUri)
            return false;

        var scheme = parsed.Scheme;
        if (scheme.Length is < 2 or > 32 || BlockedSchemes.Contains(scheme))
            return false;
        foreach (char c in scheme)
        {
            if (!(char.IsLetterOrDigit(c) || c is '+' or '.' or '-'))
                return false;
        }

        if (string.IsNullOrEmpty(parsed.Host) && parsed.AbsolutePath is "" or "/")
            return false;

        uri = parsed.AbsoluteUri;
        return true;
    }
}

/// <summary>
/// 记住已经见过的通知。第一次快照只当作底，不把通知中心里的旧消息再弹一遍。
/// </summary>
public sealed class NotificationFeed
{
    readonly Dictionary<uint, string> _fingerprints = new();
    NotificationCard? _lastAnnounced;
    DateTime _lastAnnouncedAt;

    public bool IsSeeded { get; private set; }

    public void Reset()
    {
        _fingerprints.Clear();
        _lastAnnounced = null;
        _lastAnnouncedAt = default;
        IsSeeded = false;
    }

    public void Seed(IEnumerable<NotificationSource> existing)
    {
        foreach (var item in existing)
        {
            if (_fingerprints.ContainsKey(item.Id))
                continue;
            _fingerprints[item.Id] = Fingerprint(NotificationText.Map(item));
        }

        IsSeeded = true;
    }

    public NotificationCard? Observe(NotificationSource source, DateTime now)
    {
        var card = NotificationText.Map(source);
        string fingerprint = Fingerprint(card);
        bool seen = _fingerprints.TryGetValue(source.Id, out var previous);
        _fingerprints[source.Id] = fingerprint;
        if (card == null)
            return null;
        if (seen && previous == fingerprint)
            return null;
        if (IsRepeat(card, now))
            return null;

        _lastAnnounced = card;
        _lastAnnouncedAt = now;
        return card;
    }

    public void Forget(uint id) => _fingerprints.Remove(id);

    public void Retain(IReadOnlyCollection<uint> ids)
    {
        if (_fingerprints.Count == 0)
            return;
        List<uint>? remove = null;
        foreach (var id in _fingerprints.Keys)
        {
            if (ids.Contains(id))
                continue;
            remove ??= new List<uint>();
            remove.Add(id);
        }

        if (remove == null)
            return;
        foreach (var id in remove)
            _fingerprints.Remove(id);
    }

    bool IsRepeat(NotificationCard card, DateTime now)
    {
        if (_lastAnnounced is not NotificationCard previous)
            return false;
        if (now - _lastAnnouncedAt > NotificationPolicy.RepeatWindow)
            return false;
        return previous.AppName == card.AppName
            && previous.Title == card.Title
            && previous.Summary == card.Summary;
    }

    static string Fingerprint(NotificationCard? card) =>
        card == null ? "" : card.AppName + "\u001f" + card.Title + "\u001f" + card.Summary;
}

public static class NotificationSession
{
    public static IReadOnlyList<NotificationCard> Ingest(
        NotificationFeed feed,
        bool userEnabled,
        NotificationAccess access,
        IReadOnlyList<NotificationSource> current,
        DateTime now)
    {
        if (!NotificationAccessPolicy.MayListen(userEnabled, access))
            return Array.Empty<NotificationCard>();

        if (!feed.IsSeeded)
        {
            feed.Seed(current);
            return Array.Empty<NotificationCard>();
        }

        var found = new List<NotificationCard>();
        var ids = new HashSet<uint>();
        foreach (var item in current)
        {
            ids.Add(item.Id);
            var card = feed.Observe(item, now);
            if (card != null)
                found.Add(card);
        }

        feed.Retain(ids);
        return found;
    }
}
