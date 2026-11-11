using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.Services;

/// <summary>
/// 用系统自己的启动接口打开通知来源。不把正文交给命令行，也不写进日志。
/// </summary>
static class NotificationActivator
{
    public static bool TryProtocol(string target)
    {
        if (!NotificationLaunchPlanner.IsAllowedProtocol(target))
            return false;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryApp(string appUserModelId)
    {
        if (!NotificationLaunchPlanner.IsSafeAppId(appUserModelId))
            return false;

        var id = appUserModelId.Trim();
        try
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManagerCom();
            int hr = manager.ActivateApplication(id, "", 0, out _);
            if (hr >= 0)
                return true;
        }
        catch
        {
            // 下面再试资源管理器的应用文件夹。失败只记状态，不记标识内容。
        }

        if (!id.Contains('!'))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:AppsFolder\\" + id,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenPrivacySettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Info(NotificationLog.Failed(ex.HResult));
        }
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    class ApplicationActivationManagerCom
    {
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            int options,
            out uint processId);
    }
}
