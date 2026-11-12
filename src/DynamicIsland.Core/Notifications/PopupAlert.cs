using DynamicIsland.Core.Island;

namespace DynamicIsland.Core.Notifications;

/// <summary>
/// 一个刚显示出来的窗口。只带识别用的信息——尺寸、窗口类名、所属进程，
/// 不包含窗口标题，也不包含任何正文。
/// </summary>
public readonly record struct ShownWindow(
    bool Visible,
    bool TopLevel,
    bool Shell,
    bool OwnProcess,
    int Width,
    int Height,
    string ClassName,
    string ProcessName);

/// <summary>
/// 关注的应用弹出新窗口时给个提醒。判断只看窗口形状和进程，所以不需要读内容，
/// 也不需要应用配合——微信、QQ、邮件客户端都走同一条路。
/// </summary>
public static class PopupAlert
{
    /// <summary>同一个程序连开几个窗口时，这段时间内只提醒一次。</summary>
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(4);

    /// <summary>比这更小的窗口多半是菜单、提示气泡或输入法的候选框。</summary>
    public const int MinWidth = 120;
    public const int MinHeight = 60;

    /// <summary>系统到处都会创建这些窗口，形状也像弹窗，但它们不是消息。</summary>
    static readonly string[] NoiseClasses =
    {
        "tooltips_class32",
        "#32768",
        "Menu",
        "DropDown",
        "ComboLBox",
        "IME",
        "MSCTFIME UI",
        "GDI+ Hook Window Class",
        "SysShadow",
        "ForegroundStaging",
        "TaskListThumbnailWnd"
    };

    public static bool IsNoiseClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return true;
        foreach (var noise in NoiseClasses)
        {
            if (string.Equals(className, noise, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool ShouldAlert(ShownWindow window, IReadOnlyCollection<string>? watched)
    {
        if (watched == null || watched.Count == 0)
            return false;
        if (!window.Visible || window.Shell || window.OwnProcess)
            return false;
        if (!window.TopLevel)
            return false;
        if (window.Width < MinWidth || window.Height < MinHeight)
            return false;
        if (IsNoiseClass(window.ClassName))
            return false;
        return HidePolicy.IsListed(watched, window.ProcessName);
    }
}

/// <summary>
/// 每个程序单独计时。一个程序连着弹好几个窗口时只提醒第一次，避免刷屏。
/// </summary>
public sealed class PopupFeed
{
    readonly Dictionary<string, DateTime> _last = new(StringComparer.Ordinal);

    public bool Allow(string? processName, DateTime now)
    {
        var key = HidePolicy.NormalizeProcess(processName);
        if (key.Length == 0)
            return false;
        if (_last.TryGetValue(key, out var last) && now - last < PopupAlert.RepeatWindow)
            return false;
        _last[key] = now;
        return true;
    }

    public void Clear() => _last.Clear();
}
