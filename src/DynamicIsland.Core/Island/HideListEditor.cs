namespace DynamicIsland.Core.Island;

public enum HideListOutcome
{
    Added,
    Removed,
    AlreadyListed,
    NotListed,
    Rejected
}

/// <summary>
/// 设置窗口里的隐身名单：用户输入或点选的名字，先收成和前台进程同一套写法，再加入或移出。
/// 加入后是否收成小点，仍由 HidePolicy.IsOccluded 决定。
/// </summary>
public static class HideListEditor
{
    public const int MaxNameLength = 64;
    public const int MaxCandidates = 12;

    public static bool CanList(string? process)
    {
        var name = HidePolicy.NormalizeProcess(process);
        if (name.Length == 0 || name.Length > MaxNameLength)
            return false;
        if (name is "dynamicisland" or "idle" or "system")
            return false;
        if (IsShellHost(name))
            return false;
        bool hasToken = false;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
                hasToken = true;
            else if (c is '.' or '_' or '-')
                continue;
            else
                return false;
        }

        return hasToken;
    }

    public static HideListOutcome Add(IList<string> list, string process)
    {
        if (!CanList(process))
            return HideListOutcome.Rejected;
        var name = HidePolicy.NormalizeProcess(process);
        if (HidePolicy.IsListed(list, name))
            return HideListOutcome.AlreadyListed;
        list.Add(name);
        return HideListOutcome.Added;
    }

    public static HideListOutcome Remove(IList<string> list, string process)
    {
        var name = HidePolicy.NormalizeProcess(process);
        if (name.Length == 0)
            return HideListOutcome.Rejected;
        if (!HidePolicy.IsListed(list, name))
            return HideListOutcome.NotListed;
        HidePolicy.ToggleListed(list, name);
        return HideListOutcome.Removed;
    }

    public static IReadOnlyList<string> Candidates(IEnumerable<string?> running, IEnumerable<string> listed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var raw in running)
        {
            if (!CanList(raw))
                continue;
            var name = HidePolicy.NormalizeProcess(raw);
            if (!seen.Add(name))
                continue;
            if (HidePolicy.IsListed(listed, name))
                continue;
            result.Add(name);
        }

        result.Sort(StringComparer.Ordinal);
        if (result.Count > MaxCandidates)
            result.RemoveRange(MaxCandidates, result.Count - MaxCandidates);
        return result;
    }

    static bool IsShellHost(string name) => name is
        "applicationframehost" or
        "textinputhost" or
        "searchhost" or
        "startmenuexperiencehost" or
        "shellexperiencehost" or
        "systemsettings" or
        "lockapp" or
        "dwm" or
        "sihost" or
        "runtimebroker" or
        "dllhost" or
        "conhost" or
        "fontdrvhost" or
        "widgetservice" or
        "widgets";
}
