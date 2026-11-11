using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core.Ipc;
using DynamicIsland.Core.Island;
using DynamicIsland.Services;
using DynamicIsland.Shell;

namespace DynamicIsland;

public partial class App : Application
{
    readonly object _gate = new();
    readonly List<ClientCommand> _pending = new();
    IslandWindow? _window;

    public App()
    {
        try
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
        }
        catch
        {
            // The manifest already requests PerMonitorV2. This is only a second attempt.
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var chinese = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.DefaultThreadCurrentCulture = chinese;
        CultureInfo.DefaultThreadCurrentUICulture = chinese;
        Thread.CurrentThread.CurrentCulture = chinese;
        Thread.CurrentThread.CurrentUICulture = chinese;

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("ui", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Error("fatal", ex);
        };

        var command = ClientCommand.ParseArgs(e.Args);
        if (!SingleInstance.TryBecomePrimary())
        {
            SingleInstance.Forward(command);
            Shutdown();
            return;
        }

        bool delay = StartupDelay.RequestsDelay(e.Args);
        Log.Info(delay ? "start delayed" : "start");
        SingleInstance.StartServer(Deliver);
        if (!delay)
        {
            Boot();
            return;
        }

        var timer = new DispatcherTimer { Interval = StartupDelay.LogonWait };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Boot();
        };
        timer.Start();
    }

    void Deliver(ClientCommand command)
    {
        IslandWindow? window;
        lock (_gate)
        {
            if (_window == null)
            {
                _pending.Add(command);
                return;
            }

            window = _window;
        }

        window.Dispatcher.BeginInvoke(() => window.HandleCommand(command), DispatcherPriority.Normal);
    }

    void Boot()
    {
        if (_window != null)
            return;
        var settings = SettingsStore.Load();
        var window = new IslandWindow(settings);
        var tray = new TrayController(window);
        window.AttachTray(tray);
        window.Show();
        List<ClientCommand> queued;
        lock (_gate)
        {
            _window = window;
            queued = new List<ClientCommand>(_pending);
            _pending.Clear();
        }

        foreach (var command in queued)
            window.HandleCommand(command);
        _ = window.StartServicesAsync();
        try
        {
            StartupRegistration.EnsureDelayedLogon();
        }
        catch (Exception ex)
        {
            Log.Error("startup", ex);
        }
    }

    [DllImport("user32.dll")]
    static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
