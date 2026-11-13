using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core.Formatting;
using DynamicIsland.Core.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

public sealed record NowPlaying
{
    public static NowPlaying Empty { get; } = new();
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string AppId { get; init; } = "";
    public bool IsPlaying { get; init; }
    public bool IsOpen { get; init; }
    public bool CanToggle { get; init; }
    public bool CanNext { get; init; }
    public bool CanPrevious { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public DateTime TimelineUpdatedUtc { get; init; }
    public BitmapSource? Art { get; init; }
    public string ArtKey { get; init; } = "";
    public int SessionIndex { get; init; }
    public int SessionCount { get; init; }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "正在播放" : Title;

    public string DisplayArtist
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Artist))
                return Artist;
            var app = TimeFormat.PrettyAppId(AppId);
            return string.IsNullOrWhiteSpace(app) ? "" : app;
        }
    }
}

sealed class MediaService : IDisposable
{
    readonly Dispatcher _dispatcher;
    readonly DisplayedSession _display = new();
    readonly List<Tracked> _tracked = new();
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    DispatcherTimer? _poll;
    int _artGeneration;
    int _refreshing;
    int _idCounter;
    DateTime _lastUnsupported;

    public MediaService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public NowPlaying Current { get; private set; } = NowPlaying.Empty;
    public bool Available { get; private set; }
    public event EventHandler? Changed;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        Available = true;
        _manager.CurrentSessionChanged += (_, _) => _dispatcher.BeginInvoke(RefreshRoster);
        _manager.SessionsChanged += (_, _) => _dispatcher.BeginInvoke(RefreshRoster);
        RefreshRoster();
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _poll.Tick += (_, _) => RefreshRoster();
        _poll.Start();
    }

    public void SelectRelative(int delta)
    {
        if (!_display.TrySwitch(delta))
            return;
        RequestRefresh();
    }

    public void ReleaseSessionPin()
    {
        if (_display.PinnedId == null)
            return;
        _display.ClearPin();
        RequestRefresh();
    }

    public void OptimisticToggle()
    {
        if (!Current.IsOpen)
            return;
        Current = Current with { IsPlaying = !Current.IsPlaying };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ToggleAsync()
    {
        var session = SelectedSession();
        if (session == null)
            return;
        try
        {
            await session.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            Log.Error("playpause", ex);
        }
    }

    public async Task NextAsync()
    {
        var session = SelectedSession();
        if (session == null)
        {
            Log.Info("next no-session");
            return;
        }

        try
        {
            if (!await session.TrySkipNextAsync())
                ReportUnsupported("next");
        }
        catch (Exception ex)
        {
            Log.Error("next", ex);
        }
    }

    public async Task PreviousAsync()
    {
        var session = SelectedSession();
        if (session == null)
        {
            Log.Info("previous no-session");
            return;
        }

        try
        {
            if (!await session.TrySkipPreviousAsync())
                ReportUnsupported("previous");
        }
        catch (Exception ex)
        {
            Log.Error("previous", ex);
        }
    }

    /// <summary>
    /// 有些会话只是"能显示、能暂停"，不支持上一首下一首（网页里的单个视频就是）。
    /// 失败时告诉岛一声，免得用户以为是没反应。
    /// </summary>
    void ReportUnsupported(string area)
    {
        var now = DateTime.UtcNow;
        if (now - _lastUnsupported < TimeSpan.FromSeconds(3))
            return;
        _lastUnsupported = now;
        Log.Info($"{area} unsupported");
        Unsupported?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Unsupported;

    void RefreshRoster()
    {
        if (_manager == null)
            return;
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        GlobalSystemMediaTransportControlsSession? current;
        try
        {
            sessions = _manager.GetSessions();
            current = _manager.GetCurrentSession();
        }
        catch (Exception ex)
        {
            Log.Error("media-sessions", ex);
            return;
        }

        var aumids = new List<string>(sessions.Count);
        foreach (var session in sessions)
            aumids.Add(SafeAppId(session));
        var ids = SessionIdentity.Assign(
            _tracked.Select(item => item.Id).ToList(),
            _tracked.Select(item => item.Aumid).ToList(),
            aumids,
            ref _idCounter);

        var next = new List<Tracked>(sessions.Count);
        var observations = new List<SessionObservation>(sessions.Count);
        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            bool playing = false;
            try
            {
                var status = session.GetPlaybackInfo().PlaybackStatus;
                playing = status is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing;
            }
            catch (Exception ex)
            {
                Log.Error("media-status", ex);
            }

            next.Add(new Tracked(session, ids[i], aumids[i]));
            observations.Add(new SessionObservation(ids[i], playing, IsSystemCurrent(session, current, aumids[i], aumids)));
        }

        Rewire(next);
        _tracked.Clear();
        _tracked.AddRange(next);
        _display.Update(observations);
        RequestRefresh();
    }

    void Rewire(List<Tracked> next)
    {
        foreach (var old in _tracked)
        {
            if (next.Any(item => ReferenceEquals(item.Session, old.Session)))
                continue;
            Unhook(old.Session);
        }

        foreach (var item in next)
        {
            if (_tracked.Any(old => ReferenceEquals(old.Session, item.Session)))
                continue;
            Hook(item.Session);
        }
    }

    void Hook(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged += OnSessionEvent;
        session.PlaybackInfoChanged += OnSessionEvent;
        session.TimelinePropertiesChanged += OnSessionEvent;
    }

    void Unhook(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged -= OnSessionEvent;
        session.PlaybackInfoChanged -= OnSessionEvent;
        session.TimelinePropertiesChanged -= OnSessionEvent;
    }

    void OnSessionEvent(GlobalSystemMediaTransportControlsSession session, object args) =>
        _dispatcher.BeginInvoke(RefreshRoster);

    void RequestRefresh()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
            return;
        _ = RefreshAsync();
    }

    async Task RefreshAsync()
    {
        try
        {
            do
            {
                Interlocked.Exchange(ref _refreshing, 0);
                await ReadSessionAsync().ConfigureAwait(true);
            }
            while (Interlocked.CompareExchange(ref _refreshing, 0, 0) == 1);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _refreshing, 0);
            Log.Error("media-refresh", ex);
        }
    }

    async Task ReadSessionAsync()
    {
        var selected = Selected();
        if (selected == null)
        {
            _artGeneration++;
            Current = NowPlaying.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var session = selected.Session;
        var props = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var phase = PlaybackMap.Map(playback.PlaybackStatus.ToString());
        var controls = playback.Controls;
        string artKey = (props.Title ?? "") + "\u001f" + (props.Artist ?? "") + "\u001f" + (props.AlbumTitle ?? "");
        var updated = timeline.LastUpdatedTime.UtcDateTime;
        bool sameArt = artKey == Current.ArtKey && Current.Art != null;
        var next = new NowPlaying
        {
            Title = props.Title ?? "",
            Artist = props.Artist ?? "",
            AppId = selected.Aumid,
            IsPlaying = phase == PlaybackPhase.Playing,
            IsOpen = phase != PlaybackPhase.None,
            CanToggle = controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
            CanNext = controls.IsNextEnabled,
            CanPrevious = controls.IsPreviousEnabled,
            Position = timeline.Position,
            Start = timeline.StartTime,
            End = timeline.EndTime,
            TimelineUpdatedUtc = updated == default ? DateTime.UtcNow : updated,
            Art = props.Thumbnail == null ? null : (sameArt ? Current.Art : Current.Art),
            ArtKey = artKey,
            SessionIndex = _display.Index,
            SessionCount = _display.Count
        };

        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);

        if (props.Thumbnail != null && next.IsOpen && !sameArt)
            _ = LoadArtAsync(session, selected.Id, props.Thumbnail, artKey, ++_artGeneration);
    }

    static bool IsSystemCurrent(
        GlobalSystemMediaTransportControlsSession session,
        GlobalSystemMediaTransportControlsSession? current,
        string aumid,
        IReadOnlyList<string> aumids)
    {
        if (current == null)
            return false;
        if (ReferenceEquals(session, current))
            return true;
        string currentAumid = SafeAppId(current);
        if (!string.Equals(aumid, currentAumid, StringComparison.Ordinal))
            return false;
        int count = 0;
        foreach (var item in aumids)
        {
            if (string.Equals(item, aumid, StringComparison.Ordinal))
                count++;
        }

        return count == 1;
    }

    Tracked? Selected()
    {
        var id = _display.CommandTarget;
        if (id == null)
            return null;
        foreach (var item in _tracked)
        {
            if (item.Id == id)
                return item;
        }

        return null;
    }

    GlobalSystemMediaTransportControlsSession? SelectedSession() => Selected()?.Session;

    static string SafeAppId(GlobalSystemMediaTransportControlsSession session)
    {
        try { return session.SourceAppUserModelId ?? ""; }
        catch { return ""; }
    }

    async Task LoadArtAsync(GlobalSystemMediaTransportControlsSession session, string sessionId, IRandomAccessStreamReference thumbnail, string artKey, int generation)
    {
        try
        {
            var bytes = await ReadThumbnailAsync(thumbnail).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0)
                return;
            await _dispatcher.InvokeAsync(() =>
            {
                var selected = Selected();
                if (generation != _artGeneration || selected == null || selected.Id != sessionId || !ReferenceEquals(selected.Session, session))
                    return;

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(bytes);
                image.DecodePixelWidth = 256;
                image.EndInit();
                image.Freeze();
                if (selected.Id != _display.CommandTarget)
                    return;
                Current = Current with { Art = image, ArtKey = artKey };
                Changed?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception ex)
        {
            Log.Error("artwork", ex);
        }
    }

    static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference thumbnail)
    {
        var stream = await thumbnail.OpenReadAsync();
        try
        {
            uint size = (uint)Math.Min(stream.Size, 12 * 1024 * 1024);
            if (size == 0)
                return null;
            var reader = new DataReader(stream.GetInputStreamAt(0));
            try
            {
                await reader.LoadAsync(size);
                var bytes = new byte[size];
                reader.ReadBytes(bytes);
                return bytes;
            }
            finally
            {
                reader.Dispose();
            }
        }
        finally
        {
            stream.Dispose();
        }
    }

    public void Dispose()
    {
        _poll?.Stop();
        foreach (var item in _tracked)
            Unhook(item.Session);
        _tracked.Clear();
    }

    sealed class Tracked(GlobalSystemMediaTransportControlsSession session, string id, string aumid)
    {
        public GlobalSystemMediaTransportControlsSession Session { get; } = session;
        public string Id { get; } = id;
        public string Aumid { get; } = aumid;
    }
}
