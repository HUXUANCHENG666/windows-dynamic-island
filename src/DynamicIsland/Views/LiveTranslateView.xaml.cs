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
        OldestTime.FontFamily = TitleText.FontFamily;
        OldestText.FontFamily = TitleText.FontFamily;
        OlderTime.FontFamily = TitleText.FontFamily;
        OlderText.FontFamily = TitleText.FontFamily;
        NewerTime.FontFamily = TitleText.FontFamily;
        NewerText.FontFamily = TitleText.FontFamily;
        NewerOriginal.FontFamily = TitleText.FontFamily;
        CloseText.FontFamily = TitleText.FontFamily;
        CancelText.FontFamily = TitleText.FontFamily;
        TitleText.Foreground = Theme.Title;
        Status.Foreground = Theme.Artist;
        OldestTime.Foreground = Theme.Quiet;
        OldestText.Foreground = Theme.Artist;
        OlderTime.Foreground = Theme.Quiet;
        OlderText.Foreground = Theme.Artist;
        NewerTime.Foreground = Theme.Quiet;
        NewerText.Foreground = Theme.Title;
        NewerOriginal.Foreground = Theme.Quiet;
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
        TitleRow.HorizontalAlignment = HorizontalAlignment.Left;
        OldestText.FontSize = 13;
        OlderText.FontSize = 13;
        OldestText.MaxHeight = vertical ? 64 : 40;
        OlderText.MaxHeight = vertical ? 64 : 40;
        NewerText.FontSize = vertical ? 15 : 16;
        NewerText.MaxHeight = vertical ? 80 : 56;
        NewerOriginal.MaxHeight = vertical ? 48 : 36;
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

    public void ShowCaptions(IReadOnlyList<LiveTranslateCaption> items)
    {
        if (items is null || items.Count == 0)
        {
            ClearSubtitle();
            return;
        }

        OldestBlock.Visibility = Visibility.Collapsed;
        OlderBlock.Visibility = Visibility.Collapsed;
        if (items.Count == 1)
        {
            PaintNewer(items[0]);
            return;
        }

        if (items.Count == 2)
        {
            PaintRow(OlderTime, OlderText, OlderBlock, items[0]);
            PaintNewer(items[1]);
            return;
        }

        PaintRow(OldestTime, OldestText, OldestBlock, items[0]);
        PaintRow(OlderTime, OlderText, OlderBlock, items[1]);
        PaintNewer(items[^1]);
    }

    public void ClearSubtitle()
    {
        OldestTime.Text = "";
        OldestText.Text = "";
        OlderTime.Text = "";
        OlderText.Text = "";
        NewerTime.Text = "";
        NewerText.Text = "";
        NewerOriginal.Text = "";
        OldestBlock.Visibility = Visibility.Collapsed;
        OlderBlock.Visibility = Visibility.Collapsed;
        NewerBlock.Visibility = Visibility.Collapsed;
        NewerOriginal.Visibility = Visibility.Collapsed;
    }

    static void PaintRow(TextBlock time, TextBlock text, StackPanel block, LiveTranslateCaption caption)
    {
        Ui.Text(time, caption.Timestamp);
        Ui.Text(text, caption.Primary);
        block.Visibility = Visibility.Visible;
    }

    void PaintNewer(LiveTranslateCaption caption)
    {
        Ui.Text(NewerTime, caption.Timestamp);
        Ui.Text(NewerText, caption.Primary);
        NewerBlock.Visibility = Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(caption.Original)
            && !string.Equals(caption.Original, caption.Primary, StringComparison.Ordinal))
        {
            Ui.Text(NewerOriginal, caption.Original);
            NewerOriginal.Visibility = Visibility.Visible;
        }
        else
        {
            NewerOriginal.Text = "";
            NewerOriginal.Visibility = Visibility.Collapsed;
        }
    }
}
