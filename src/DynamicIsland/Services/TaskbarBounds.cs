using DynamicIsland.Core.Island;
using DynamicIsland.Native;

namespace DynamicIsland.Services;

static class TaskbarBounds
{
    public static IReadOnlyList<PixelRect> Query()
    {
        var list = new List<PixelRect>();
        Add(list, NativeMethods.FindWindow("Shell_TrayWnd", null));
        IntPtr after = IntPtr.Zero;
        while (true)
        {
            after = NativeMethods.FindWindowEx(IntPtr.Zero, after, "Shell_SecondaryTrayWnd", null);
            if (after == IntPtr.Zero)
                break;
            Add(list, after);
        }

        return list;
    }

    static void Add(List<PixelRect> list, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
            return;
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width < 8 || height < 8)
            return;
        list.Add(new PixelRect(rect.Left, rect.Top, width, height));
    }
}
