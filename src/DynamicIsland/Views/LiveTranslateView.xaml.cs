using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Core.LiveTranslate;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class LiveTranslateView : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler? CancelRequested;

    const double ScrollLine = 48;

    double? _progress;
    Storyboard? _indeterminate;
    bool _vertical;
    bool _stickToEnd = true;
    bool _programmaticScroll;
    bool _pendingStick;
    int _stickGeneration;
    IReadOnlyList<LiveTranslateCaption>? _items;

    public LiveTranslateView()
    {
        InitializeComponent();
        TitleText.FontFamily = new FontFamily(IslandFonts.Ui);
        Status.FontFamily = TitleText.FontFamily;
        CloseText.FontFamily = TitleText.FontFamily;
        CancelText.FontFamily = TitleText.FontFamily;
        TitleText.Foreground = Theme.Title;
        Status.Foreground = Theme.Artist;
        CloseText.Foreground = Theme.Title;
        CancelText.Foreground = Theme.Title;
        Dot.Fill = Theme.Live;
        ProgressHost.Background = Theme.Track;
        ProgressBar.Background = Theme.Live;
        IndeterminateBar.Background = Theme.Live;
        ProgressHost.SizeChanged += (_, _) => ApplyProgress(restartIndeterminate: true);
        CaptionScroll.Background = Brushes.Transparent;
        CaptionList.SizeChanged += (_, _) =>
        {
            if (_stickToEnd)
                QueueStickToEnd();
        };
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
        if (_vertical == vertical)
            return;
        _vertical = vertical;
        if (_items is { Count: > 0 } items)
            ShowCaptions(items);
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
        _items = items;
        if (items is null || items.Count == 0)
        {
            ClearSubtitle();
            return;
        }

        double keep = CaptionScroll.VerticalOffset;
        _programmaticScroll = true;
        CaptionList.Children.Clear();
        for (int i = 0; i < items.Count; i++)
            CaptionList.Children.Add(BuildRow(items[i], newest: i == items.Count - 1));

        CaptionScroll.UpdateLayout();
        if (_stickToEnd)
            QueueStickToEnd();
        else
            RestoreOffset(keep);
    }

    public void ClearSubtitle()
    {
        _items = null;
        _programmaticScroll = true;
        CaptionList.Children.Clear();
        _stickToEnd = true;
        QueueStickToEnd();
    }

    public void TryWheel(int delta)
    {
        if (!CaptionScroll.IsVisible)
            return;
        double step = delta / 120.0 * ScrollLine;
        double max = Math.Max(0, CaptionScroll.ExtentHeight - CaptionScroll.ViewportHeight);
        CaptionScroll.ScrollToVerticalOffset(Math.Clamp(CaptionScroll.VerticalOffset - step, 0, max));
    }

    void OnCaptionScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_programmaticScroll || _pendingStick)
            return;
        _stickToEnd = CaptionScrollPolicy.IsAtEnd(
            CaptionScroll.VerticalOffset, CaptionScroll.ViewportHeight, CaptionScroll.ExtentHeight);
    }

    void OnCaptionMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TryWheel(e.Delta);
        e.Handled = true;
    }

    void QueueStickToEnd()
    {
        StickToEnd();
        _pendingStick = true;
        int generation = ++_stickGeneration;
        Dispatcher.BeginInvoke(() =>
        {
            if (generation != _stickGeneration)
                return;
            if (_stickToEnd)
                StickToEnd();
            _pendingStick = false;
        }, DispatcherPriority.Loaded);
    }

    void StickToEnd()
    {
        _programmaticScroll = true;
        CaptionScroll.UpdateLayout();
        CaptionScroll.ScrollToVerticalOffset(
            CaptionScrollPolicy.EndOffset(CaptionScroll.ViewportHeight, CaptionScroll.ExtentHeight));
        _programmaticScroll = false;
        _stickToEnd = true;
    }

    void RestoreOffset(double offset)
    {
        _pendingStick = false;
        _programmaticScroll = true;
        CaptionScroll.UpdateLayout();
        double max = CaptionScrollPolicy.EndOffset(CaptionScroll.ViewportHeight, CaptionScroll.ExtentHeight);
        CaptionScroll.ScrollToVerticalOffset(Math.Clamp(offset, 0, max));
        _programmaticScroll = false;
    }

    FrameworkElement BuildRow(LiveTranslateCaption caption, bool newest)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, newest ? 0 : 8) };
        var time = new TextBlock
        {
            FontFamily = TitleText.FontFamily,
            FontSize = 11,
            Foreground = Theme.Quiet,
            Margin = new Thickness(0, 0, 0, 2),
            TextWrapping = TextWrapping.NoWrap
        };
        var text = new TextBlock
        {
            FontFamily = TitleText.FontFamily,
            FontSize = newest ? (_vertical ? 15 : 16) : 13,
            FontWeight = newest ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = newest ? Theme.Title : Theme.Artist,
            TextWrapping = TextWrapping.Wrap
        };
        Ui.Text(time, caption.Timestamp);
        Ui.Text(text, caption.Primary);
        block.Children.Add(time);
        block.Children.Add(text);
        if (!string.IsNullOrWhiteSpace(caption.Original)
            && !string.Equals(caption.Original, caption.Primary, StringComparison.Ordinal))
        {
            var original = new TextBlock
            {
                FontFamily = TitleText.FontFamily,
                FontSize = 12,
                Foreground = Theme.Quiet,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            Ui.Text(original, caption.Original);
            block.Children.Add(original);
        }

        return block;
    }
}
