using DynamicIsland.Native;

namespace DynamicIsland.Services;

/// <summary>
/// 前台、移动结束、最小化恢复，以及前台窗口自己的位置变化。
/// 位置变化很密，这里按 250ms 合并，不轮询。
/// </summary>
sealed class FullscreenWatcher : IDisposable
{
    readonly NativeMethods.WinEventProc _callback;
    IntPtr _sink;
    IntPtr _foregroundHook;
    IntPtr _moveEndHook;
    IntPtr _minimizeHook;
    IntPtr _moveHook;
    IntPtr _foreground;
    int _posted;
    uint _lastLocation;

    public FullscreenWatcher()
    {
        _callback = OnEvent;
    }

    public void Start(IntPtr sink)
    {
        _sink = sink;
        uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
        _foregroundHook = Hook(NativeMethods.EVENT_SYSTEM_FOREGROUND, flags);
        _moveEndHook = Hook(NativeMethods.EVENT_SYSTEM_MOVESIZEEND, flags);
        _minimizeHook = Hook(NativeMethods.EVENT_SYSTEM_MINIMIZEEND, flags);
        _moveHook = Hook(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, flags);
    }

    IntPtr Hook(uint eventType, uint flags) => NativeMethods.SetWinEventHook(
        eventType, eventType, IntPtr.Zero, _callback, 0, 0, flags);

    void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType == NativeMethods.EVENT_OBJECT_LOCATIONCHANGE)
        {
            var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root == IntPtr.Zero)
                root = hwnd;
            if (idObject != 0 || idChild != 0 || (hwnd != _foreground && root != _foreground) || unchecked(time - _lastLocation) < 250)
                return;
            _lastLocation = time;
        }
        else if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            _foreground = root != IntPtr.Zero ? root : hwnd;
        }
        else if (eventType is not (NativeMethods.EVENT_SYSTEM_MOVESIZEEND or NativeMethods.EVENT_SYSTEM_MINIMIZEEND))
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _posted, 1, 0) != 0)
            return;
        NativeMethods.PostMessage(_sink, NativeMethods.WM_APP_CHECKHIDE, IntPtr.Zero, IntPtr.Zero);
    }

    public void MarkDelivered() => Interlocked.Exchange(ref _posted, 0);

    public void Dispose()
    {
        Unhook(ref _foregroundHook);
        Unhook(ref _moveEndHook);
        Unhook(ref _minimizeHook);
        Unhook(ref _moveHook);
    }

    static void Unhook(ref IntPtr hook)
    {
        if (hook == IntPtr.Zero)
            return;
        NativeMethods.UnhookWinEvent(hook);
        hook = IntPtr.Zero;
    }
}
