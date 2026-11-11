using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.SystemState;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class NoticeView : UserControl
{
    double _volumeLevel = -1;

    public NoticeView()
    {
        InitializeComponent();
        var ui = new FontFamily(IslandFonts.Ui);
        Title.FontFamily = ui;
        Detail.FontFamily = ui;
        AppName.FontFamily = ui;
        Percent.FontFamily = ui;
        Title.Foreground = Theme.Title;
        Detail.Foreground = Theme.Artist;
        AppName.Foreground = Theme.Quiet;
        Percent.Foreground = Theme.Title;
        BarTrack.Background = Theme.Track;
        BarFill.Background = Theme.Title;
        SpeakerBody.Data = Geometry.Parse(Glyphs.Speaker);
        SpeakerBody.Fill = Theme.Title;
        SpeakerWave.Data = Geometry.Parse(Glyphs.SpeakerWave);
        SpeakerWave.Stroke = Theme.Title;
        SpeakerSlash.Data = Geometry.Parse(Glyphs.Slash);
        SpeakerSlash.Stroke = Theme.Title;
        BellPath.Data = Geometry.Parse(Glyphs.Bell);
        BellPath.Fill = Theme.Title;
        BarHost.SizeChanged += (_, _) => ApplyVolumeBar();
    }

    public void RefreshBar() => ApplyVolumeBar();

    void ApplyVolumeBar()
    {
        if (_volumeLevel < 0 || BarHost.ActualWidth <= 0)
            return;
        BarFill.Width = Math.Max(0, BarHost.ActualWidth * _volumeLevel);
    }

    public void SetVolume(VolumeReading reading)
    {
        HideApp();
        ShowIcon(speaker: true, battery: false, bell: false);
        SpeakerSlash.Visibility = reading.Muted ? Visibility.Visible : Visibility.Collapsed;
        SpeakerWave.Visibility = reading.Muted ? Visibility.Collapsed : Visibility.Visible;
        Ui.Text(Title, reading.Muted ? "静音" : "音量");
        Ui.Text(Percent, reading.Percent + "%");
        Detail.Visibility = Visibility.Collapsed;
        BarHost.Visibility = Visibility.Visible;
        _volumeLevel = reading.Muted ? 0 : Math.Clamp(reading.Level, 0, 1);
        ApplyVolumeBar();
    }

    public void SetBattery(PowerAnnouncement announcement)
    {
        HideApp();
        ShowIcon(speaker: false, battery: true, bell: false);
        Battery.Set(announcement.Percent, announcement.Charging);
        Ui.Text(Title, announcement.Title);
        Ui.Text(Detail, announcement.Detail);
        Percent.Text = "";
        Detail.Visibility = Visibility.Visible;
        BarHost.Visibility = Visibility.Collapsed;
    }

    public void SetToast(string title, string body) => SetIncoming("", title, body);

    public void SetIncoming(string app, string title, string summary)
    {
        ShowIcon(speaker: false, battery: false, bell: true);
        bool hasApp = !string.IsNullOrWhiteSpace(app);
        AppName.Visibility = hasApp ? Visibility.Visible : Visibility.Collapsed;
        if (hasApp)
            Ui.Text(AppName, app);
        Ui.Text(Title, string.IsNullOrWhiteSpace(title) ? "通知" : title);
        Ui.Text(Detail, summary);
        Percent.Text = "";
        Detail.Visibility = string.IsNullOrWhiteSpace(summary) ? Visibility.Collapsed : Visibility.Visible;
        BarHost.Visibility = Visibility.Collapsed;
        _volumeLevel = -1;
    }

    public void SetVertical(bool vertical)
    {
        if (vertical)
        {
            IconCol.Width = new GridLength(1, GridUnitType.Star);
            TextCol.Width = new GridLength(1, GridUnitType.Star);
            IconRow.Height = GridLength.Auto;
            Grid.SetColumn(IconHost, 0);
            Grid.SetRow(IconHost, 0);
            Grid.SetColumnSpan(IconHost, 2);
            Grid.SetColumn(Copy, 0);
            Grid.SetRow(Copy, 1);
            Grid.SetColumnSpan(Copy, 2);
            Copy.Margin = new Thickness(0, 8, 0, 0);
            Copy.HorizontalAlignment = HorizontalAlignment.Stretch;
            Title.TextAlignment = TextAlignment.Center;
            Detail.TextAlignment = TextAlignment.Center;
            AppName.TextAlignment = TextAlignment.Center;
            AppName.HorizontalAlignment = HorizontalAlignment.Center;
            Detail.MaxHeight = 96;
            Title.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(Percent, 0);
            Grid.SetRow(Percent, 1);
            Percent.Margin = new Thickness(0, 2, 0, 0);
            Percent.HorizontalAlignment = HorizontalAlignment.Center;
            return;
        }

        IconCol.Width = new GridLength(40);
        TextCol.Width = new GridLength(1, GridUnitType.Star);
        IconRow.Height = new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(IconHost, 0);
        Grid.SetRow(IconHost, 0);
        Grid.SetColumnSpan(IconHost, 1);
        Grid.SetColumn(Copy, 1);
        Grid.SetRow(Copy, 0);
        Grid.SetColumnSpan(Copy, 1);
        Copy.Margin = new Thickness(8, 0, 0, 0);
        Copy.HorizontalAlignment = HorizontalAlignment.Stretch;
        Title.TextAlignment = TextAlignment.Left;
        Detail.TextAlignment = TextAlignment.Left;
        AppName.TextAlignment = TextAlignment.Left;
        AppName.HorizontalAlignment = HorizontalAlignment.Stretch;
        Detail.MaxHeight = 54;
        Title.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(Percent, 1);
        Grid.SetRow(Percent, 0);
        Percent.Margin = new Thickness(10, 0, 0, 0);
        Percent.HorizontalAlignment = HorizontalAlignment.Right;
    }

    void HideApp() => AppName.Visibility = Visibility.Collapsed;

    void ShowIcon(bool speaker, bool battery, bool bell)
    {
        Speaker.Visibility = speaker ? Visibility.Visible : Visibility.Collapsed;
        Battery.Visibility = battery ? Visibility.Visible : Visibility.Collapsed;
        Bell.Visibility = bell ? Visibility.Visible : Visibility.Collapsed;
    }
}
