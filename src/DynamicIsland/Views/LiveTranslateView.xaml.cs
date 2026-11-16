using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.LiveTranslate;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class LiveTranslateView : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler? CancelRequested;

    public LiveTranslateView()
    {
        InitializeComponent();
        TitleText.FontFamily = new FontFamily(IslandFonts.Ui);
        Status.FontFamily = TitleText.FontFamily;
        Original.FontFamily = TitleText.FontFamily;
        Chinese.FontFamily = TitleText.FontFamily;
        CloseText.FontFamily = TitleText.FontFamily;
        CancelText.FontFamily = TitleText.FontFamily;
        TitleText.Foreground = Theme.Title;
        Status.Foreground = Theme.Artist;
        Original.Foreground = Theme.Quiet;
        Chinese.Foreground = Theme.Title;
        CloseText.Foreground = Theme.Title;
        CancelText.Foreground = Theme.Title;
        Dot.Fill = Theme.Live;
        ProgressHost.Background = Theme.Track;
        ProgressBar.Background = Theme.Live;
        Ui.Hover(CloseTranslate, Theme.Chip, Theme.ChipHover);
        Ui.Hover(CancelDownload, Theme.Chip, Theme.ChipHover);
        CloseTranslate.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
        CancelDownload.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CancelRequested?.Invoke(this, EventArgs.Empty);
        };
        ShowStatus(LiveTranslateCopy.Listening, working: false, progress: null, canCancel: false);
    }

    public void SetVertical(bool vertical)
    {
        TitleRow.HorizontalAlignment = vertical ? HorizontalAlignment.Left : HorizontalAlignment.Left;
        Chinese.FontSize = vertical ? 15 : 16;
        Chinese.MaxHeight = vertical ? 96 : 64;
        Original.MaxHeight = vertical ? 48 : 36;
    }

    public void ShowStatus(string message, bool working, double? progress, bool canCancel)
    {
        Ui.Text(Status, message);
        Status.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        Dot.Opacity = working ? 1 : 0.45;
        CancelDownload.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        if (progress is double value)
        {
            ProgressHost.Visibility = Visibility.Visible;
            ProgressHost.UpdateLayout();
            double width = Math.Max(0, ProgressHost.ActualWidth * Math.Clamp(value, 0, 1));
            if (width < 1 && value > 0)
                width = 8;
            ProgressBar.Width = width;
        }
        else
        {
            ProgressHost.Visibility = Visibility.Collapsed;
            ProgressBar.Width = 0;
        }
    }

    public void ShowSubtitle(string chinese, string? original, bool alreadyChinese)
    {
        Ui.Text(Chinese, chinese);
        Chinese.Visibility = string.IsNullOrWhiteSpace(chinese) ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(original) && !string.Equals(original, chinese, StringComparison.Ordinal))
        {
            Ui.Text(Original, original);
            Original.Visibility = Visibility.Visible;
        }
        else
        {
            Original.Visibility = Visibility.Collapsed;
        }

        if (alreadyChinese)
            ShowStatus(LiveTranslateCopy.AlreadyChinese, working: true, progress: null, canCancel: false);
        else
            ShowStatus(LiveTranslateCopy.Listening, working: true, progress: null, canCancel: false);
    }

    public void ClearSubtitle()
    {
        Chinese.Text = "";
        Original.Text = "";
        Chinese.Visibility = Visibility.Collapsed;
        Original.Visibility = Visibility.Collapsed;
    }
}
