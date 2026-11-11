using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core.Formatting;
using DynamicIsland.Core.Island;
using DynamicIsland.Services;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class ExpandedMediaView : UserControl
{
    public event EventHandler? PlayPauseClicked;
    public event EventHandler? NextClicked;
    public event EventHandler? PreviousClicked;
    public event EventHandler? PreviousSessionClicked;
    public event EventHandler? NextSessionClicked;
    TimeSpan _position;
    TimeSpan _duration;
    bool _hasTimeline;
    ImageSource? _art;
    ImageSource? _painted;
    double _slot = MediaArtLayout.HorizontalSize;

    public ExpandedMediaView()
    {
        InitializeComponent();
        var ui = new FontFamily(IslandFonts.Ui);
        Title.FontFamily = ui;
        Artist.FontFamily = ui;
        Elapsed.FontFamily = ui;
        Duration.FontFamily = ui;
        Title.Foreground = Theme.Title;
        Artist.Foreground = Theme.Artist;
        Elapsed.Foreground = Theme.Artist;
        Duration.Foreground = Theme.Quiet;
        TrackBack.Background = Theme.Track;
        Progress.Background = Theme.Title;
        Art.Fill = Theme.Placeholder;
        NotePath.Data = Geometry.Parse(Glyphs.Music);
        NotePath.Fill = Theme.Quiet;
        PreviousIcon.Data = Geometry.Parse(Glyphs.Previous);
        NextIcon.Data = Geometry.Parse(Glyphs.Next);
        PreviousIcon.Fill = Theme.Title;
        NextIcon.Fill = Theme.Title;
        PlayIcon.Fill = Theme.Ink;
        Play.Background = Theme.Title;
        Ui.Hover(Previous, Brushes.Transparent, Theme.Hover);
        Ui.Hover(Next, Brushes.Transparent, Theme.Hover);
        Previous.MouseLeftButtonUp += (_, e) => Raise(PreviousClicked, e);
        Next.MouseLeftButtonUp += (_, e) => Raise(NextClicked, e);
        Play.MouseLeftButtonUp += (_, e) => Raise(PlayPauseClicked, e);
        Play.Cursor = Cursors.Hand;
        Play.Focusable = false;
        PrevSessionIcon.Data = Geometry.Parse(Glyphs.ChevronLeft);
        NextSessionIcon.Data = Geometry.Parse(Glyphs.ChevronRight);
        PrevSessionIcon.Stroke = Theme.Title;
        NextSessionIcon.Stroke = Theme.Title;
        SessionCountText.FontFamily = ui;
        SessionCountText.Foreground = Theme.Title;
        var veil = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        veil.Freeze();
        PrevSession.Background = veil;
        NextSession.Background = veil;
        SessionBadge.Background = veil;
        PrevSession.Cursor = Cursors.Hand;
        NextSession.Cursor = Cursors.Hand;
        PrevSession.Focusable = false;
        NextSession.Focusable = false;
        PrevSession.MouseLeftButtonUp += (_, e) => Raise(PreviousSessionClicked, e);
        NextSession.MouseLeftButtonUp += (_, e) => Raise(NextSessionClicked, e);
        PreviewMouseWheel += (_, e) =>
        {
            if (Switcher.Visibility != Visibility.Visible)
                return;
            e.Handled = true;
            Raise(e.Delta > 0 ? PreviousSessionClicked : NextSessionClicked, e);
        };
        Track.SizeChanged += (_, _) => ApplyProgress();
        AutomationProperties.SetName(Play, "播放或暂停");
        AutomationProperties.SetName(Previous, "上一首");
        AutomationProperties.SetName(Next, "下一首");
    }

    public void Apply(NowPlaying now)
    {
        Ui.Text(Title, now.DisplayTitle);
        Ui.Text(Artist, string.IsNullOrWhiteSpace(now.DisplayArtist) ? " " : now.DisplayArtist);
        PlayIcon.Data = Geometry.Parse(now.IsPlaying ? Glyphs.Pause : Glyphs.Play);
        SetEnabled(Previous, now.CanPrevious);
        SetEnabled(Next, now.CanNext);
        SetEnabled(Play, now.CanToggle || now.IsOpen);
        _art = now.Art;
        PaintArt();
        bool several = now.SessionCount > 1 && now.SessionIndex > 0;
        Switcher.Visibility = several ? Visibility.Visible : Visibility.Collapsed;
        if (several)
            Ui.Text(SessionCountText, now.SessionIndex + "/" + now.SessionCount);
    }

    void PaintArt()
    {
        if (_art == null)
        {
            _painted = null;
            Art.Fill = Theme.Placeholder;
            Note.Visibility = Visibility.Visible;
            return;
        }

        if (ReferenceEquals(_art, _painted))
        {
            Note.Visibility = Visibility.Collapsed;
            return;
        }

        _painted = _art;
        Art.Fill = ArtBrush.Create(_art, _slot, _slot);
        Note.Visibility = Visibility.Collapsed;
    }

    public void SetProgress(TimeSpan position, TimeSpan duration, bool hasTimeline)
    {
        _position = position;
        _duration = duration;
        _hasTimeline = hasTimeline;
        ApplyProgress();
    }

    void ApplyProgress()
    {
        ProgressRow.Visibility = _hasTimeline ? Visibility.Visible : Visibility.Collapsed;
        if (!_hasTimeline)
            return;
        Ui.Text(Elapsed, TimeFormat.Media(_position));
        Ui.Text(Duration, TimeFormat.Media(_duration));
        double width = Track.ActualWidth * TimeFormat.Progress(_position, _duration);
        if (double.IsNaN(width) || width < 0)
            width = 0;
        Progress.Width = width;
    }

    static void SetEnabled(Border border, bool enabled)
    {
        border.Opacity = enabled ? 1 : 0.32;
        border.IsHitTestVisible = enabled;
    }

    public void SetVertical(bool vertical)
    {
        var spec = MediaArtLayout.For(vertical ? IslandOrientation.Vertical : IslandOrientation.Horizontal);
        _slot = spec.ArtSize;
        _painted = null;
        if (vertical)
        {
            TopRow.Height = new GridLength(1, GridUnitType.Star);
            Head.RowDefinitions.Clear();
            Head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Head.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            Head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            TopRow.Height = new GridLength(spec.ArtSize);
            Head.RowDefinitions.Clear();
        }

        ArtCol.Width = spec.CenterArt
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(spec.ArtSize);
        GapCol.Width = spec.TrailingColumnCollapsed ? new GridLength(0) : new GridLength(14);
        MetaCol.Width = spec.TrailingColumnCollapsed
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(ArtHost, spec.ArtColumn);
        Grid.SetColumnSpan(ArtHost, spec.ArtSpan);
        Grid.SetRow(ArtHost, spec.ArtRow);
        Grid.SetColumn(Meta, spec.MetaColumn);
        Grid.SetColumnSpan(Meta, spec.MetaSpan);
        Grid.SetRow(Meta, spec.MetaRow);
        ArtHost.Width = spec.ArtSize;
        ArtHost.Height = spec.ArtSize;
        ArtHost.HorizontalAlignment = spec.CenterArt ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        ArtHost.VerticalAlignment = spec.CenterArt ? VerticalAlignment.Center : VerticalAlignment.Top;
        Title.TextAlignment = spec.CenterArt ? TextAlignment.Center : TextAlignment.Left;
        Artist.TextAlignment = spec.CenterArt ? TextAlignment.Center : TextAlignment.Left;
        Title.HorizontalAlignment = spec.CenterArt ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        Artist.HorizontalAlignment = spec.CenterArt ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        PaintArt();
    }

    static void Raise(EventHandler? handler, MouseButtonEventArgs e)
    {
        e.Handled = true;
        handler?.Invoke(null, EventArgs.Empty);
    }

    static void Raise(EventHandler? handler, MouseWheelEventArgs e)
    {
        e.Handled = true;
        handler?.Invoke(null, EventArgs.Empty);
    }
}
