namespace DynamicIsland.Core.Island;

/// <summary>
/// 登录自启动时先等一会儿，避开开机那一阵的磁盘和会话初始化。
/// 用户自己双击打开不走这条路径。
/// </summary>
public static class StartupDelay
{
    public const string Flag = "--delayed-start";
    public static readonly TimeSpan LogonWait = TimeSpan.FromSeconds(12);

    public static bool IsFlag(string? arg) =>
        string.Equals(arg, Flag, StringComparison.OrdinalIgnoreCase)
        || string.Equals(arg, "delayed-start", StringComparison.OrdinalIgnoreCase);

    public static bool RequestsDelay(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            if (IsFlag(arg))
                return true;
        }

        return false;
    }

    public static string LogonCommand(string exePath) => $"\"{exePath}\" {Flag}";
}
