namespace DynamicIsland.Core.Island;

public enum TrayPointerAction
{
    None,
    OpenMenu,
    Primary
}

public enum TrayDismiss
{
    Deactivated,
    Outside,
    Escape,
    ItemChosen
}

/// <summary>
/// 托盘回调里一次右键经常连着送来 WM_RBUTTONUP 和 WM_CONTEXTMENU。
/// 两条都去开关菜单的话，刚弹出的那块会被第二条立刻关掉。
/// </summary>
public static class TrayMenuEvents
{
    public const int WmLButtonUp = 0x0202;
    public const int WmRButtonDown = 0x0204;
    public const int WmRButtonUp = 0x0205;
    public const int WmContextMenu = 0x007B;
    public const int WmMouseMove = 0x0200;
    public const int NinSelect = 0x0400;
    public const int NinKeySelect = 0x0401;
    public const int CoalesceWindowMs = 400;

    /// <summary>
    /// 解析 Shell_NotifyIcon 回调的 lParam。
    /// 版本 0 时 lParam 就是鼠标消息；版本 4 时低 16 位是消息，高 16 位是图标 id。
    /// 这里不看 wParam 里的坐标：版本 4 对 WM_CONTEXTMENU 的坐标没有定义，而且有符号 16 位装不下副屏。
    /// </summary>
    public static TrayPointerAction Decode(long lParam)
    {
        int full = unchecked((int)(lParam & 0xFFFFFFFF));
        int low = full & 0xFFFF;
        int code = full is WmLButtonUp or WmRButtonUp or WmContextMenu or NinSelect or NinKeySelect
            ? full
            : low;
        return code switch
        {
            WmRButtonUp or WmContextMenu => TrayPointerAction.OpenMenu,
            WmLButtonUp or NinSelect or NinKeySelect => TrayPointerAction.Primary,
            _ => TrayPointerAction.None
        };
    }

    /// <summary>
    /// 从未打开过时传入负数。刚打开的几百毫秒内再来一次打开请求，当成同一次右键，忽略。
    /// </summary>
    public static bool ShouldOpen(long millisecondsSinceLastOpen) =>
        millisecondsSinceLastOpen < 0 || millisecondsSinceLastOpen >= CoalesceWindowMs;
}

/// <summary>
/// 菜单要留下来，直到用户选了一项、按了 Esc，或点到外面。
/// 弹出当下系统会先把前台抢走（尤其是隐藏图标的浮层在关），这段时间里的 Deactivated 不能关菜单。
/// </summary>
public static class TrayMenuDismiss
{
    public const int OutsideGraceMs = 150;
    public const int DeactivateGraceMs = 800;

    public static bool ShouldDismiss(bool deactivateArmed, TrayDismiss reason, long ageMs) => reason switch
    {
        TrayDismiss.Escape or TrayDismiss.ItemChosen => true,
        TrayDismiss.Outside => ageMs >= OutsideGraceMs,
        TrayDismiss.Deactivated => deactivateArmed,
        _ => false
    };
}
