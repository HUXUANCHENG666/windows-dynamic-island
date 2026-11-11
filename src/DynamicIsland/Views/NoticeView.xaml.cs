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
        Percent.FontFamily = ui;
        Title.Foreground = Theme.Title;
        Detail.Foreground = Theme.Artist;
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
        ShowIcon(speaker: false, battery: true, bell: false);
        Battery.Set(announcement.Percent, announcement.Charging);
        Ui.Text(Title, announcement.Title);
        Ui.Text(Detail, announcement.Detail);
        Percent.Text = "";
        Detail.Visibility = Visibility.Visible;
        BarHost.Visibility = Visibility.Collapsed;
    }

    public void SetToast(string title, string body)
    {
        ShowIcon(speaker: false, battery: false, bell: true);
        Ui.Text(Title, string.IsNullOrWhiteSpace(title) ? "通知" : title);
        Ui.Text(Detail, body);
        Percent.Text = "";
        Detail.Visibility = string.IsNullOrWhiteSpace(body) ? Visibility.Collapsed : Visibility.Visible;
        BarHost.Visibility = Visibility.Collapsed;
    }

    void ShowIcon(bool speaker, bool battery, bool bell)
    {
        Speaker.Visibility = speaker ? Visibility.Visible : Visibility.Collapsed;
        Battery.Visibility = battery ? Visibility.Visible : Visibility.Collapsed;
        Bell.Visibility = bell ? Visibility.Visible : Visibility.Collapsed;
    }
}
