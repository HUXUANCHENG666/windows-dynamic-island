using DynamicIsland.Native;

namespace DynamicIsland.Services;

/// <summary>
/// 只监听"有新窗口显示出来"这一件事，把窗口句柄丢回 UI 线程去认进程。
/// 不读窗口标题，不读别的进程内存，也不挂任何 DLL。
/// </summary>
sealed class PopupWatcher : IDisposable
{
    readonly NativeMethods.WinEventProc _callback;
    IntPtr _sink;
    IntPtr _hook;

    public PopupWatcher() => _callback = OnEvent;

    public bool IsRunning => _hook != IntPtr.Zero;

    public void Start(IntPtr sink)
    {
        if (_hook != IntPtr.Zero)
            return;
        _sink = sink;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_SHOW,
            NativeMethods.EVENT_OBJECT_SHOW,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
            return;
        NativeMethods.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // 只要窗口本身显示，不要控件和光标位置的变化。
        if (hwnd == IntPtr.Zero || idObject != 0 || idChild != 0)
            return;
        var sink = _sink;
        if (sink == IntPtr.Zero)
            return;
        NativeMethods.PostMessage(sink, NativeMethods.WM_APP_POPUP, IntPtr.Zero, hwnd);
    }

    public void Dispose()
    {
        Stop();
        _sink = IntPtr.Zero;
    }
}
