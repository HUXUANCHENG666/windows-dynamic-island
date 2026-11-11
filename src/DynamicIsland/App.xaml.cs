using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core.Ipc;
using DynamicIsland.Services;
using DynamicIsland.Shell;

namespace DynamicIsland;

public partial class App : Application
{
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

        Log.Info("start");
        var settings = SettingsStore.Load();
        var window = new IslandWindow(settings);
        SingleInstance.StartServer(cmd => window.Dispatcher.BeginInvoke(() => window.HandleCommand(cmd), DispatcherPriority.Normal));
        var tray = new TrayController(window);
        window.AttachTray(tray);
        window.Show();
        _ = window.StartServicesAsync();
    }

    [DllImport("user32.dll")]
    static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
