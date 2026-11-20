using System.Runtime.InteropServices;
using DynamicIsland.Core.Motion;
using DynamicIsland.Visuals;

// Headless probe of public capture APIs on a synthetic stacked-window scene.
// This is a CI / runner observation, NOT proof of the user's real desktop.
// Exit 0 after a completed report even if every path is a black hole; non-zero only on harness failure.


static class Program
{
static int Main()
{
const int OverlayW = 240, OverlayH = 120;
const int BackColor = unchecked((int)0xFF31C46B);
const int OverlayColor = unchecked((int)0xFFCC2244);
const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11, SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
const int WS_POPUP = unchecked((int)0x80000000), WS_VISIBLE = 0x10000000;
const int WS_EX_LAYERED = 0x00080000, WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000;
const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
IntPtr HWND_TOPMOST = new(-1);
const uint LWA_ALPHA = 0x2;

var os = Environment.OSVersion.Version;
bool supports = GlassUnderlay.OsSupportsExcludeFromCapture(os);
Console.WriteLine($"UNDERLAY-PROBE os={os} supportsExcludeFromCapture={supports} 合成窗口叠层，不是用户真实桌面证明。");

short classAtom = 0;
IntPtr back = IntPtr.Zero, overlay = IntPtr.Zero;
var wndProc = (WndProc)((h, msg, w, l) =>
{
    if (msg == 0x000F) // WM_PAINT
    {
        IntPtr dc = BeginPaint(h, out var ps);
        int color = (int)GetWindowLongPtr(h, -21).ToInt64();
        if (color == 0) color = unchecked((int)0xFF888888);
        int bgr = ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF);
        IntPtr brush = CreateSolidBrush(bgr);
        FillRect(dc, ref ps.RcPaint, brush);
        DeleteObject(brush);
        EndPaint(h, ref ps);
        return IntPtr.Zero;
    }
    return DefWindowProc(h, msg, w, l);
});
GC.KeepAlive(wndProc);
try
{
    var wc = new WNDCLASSEXW
    {
        Size = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
        WndProc = wndProc,
        Instance = GetModuleHandle(null),
        ClassName = "GlassUnderlayCheckClass"
    };
    classAtom = RegisterClassEx(ref wc);
    if (classAtom == 0)
        throw new InvalidOperationException($"RegisterClassEx failed {Marshal.GetLastWin32Error()}");

    int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
    int x = Math.Max(40, screenW / 4), y = Math.Max(40, screenH / 4);

    back = CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "GlassUnderlayCheckClass", "UnderlayBack",
        WS_POPUP | WS_VISIBLE, x, y, OverlayW, OverlayH, IntPtr.Zero, IntPtr.Zero, wc.Instance, IntPtr.Zero);
    overlay = CreateWindowEx(WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        "GlassUnderlayCheckClass", "UnderlayFront",
        WS_POPUP | WS_VISIBLE, x, y, OverlayW, OverlayH, IntPtr.Zero, IntPtr.Zero, wc.Instance, IntPtr.Zero);
    if (back == IntPtr.Zero || overlay == IntPtr.Zero)
        throw new InvalidOperationException($"CreateWindowEx failed {Marshal.GetLastWin32Error()}");

    SetWindowLongPtr(back, -21, new IntPtr(BackColor));
    SetWindowLongPtr(overlay, -21, new IntPtr(OverlayColor));
    InvalidateRect(back, IntPtr.Zero, true);
    InvalidateRect(overlay, IntPtr.Zero, true);
    FillWindow(back, BackColor);
    if (!SetLayeredWindowAttributes(overlay, 0, 255, LWA_ALPHA))
        throw new InvalidOperationException($"SetLayeredWindowAttributes failed {Marshal.GetLastWin32Error()}");
    FillWindow(overlay, OverlayColor);
    SetWindowPos(overlay, HWND_TOPMOST, x, y, OverlayW, OverlayH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    Pump(8);
    Thread.Sleep(200);
    Pump(8);

    int[] plain = Capture(x, y, OverlayW, OverlayH, SRCCOPY);
    int[] noCaptureBlt = Capture(x, y, OverlayW, OverlayH, SRCCOPY); // same ROP; Win8+ layered is included either way
    int[] noCap = Capture(x, y, OverlayW, OverlayH, SRCCOPY & ~CAPTUREBLT);
    Report("bitblt-src", plain, OverlayColor, BackColor);
    Report("bitblt-nocaptureblt", noCap, OverlayColor, BackColor);

    bool affinity = false;
    if (supports)
    {
        affinity = SetWindowDisplayAffinity(overlay, WDA_EXCLUDEFROMCAPTURE);
        Console.WriteLine($"affinity-set={affinity} last-error={Marshal.GetLastWin32Error()}");
        if (affinity && GetWindowDisplayAffinity(overlay, out uint actual))
            Console.WriteLine($"affinity-read=0x{actual:X}");
        Pump(4);
        Thread.Sleep(200);
        Pump(4);
        int[] excluded = Capture(x, y, OverlayW, OverlayH, SRCCOPY);
        Report("bitblt-exclude", excluded, OverlayColor, BackColor);

        try
        {
            using var dda = new GlassDesktopDuplication();
            int[] dup = new int[OverlayW * OverlayH];
            bool ok = dda.TryCapture(x, y, OverlayW, OverlayH, dup, OverlayW);
            Console.WriteLine($"dda-acquire={ok}");
            if (ok)
                Report("dda-exclude", dup, OverlayColor, BackColor);
            else
                Console.WriteLine("dda-exclude result=unavailable (not a failure of the harness)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"dda-exclude result=error type={ex.GetType().Name}");
        }
    }
    else
        Console.WriteLine("skip-exclude: OS below 19041, would silently become WDA_MONITOR (black).");

    if (affinity)
        SetWindowDisplayAffinity(overlay, WDA_NONE);
}
finally
{
    if (overlay != IntPtr.Zero) { SetWindowDisplayAffinity(overlay, WDA_NONE); DestroyWindow(overlay); }
    if (back != IntPtr.Zero) DestroyWindow(back);
    if (classAtom != 0) UnregisterClass("GlassUnderlayCheckClass", GetModuleHandle(null));
}

Console.WriteLine("UNDERLAY-PROBE done. 结论仅反映本 runner 的合成叠层，未在用户 Windows 桌面证实。");
return 0;
}

static void Report(string label, int[] pixels, int overlay, int under)
{
    int mid = pixels[pixels.Length / 2];
    int overlayHits = 0, underHits = 0, blackHits = 0;
    foreach (int c in pixels)
    {
        int rgb = c & 0x00FFFFFF;
        if (rgb == (overlay & 0x00FFFFFF)) overlayHits++;
        else if (rgb == (under & 0x00FFFFFF)) underHits++;
        else if (((c >> 16) & 255) < 12 && ((c >> 8) & 255) < 12 && (c & 255) < 12) blackHits++;
    }
    string verdict = underHits > pixels.Length / 2 ? "UNDERLAY"
        : overlayHits > pixels.Length / 2 ? "OVERLAY"
        : blackHits > pixels.Length / 2 ? "BLACK"
        : "MIXED";
    Console.WriteLine($"{label} result={verdict} mid={mid:X8} overlay={overlayHits} underlay={underHits} black={blackHits} pixels={pixels.Length}");
}

static int[] Capture(int x, int y, int w, int h, uint rop)
{
    IntPtr screen = CreateDC("DISPLAY", null, null, IntPtr.Zero);
    if (screen == IntPtr.Zero) throw new InvalidOperationException("CreateDC(DISPLAY) failed");
    IntPtr dc = CreateCompatibleDC(screen);
    var info = new BITMAPINFO { Header = new BITMAPINFOHEADER
    {
        Size = Marshal.SizeOf<BITMAPINFOHEADER>(), Width = w, Height = -h, Planes = 1, BitCount = 32, Compression = 0
    }};
    IntPtr dib = CreateDIBSection(dc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
    IntPtr old = SelectObject(dc, dib);
    try
    {
        if (!BitBlt(dc, 0, 0, w, h, screen, x, y, rop))
            throw new InvalidOperationException($"BitBlt failed {Marshal.GetLastWin32Error()}");
        int[] dest = new int[w * h];
        Marshal.Copy(bits, dest, 0, dest.Length);
        return dest;
    }
    finally
    {
        SelectObject(dc, old);
        if (dib != IntPtr.Zero) DeleteObject(dib);
        DeleteDC(dc);
        DeleteDC(screen);
    }
}

static void FillWindow(IntPtr hwnd, int color)
{
    IntPtr dc = GetDC(hwnd);
    if (dc == IntPtr.Zero) return;
    int bgr = ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF);
    IntPtr brush = CreateSolidBrush(bgr);
    GetClientRect(hwnd, out RECT r);
    FillRect(dc, ref r, brush);
    DeleteObject(brush);
    ReleaseDC(hwnd, dc);
}

static void Pump(int max)
{
    for (int i = 0; i < max && PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, 1); i++)
    {
        TranslateMessage(ref msg);
        DispatchMessage(ref msg);
    }
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

[DllImport("user32.dll", EntryPoint = "DefWindowProcW")] static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")] static extern short RegisterClassEx(ref WNDCLASSEXW c);
[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")] static extern bool UnregisterClass(string n, IntPtr i);
[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
static extern IntPtr CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
[DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
[DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
[DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
[DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
[DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
[DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);
[DllImport("user32.dll", SetLastError = true)] static extern bool GetWindowDisplayAffinity(IntPtr h, out uint a);
[DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insert, int x, int y, int w, int ht, uint flags);
[DllImport("gdi32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateDC(string driver,string? device,string? output,IntPtr init);
[DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
[DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
[DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
[DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref RECT r, IntPtr brush);
[DllImport("user32.dll", EntryPoint = "PeekMessageW")] static extern bool PeekMessage(out MSG m, IntPtr h, uint min, uint max, uint remove);
[DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
[DllImport("user32.dll", EntryPoint = "DispatchMessageW")] static extern IntPtr DispatchMessage(ref MSG m);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? n);
[DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
[DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr s, uint o);
[DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
[DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
[DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
[DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);
[DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(int color);

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct WNDCLASSEXW
{
    public uint Size; public uint Style; public WndProc WndProc; public int ClsExtra; public int WndExtra;
    public IntPtr Instance, Icon, Cursor, Background; public string? MenuName, ClassName; public IntPtr IconSm;
}
[StructLayout(LayoutKind.Sequential)]
struct RECT { public int Left, Top, Right, Bottom; }
[StructLayout(LayoutKind.Sequential)]
struct PAINTSTRUCT
{
    public IntPtr Hdc; public int Erase; public RECT RcPaint;
    public int Restore, IncUpdate;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
}
[StructLayout(LayoutKind.Sequential)]
struct MSG { public IntPtr Hwnd; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }
[StructLayout(LayoutKind.Sequential)]
struct BITMAPINFOHEADER
{
    public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
}
[StructLayout(LayoutKind.Sequential)]
struct BITMAPINFO { public BITMAPINFOHEADER Header; }
}
