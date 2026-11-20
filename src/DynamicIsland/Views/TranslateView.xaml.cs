using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Translation;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class TranslateView : UserControl
{
    public event EventHandler? ConfirmClicked;
    public event EventHandler<TranslateDirection>? DirectionClicked;
    public event EventHandler<string>? CopyRequested;
    public event EventHandler? DismissClicked;
    public event EventHandler? ResultMenuClosed;

    readonly HoverFill _goLook = new();
    readonly HoverFill _copyLook = new();
    readonly HoverFill _autoLook = new();
    readonly HoverFill _zhEnLook = new();
    readonly HoverFill _enZhLook = new();
    readonly HoverFill _closeLook = new();
    IslandSkin _skin = IslandSkin.Default;
    TranslateDirection _direction = TranslateDirection.Auto;

    public TranslateView()
    {
        InitializeComponent();
        var ui = new FontFamily(IslandFonts.Ui);
        Source.FontFamily = ui;
        Result.FontFamily = ui;
        GoText.FontFamily = ui;
        AutoText.FontFamily = ui;
        ZhEnText.FontFamily = ui;
        EnZhText.FontFamily = ui;
        CopyText.FontFamily = ui;
        CloseText.FontFamily = ui;
        Source.Foreground = Theme.Artist;
        Result.Foreground = Theme.Title;
        Result.CaretBrush = Theme.Title;
        Result.SelectionBrush = Theme.ChipHover;
        Result.SelectionTextBrush = Theme.Title;
        AutoText.Foreground = Theme.Title;
        ZhEnText.Foreground = Theme.Title;
        EnZhText.Foreground = Theme.Title;
        CloseText.Foreground = Theme.Title;
        Ui.Hover(Go, _goLook);
        Ui.Hover(Copy, _copyLook);
        Ui.Hover(Auto, _autoLook);
        Ui.Hover(ZhEn, _zhEnLook);
        Ui.Hover(EnZh, _enZhLook);
        Ui.Hover(Close, _closeLook);
        ApplySkin(IslandSkin.Default);
        Go.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (Go.IsHitTestVisible)
                ConfirmClicked?.Invoke(this, EventArgs.Empty);
        };
        Copy.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (IsShowingResult && Result.Text.Length > 0)
                CopyRequested?.Invoke(this, Result.Text);
        };
        Close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            DismissClicked?.Invoke(this, EventArgs.Empty);
        };
        Auto.MouseLeftButtonUp += (_, e) => Pick(TranslateDirection.Auto, e);
        ZhEn.MouseLeftButtonUp += (_, e) => Pick(TranslateDirection.ChineseToEnglish, e);
        EnZh.MouseLeftButtonUp += (_, e) => Pick(TranslateDirection.EnglishToChinese, e);
        Result.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, CopyExecuted, CanCopy));
        Result.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (ResultIsSelectable)
                Result.Focus();
        };
        if (Result.ContextMenu != null)
            Result.ContextMenu.Closed += (_, _) => ResultMenuClosed?.Invoke(this, EventArgs.Empty);
        SetDirection(TranslateDirection.Auto);
    }

    public bool ResultIsSelectable { get; private set; }
    public bool IsShowingResult { get; private set; }
    public string ResultText => Result.Text;
    public bool IsResultMenuOpen => Result.ContextMenu?.IsOpen == true;
    public FrameworkElement ResultBox => Result;
    public FrameworkElement CopyButton => Copy;
    public FrameworkElement CloseButton => Close;

    public bool IsShowing(string text) =>
        IsShowingResult && string.Equals(Result.Text, text, StringComparison.Ordinal);

    public void ApplySkin(IslandSkin skin)
    {
        _skin = skin;
        bool glass = skin == IslandSkin.Glass;
        StyleKey(Go, _goLook, glass, primary: true);
        StyleKey(Copy, _copyLook, glass, primary: true);
        StyleKey(Auto, _autoLook, glass, primary: false);
        StyleKey(ZhEn, _zhEnLook, glass, primary: false);
        StyleKey(EnZh, _enZhLook, glass, primary: false);
        StyleKey(Close, _closeLook, glass, primary: false);
        GoText.Foreground = glass ? Theme.Title : Theme.Ink;
        CopyText.Foreground = glass ? Theme.Title : Theme.Ink;
        SetDirection(_direction);
    }

    static void StyleKey(Border border, HoverFill fill, bool glass, bool primary)
    {
        if (glass)
        {
            fill.Normal = Theme.GlassKey;
            fill.Hover = Theme.GlassKeyHover;
            fill.Pressed = Theme.GlassKeyPress;
            border.BorderBrush = Theme.GlassKeyStroke;
            border.BorderThickness = new Thickness(1);
        }
        else if (primary)
        {
            fill.Normal = Theme.Title;
            fill.Hover = Theme.Title;
            fill.Pressed = Theme.ChipHover;
            border.BorderBrush = Brushes.Transparent;
            border.BorderThickness = new Thickness(0);
        }
        else
        {
            fill.Normal = Theme.Chip;
            fill.Hover = Theme.ChipHover;
            fill.Pressed = Theme.Hover;
            border.BorderBrush = Brushes.Transparent;
            border.BorderThickness = new Thickness(0);
        }

        Ui.Paint(border, fill);
    }

    public void SetVertical(bool vertical)
    {
        // 竖排时不要把六个按钮竖着堆起来——那样会把译文挤出画面。
        // 保持横向排列，宽度不够时由 WrapPanel 自动换行，行间用下边距隔开。
        Actions.Orientation = Orientation.Horizontal;
        var margin = vertical ? new Thickness(0, 0, 6, 8) : new Thickness(0, 0, 6, 0);
        Go.Margin = margin;
        Auto.Margin = margin;
        ZhEn.Margin = margin;
        EnZh.Margin = margin;
        Copy.Margin = margin;
        Close.Margin = margin;
        Source.MaxHeight = vertical ? 60 : 36;
        Source.TextAlignment = vertical ? TextAlignment.Center : TextAlignment.Left;
        Result.TextAlignment = vertical ? TextAlignment.Center : TextAlignment.Left;
        // 换行要拿到完整可用宽度；居中对齐会让宽度收缩成一行，反而不换行。
        Actions.HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
    }

    public void SetDirection(TranslateDirection direction)
    {
        _direction = direction;
        bool glass = _skin == IslandSkin.Glass;
        Brush selected = glass ? Theme.GlassKeyHover : Theme.ChipHover;
        Brush idle = glass ? Theme.GlassKey : Theme.Chip;
        PaintChoice(Auto, _autoLook, direction == TranslateDirection.Auto, selected, idle);
        PaintChoice(ZhEn, _zhEnLook, direction == TranslateDirection.ChineseToEnglish, selected, idle);
        PaintChoice(EnZh, _enZhLook, direction == TranslateDirection.EnglishToChinese, selected, idle);
    }

    static void PaintChoice(Border border, HoverFill fill, bool on, Brush selected, Brush idle)
    {
        fill.Normal = on ? selected : idle;
        fill.Hover = selected;
        Ui.Paint(border, fill);
    }

    public void ShowInvite(string text)
    {
        Ui.Text(Source, TrimPreview(text));
        SetResult("点「翻译」后才会发出去", selectable: false);
        Ui.Text(GoText, "翻译");
        Go.IsHitTestVisible = true;
        Go.Opacity = 1;
        ShowResultActions(false);
    }

    public void ShowBusy(bool clipped)
    {
        SetResult(clipped ? "只发送前 400 字，正在翻译…" : "正在翻译…", selectable: false);
        Ui.Text(GoText, "翻译中");
        Go.IsHitTestVisible = false;
        Go.Opacity = 0.55;
        ShowResultActions(false);
    }

    public void ShowResult(string text)
    {
        SetResult(text, selectable: true);
        Ui.Text(GoText, "再译");
        Go.IsHitTestVisible = true;
        Go.Opacity = 1;
        ShowResultActions(true);
    }

    public void ShowError(string message)
    {
        SetResult(message, selectable: false);
        Ui.Text(GoText, "重试");
        Go.IsHitTestVisible = true;
        Go.Opacity = 1;
        ShowResultActions(false);
    }

    public void BlurResult()
    {
        if (Result.IsKeyboardFocusWithin)
            Keyboard.ClearFocus();
    }

    static string TrimPreview(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 80 ? flat : flat[..80] + "…";
    }

    void ShowResultActions(bool show)
    {
        IsShowingResult = show;
        Copy.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Close.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void SetResult(string text, bool selectable)
    {
        SetBox(text);
        ResultIsSelectable = selectable;
        Result.Focusable = selectable;
        Result.IsHitTestVisible = selectable;
        Result.Cursor = selectable ? Cursors.IBeam : Cursors.Arrow;
        if (!selectable)
            BlurResult();
    }

    void SetBox(string value)
    {
        if (!string.Equals(Result.Text, value, StringComparison.Ordinal))
            Result.Text = value;
    }

    void Pick(TranslateDirection direction, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetDirection(direction);
        DirectionClicked?.Invoke(this, direction);
    }

    void CanCopy(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = IsShowingResult && Result.Text.Length > 0;
        e.Handled = true;
    }

    void CopyExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        RequestCopy();
        e.Handled = true;
    }

    void RequestCopy()
    {
        if (!IsShowingResult || Result.Text.Length == 0)
            return;
        var text = Result.SelectionLength > 0 ? Result.SelectedText : Result.Text;
        if (string.IsNullOrEmpty(text))
            return;
        CopyRequested?.Invoke(this, text);
    }
}
