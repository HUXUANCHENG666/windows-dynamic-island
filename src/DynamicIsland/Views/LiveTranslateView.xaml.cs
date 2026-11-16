using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Core.LiveTranslate;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class LiveTranslateView : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler? CancelRequested;

    double? _progress;
    Storyboard? _indeterminate;

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
        IndeterminateBar.Background = Theme.Live;
        ProgressHost.SizeChanged += (_, _) => ApplyProgress(restartIndeterminate: true);
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
        _progress = progress;
        ApplyProgress(restartIndeterminate: false);
    }

    void ApplyProgress(bool restartIndeterminate)
    {
        if (_progress is not double value)
        {
            StopIndeterminate();
            ProgressHost.Visibility = Visibility.Collapsed;
            ProgressBar.Width = 0;
            return;
        }

        ProgressHost.Visibility = Visibility.Visible;
        ProgressHost.UpdateLayout();
        if (value <= 0)
        {
            ProgressBar.Width = 0;
            StartIndeterminate(restartIndeterminate || _indeterminate == null);
            return;
        }

        StopIndeterminate();
        double width = Math.Max(0, ProgressHost.ActualWidth * Math.Clamp(value, 0, 1));
        if (width < 1)
            width = 8;
        ProgressBar.Width = width;
    }

    void StartIndeterminate(bool restart)
    {
        IndeterminateBar.Visibility = Visibility.Visible;
        if (_indeterminate != null && !restart)
            return;

        StopIndeterminate(hide: false);
        double travel = Math.Max(80, ProgressHost.ActualWidth);
        var anim = new DoubleAnimation
        {
            From = -48,
            To = travel,
            Duration = TimeSpan.FromSeconds(1.2),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim, IndeterminateShift);
        Storyboard.SetTargetProperty(anim, new PropertyPath(TranslateTransform.XProperty));
        _indeterminate = new Storyboard { Children = { anim } };
        _indeterminate.Begin();
    }

    void StopIndeterminate(bool hide = true)
    {
        _indeterminate?.Stop();
        _indeterminate = null;
        IndeterminateShift.X = 0;
        if (hide)
            IndeterminateBar.Visibility = Visibility.Collapsed;
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
