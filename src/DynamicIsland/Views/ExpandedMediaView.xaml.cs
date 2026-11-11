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
    TimeSpan _position;
    TimeSpan _duration;
    bool _hasTimeline;

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
        if (now.Art == null)
        {
            Art.Fill = Theme.Placeholder;
            Note.Visibility = Visibility.Visible;
        }
        else
        {
            Art.Fill = new ImageBrush(now.Art) { Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(Art, BitmapScalingMode.HighQuality);
            Note.Visibility = Visibility.Collapsed;
        }
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
        if (vertical)
        {
            TopRow.Height = new GridLength(1, GridUnitType.Star);
            Head.RowDefinitions.Clear();
            Head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Head.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            Head.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ArtCol.Width = new GridLength(1, GridUnitType.Star);
            GapCol.Width = new GridLength(0);
            Grid.SetColumn(ArtHost, 0);
            Grid.SetRow(ArtHost, 0);
            Grid.SetColumn(Meta, 0);
            Grid.SetRow(Meta, 2);
            ArtHost.Width = 132;
            ArtHost.Height = 132;
            ArtHost.HorizontalAlignment = HorizontalAlignment.Center;
            Title.TextAlignment = TextAlignment.Center;
            Artist.TextAlignment = TextAlignment.Center;
            Title.HorizontalAlignment = HorizontalAlignment.Center;
            Artist.HorizontalAlignment = HorizontalAlignment.Center;
            return;
        }

        TopRow.Height = new GridLength(104);
        Head.RowDefinitions.Clear();
        ArtCol.Width = new GridLength(104);
        GapCol.Width = new GridLength(14);
        Grid.SetColumn(ArtHost, 0);
        Grid.SetRow(ArtHost, 0);
        Grid.SetColumn(Meta, 2);
        Grid.SetRow(Meta, 0);
        ArtHost.Width = 104;
        ArtHost.Height = 104;
        ArtHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        Title.TextAlignment = TextAlignment.Left;
        Artist.TextAlignment = TextAlignment.Left;
        Title.HorizontalAlignment = HorizontalAlignment.Stretch;
        Artist.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    static void Raise(EventHandler? handler, MouseButtonEventArgs e)
    {
        e.Handled = true;
        handler?.Invoke(null, EventArgs.Empty);
    }
}
