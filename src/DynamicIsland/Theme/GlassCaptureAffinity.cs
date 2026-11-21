using DynamicIsland.Core.Motion;
using DynamicIsland.Native;

namespace DynamicIsland.Visuals;

/// <summary>
/// 用公开的 SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) 把本窗从捕获中拿掉，以便拷到正下方那一层。
/// 低于 Windows 10 2004 时绝不设置该标志：系统会静默当成 WDA_MONITOR，捕获里是黑块。
/// </summary>
static class GlassCaptureAffinity
{
    public static bool OsSupportsExcludeFromCapture =>
        GlassUnderlay.OsSupportsExcludeFromCapture(Environment.OSVersion.Version);

    public static bool TrySet(IntPtr hwnd, bool exclude)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        if (exclude && !OsSupportsExcludeFromCapture)
            return false;
        uint value = exclude ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;
        if (!NativeMethods.SetWindowDisplayAffinity(hwnd, value))
            return false;
        if (!exclude)
            return true;
        // 再读回，避免失败时仍以为已经排除。
        if (!NativeMethods.GetWindowDisplayAffinity(hwnd, out uint actual))
            return true;
        return actual == NativeMethods.WDA_EXCLUDEFROMCAPTURE;
    }
}
