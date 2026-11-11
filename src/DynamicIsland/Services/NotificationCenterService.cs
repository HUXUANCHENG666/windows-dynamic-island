using System.Windows.Threading;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using DynamicIsland.Core.Notifications;

namespace DynamicIsland.Services;

/// <summary>
/// 只读已经进了 Windows 通知中心的通知。用户允许之前不会调用 GetNotifications。
/// 正文留在内存里做映射，不写日志，也不上网。
/// </summary>
sealed class NotificationCenterService : IDisposable
{
    readonly Dispatcher _dispatcher;
    readonly NotificationFeed _feed = new();
    readonly Dictionary<uint, AppInfo> _apps = new();
    UserNotificationListener? _listener;
    TypedEventHandler<UserNotificationListener, UserNotificationChangedEventArgs>? _handler;
    DispatcherTimer? _poll;
    bool _listenerFailed;
    bool _running;
    bool _pollBusy;
    bool _disposed;
    int _lastFailure;

    public NotificationCenterService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public event EventHandler<NotificationCard>? Arrived;
    public event EventHandler<NotificationAccess>? AccessLost;

    public bool IsRunning => _running;

    public NotificationAccess ReadAccess()
    {
        var listener = TryListener();
        if (listener == null)
            return NotificationAccess.Unavailable;
        try
        {
            return Map(listener.GetAccessStatus());
        }
        catch (Exception ex)
        {
            Log.Info(NotificationLog.Failed(ex.HResult));
            return NotificationAccess.Unavailable;
        }
    }

    public async Task<NotificationAccess> RequestAccessAsync()
    {
        var listener = TryListener();
        if (listener == null)
            return NotificationAccess.Unavailable;
        try
        {
            return Map(await listener.RequestAccessAsync());
        }
        catch (Exception ex)
        {
            Log.Info(NotificationLog.Failed(ex.HResult));
            return NotificationAccess.Unavailable;
        }
    }

    public void Start()
    {
        if (_disposed || _running)
            return;
        if (!NotificationAccessPolicy.MayListen(true, ReadAccess()))
            return;

        _running = true;
        _feed.Reset();
        Log.Info(NotificationLog.Line(NotificationLogEvent.Listening));
        _ = StartAsync();
    }

    public void Stop()
    {
        bool was = _running;
        _running = false;
        _poll?.Stop();
        Detach();
        _feed.Reset();
        _apps.Clear();
        if (was)
            Log.Info(NotificationLog.Line(NotificationLogEvent.Stopped));
    }

    public async Task<bool> OpenAsync(NotificationCard card)
    {
        try
        {
            if (card.Launch.Kind == NotificationLaunchKind.Protocol
                && NotificationActivator.TryProtocol(card.Launch.Target))
            {
                Log.Info(NotificationLog.Line(NotificationLogEvent.Opened));
                return true;
            }

            if (_apps.TryGetValue(card.Id, out var info) && await TryLaunchPackageAsync(info))
            {
                Log.Info(NotificationLog.Line(NotificationLogEvent.Opened));
                return true;
            }

            if (card.Launch.Kind == NotificationLaunchKind.App && NotificationActivator.TryApp(card.Launch.Target))
            {
                Log.Info(NotificationLog.Line(NotificationLogEvent.Opened));
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Info(NotificationLog.Failed(ex.HResult));
        }

        Log.Info(NotificationLog.Line(NotificationLogEvent.OpenFailed));
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }

    async Task StartAsync()
    {
        await PollAsync();
        if (!_running)
            return;
        EnsureTimer();
    }

    void EnsureTimer()
    {
        _poll ??= new DispatcherTimer { Interval = NotificationPolicy.PollInterval };
        _poll.Tick -= OnPoll;
        _poll.Tick += OnPoll;
        _poll.Start();
        Log.Info(NotificationLog.Line(NotificationLogEvent.Poll));
    }

    void OnPoll(object? sender, EventArgs e) => _ = PollAsync();

    async Task PollAsync()
    {
        if (!_running || _pollBusy || _disposed)
            return;
        _pollBusy = true;
        try
        {
            var access = ReadAccess();
            if (!NotificationAccessPolicy.MayListen(true, access))
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    if (!_running)
                        return;
                    Stop();
                    AccessLost?.Invoke(this, access);
                });
                return;
            }

            var listener = _listener;
            if (listener == null)
                return;

            var list = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var sources = new List<NotificationSource>(list.Count);
            var apps = new List<(uint Id, AppInfo? Info)>(list.Count);
            foreach (var item in list)
            {
                try
                {
                    if (!TryMap(item, out var source))
                        continue;
                    sources.Add(source);
                    AppInfo? info = null;
                    try { info = item.AppInfo; } catch { /* 有的来源没有应用信息。 */ }
                    apps.Add((item.Id, info));
                }
                catch
                {
                    // 单条坏了不影响其余通知。不要把正文带进异常日志。
                }
            }

            await _dispatcher.InvokeAsync(() => Deliver(sources, apps));
            _lastFailure = 0;
        }
        catch (Exception ex)
        {
            if (ex.HResult != _lastFailure)
            {
                _lastFailure = ex.HResult;
                Log.Info(NotificationLog.Failed(ex.HResult));
            }
        }
        finally
        {
            _pollBusy = false;
        }
    }

    void Deliver(List<NotificationSource> sources, List<(uint Id, AppInfo? Info)> apps)
    {
        if (!_running)
            return;

        bool seeded = _feed.IsSeeded;
        var cards = NotificationSession.Ingest(_feed, true, NotificationAccess.Allowed, sources, DateTime.UtcNow);
        _apps.Clear();
        foreach (var (id, info) in apps)
        {
            if (info != null)
                _apps[id] = info;
        }

        if (!seeded && _feed.IsSeeded)
        {
            Log.Info(NotificationLog.Line(NotificationLogEvent.Baseline));
            TrySubscribe();
        }

        foreach (var card in cards)
        {
            Log.Info(NotificationLog.Arrival(card));
            Arrived?.Invoke(this, card);
        }
    }

    void TrySubscribe()
    {
        if (_listener == null || _handler != null)
            return;
        _handler = OnChanged;
        try
        {
            _listener.NotificationChanged += _handler;
            Log.Info(NotificationLog.Line(NotificationLogEvent.Events));
        }
        catch (Exception ex)
        {
            _handler = null;
            Log.Info(NotificationLog.Failed(ex.HResult));
        }
    }

    void OnChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        var kind = args.ChangeKind;
        var id = args.UserNotificationId;
        _dispatcher.BeginInvoke(() => HandleChange(kind, id));
    }

    void HandleChange(UserNotificationChangedKind kind, uint id)
    {
        if (!_running || _listener == null)
            return;
        if (kind == UserNotificationChangedKind.Removed)
        {
            _feed.Forget(id);
            _apps.Remove(id);
            return;
        }

        UserNotification notification;
        try
        {
            notification = _listener.GetNotification(id);
        }
        catch (Exception ex)
        {
            Log.Info(NotificationLog.Failed(ex.HResult));
            return;
        }

        if (notification == null || !TryMap(notification, out var source))
            return;

        try
        {
            if (notification.AppInfo != null)
                _apps[id] = notification.AppInfo;
        }
        catch
        {
            // 没有应用信息时，点击会退回到应用标识。
        }

        var card = _feed.Observe(source, DateTime.UtcNow);
        if (card == null)
            return;
        Log.Info(NotificationLog.Arrival(card));
        Arrived?.Invoke(this, card);
    }

    void Detach()
    {
        if (_listener != null && _handler != null)
        {
            try { _listener.NotificationChanged -= _handler; }
            catch { /* 退订失败也不留下正文。 */ }
        }

        _handler = null;
    }

    UserNotificationListener? TryListener()
    {
        if (_listenerFailed)
            return null;
        if (_listener != null)
            return _listener;
        try
        {
            _listener = UserNotificationListener.Current;
            return _listener;
        }
        catch (Exception ex)
        {
            _listenerFailed = true;
            Log.Info(NotificationLog.Failed(ex.HResult));
            return null;
        }
    }

    static async Task<bool> TryLaunchPackageAsync(AppInfo info)
    {
        try
        {
            var package = info.Package;
            if (package == null)
                return false;

            IReadOnlyList<AppListEntry> entries;
            try
            {
                entries = package.GetAppListEntries();
            }
            catch
            {
                return false;
            }

            string? wanted = null;
            try { wanted = info.AppUserModelId; } catch { /* 标识读不到就按列表顺序试。 */ }

            var ordered = new List<AppListEntry>(entries.Count);
            foreach (var entry in entries)
            {
                string? entryId = null;
                try { entryId = entry.AppUserModelId; } catch { /* 继续试下一项。 */ }
                if (wanted != null && string.Equals(entryId, wanted, StringComparison.OrdinalIgnoreCase))
                    ordered.Insert(0, entry);
                else
                    ordered.Add(entry);
            }

            foreach (var entry in ordered)
            {
                try
                {
                    if (await entry.LaunchAsync())
                        return true;
                }
                catch
                {
                    // 这一项打不开，试下一个入口。
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    static bool TryMap(UserNotification notification, out NotificationSource source)
    {
        source = default;
        if (notification == null)
            return false;

        string? app = null;
        string? aumid = null;
        try { app = notification.AppInfo?.DisplayInfo?.DisplayName; } catch { /* 显示名缺失时用标题。 */ }
        try { aumid = notification.AppInfo?.AppUserModelId; } catch { /* 没有标识就只能显示，点不开。 */ }

        string? title = null;
        var details = new List<string>();
        try
        {
            var visual = notification.Notification?.Visual;
            NotificationBinding? binding = null;
            if (visual != null)
            {
                binding = visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                if (binding == null && visual.Bindings is { Count: > 0 })
                    binding = visual.Bindings[0];
            }

            if (binding != null)
            {
                var texts = binding.GetTextElements();
                for (int i = 0; i < texts.Count; i++)
                {
                    string? text = null;
                    try { text = texts[i]?.Text; } catch { /* 这一段文字读失败就跳过。 */ }
                    if (string.IsNullOrWhiteSpace(text))
                        continue;
                    if (title == null)
                        title = text;
                    else
                        details.Add(text);
                }
            }
        }
        catch
        {
            // 视觉部分坏了就只显示应用名。
        }

        source = new NotificationSource(notification.Id, app, title, details, null, aumid);
        return true;
    }

    static NotificationAccess Map(UserNotificationListenerAccessStatus status) => status switch
    {
        UserNotificationListenerAccessStatus.Allowed => NotificationAccess.Allowed,
        UserNotificationListenerAccessStatus.Denied => NotificationAccess.Denied,
        UserNotificationListenerAccessStatus.Unspecified => NotificationAccess.Unspecified,
        _ => NotificationAccess.Unknown
    };
}
