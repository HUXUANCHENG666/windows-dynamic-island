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
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _session;
    DispatcherTimer? _poll;
    int _artGeneration;
    int _refreshing;

    public MediaService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public NowPlaying Current { get; private set; } = NowPlaying.Empty;
    public bool Available { get; private set; }
    public event EventHandler? Changed;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        Available = true;
        _manager.CurrentSessionChanged += (_, _) => _dispatcher.BeginInvoke(BindCurrent);
        BindCurrent();
        _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _poll.Tick += (_, _) => Poll();
        _poll.Start();
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
        var session = _session;
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
        try { if (_session != null) await _session.TrySkipNextAsync(); }
        catch (Exception ex) { Log.Error("next", ex); }
    }

    public async Task PreviousAsync()
    {
        try { if (_session != null) await _session.TrySkipPreviousAsync(); }
        catch (Exception ex) { Log.Error("previous", ex); }
    }

    void Poll()
    {
        try
        {
            var current = _manager?.GetCurrentSession();
            if (!SameSession(current, _session))
                BindCurrent();
            else if (_session != null)
                RequestRefresh();
        }
        catch (Exception ex)
        {
            Log.Error("media-poll", ex);
        }
    }

    static bool SameSession(GlobalSystemMediaTransportControlsSession? a, GlobalSystemMediaTransportControlsSession? b)
    {
        if (a == null || b == null)
            return a == null && b == null;
        try
        {
            return string.Equals(a.SourceAppUserModelId, b.SourceAppUserModelId, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    void BindCurrent()
    {
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnSessionEvent;
            _session.PlaybackInfoChanged -= OnSessionEvent;
            _session.TimelinePropertiesChanged -= OnSessionEvent;
        }

        try
        {
            _session = _manager?.GetCurrentSession();
        }
        catch (Exception ex)
        {
            Log.Error("media-session", ex);
            _session = null;
        }

        if (_session == null)
        {
            _artGeneration++;
            Current = NowPlaying.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _session.MediaPropertiesChanged += OnSessionEvent;
        _session.PlaybackInfoChanged += OnSessionEvent;
        _session.TimelinePropertiesChanged += OnSessionEvent;
        RequestRefresh();
    }

    void OnSessionEvent(GlobalSystemMediaTransportControlsSession session, object args)
    {
        var captured = session;
        _dispatcher.BeginInvoke(() =>
        {
            if (!SameSession(captured, _session))
                return;
            RequestRefresh();
        });
    }

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
        var session = _session;
        if (session == null)
            return;

        var props = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var phase = PlaybackMap.Map(playback.PlaybackStatus.ToString());
        var controls = playback.Controls;
        string artKey = (props.Title ?? "") + "\u001f" + (props.Artist ?? "") + "\u001f" + (props.AlbumTitle ?? "");
        var updated = timeline.LastUpdatedTime.UtcDateTime;
        var next = new NowPlaying
        {
            Title = props.Title ?? "",
            Artist = props.Artist ?? "",
            AppId = SafeAppId(session),
            IsPlaying = phase == PlaybackPhase.Playing,
            IsOpen = phase != PlaybackPhase.None,
            CanToggle = controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
            CanNext = controls.IsNextEnabled,
            CanPrevious = controls.IsPreviousEnabled,
            Position = timeline.Position,
            Start = timeline.StartTime,
            End = timeline.EndTime,
            TimelineUpdatedUtc = updated == default ? DateTime.UtcNow : updated,
            Art = artKey == Current.ArtKey ? Current.Art : null,
            ArtKey = artKey
        };

        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);

        if (next.Art == null && props.Thumbnail != null && next.IsOpen)
            _ = LoadArtAsync(session, props.Thumbnail, artKey, ++_artGeneration);
    }

    static string SafeAppId(GlobalSystemMediaTransportControlsSession session)
    {
        try { return session.SourceAppUserModelId ?? ""; }
        catch { return ""; }
    }

    async Task LoadArtAsync(GlobalSystemMediaTransportControlsSession session, IRandomAccessStreamReference thumbnail, string artKey, int generation)
    {
        try
        {
            var bytes = await ReadThumbnailAsync(thumbnail).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0)
                return;
            await _dispatcher.InvokeAsync(() =>
            {
                if (generation != _artGeneration || !SameSession(session, _session) || Current.ArtKey != artKey)
                    return;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(bytes);
                image.DecodePixelWidth = 256;
                image.EndInit();
                image.Freeze();
                Current = Current with { Art = image };
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

    public void Dispose() => _poll?.Stop();
}
