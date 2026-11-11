using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.Translation;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class TranslateView : UserControl
{
    public event EventHandler? ConfirmClicked;
    public event EventHandler<TranslateDirection>? DirectionClicked;

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
        Source.Foreground = Theme.Artist;
        Result.Foreground = Theme.Title;
        GoText.Foreground = Theme.Ink;
        AutoText.Foreground = Theme.Title;
        ZhEnText.Foreground = Theme.Title;
        EnZhText.Foreground = Theme.Title;
        Go.Background = Theme.Title;
        Go.Cursor = System.Windows.Input.Cursors.Hand;
        Go.Focusable = false;
        Ui.Hover(Auto, Theme.Chip, Theme.ChipHover);
        Ui.Hover(ZhEn, Theme.Chip, Theme.ChipHover);
        Ui.Hover(EnZh, Theme.Chip, Theme.ChipHover);
        Go.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (Go.IsHitTestVisible)
                ConfirmClicked?.Invoke(this, EventArgs.Empty);
        };
        Auto.MouseLeftButtonUp += (_, e) => Pick(TranslateDirection.Auto, e);
        ZhEn.MouseLeftButtonUp += (_, e) => Pick(TranslateDirection.ChineseToEnglish, e);
        EnZh.MouseLeftButtonUp += (_, e) => Pick(TranslateDirection.EnglishToChinese, e);
        SetDirection(TranslateDirection.Auto);
    }

    public void SetVertical(bool vertical)
    {
        Actions.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        var gap = vertical ? new Thickness(0, 8, 0, 0) : new Thickness(8, 0, 0, 0);
        Auto.Margin = gap;
        ZhEn.Margin = gap;
        EnZh.Margin = gap;
        Source.MaxHeight = vertical ? 96 : 40;
        Result.MaxHeight = vertical ? 140 : 72;
        Source.TextAlignment = vertical ? TextAlignment.Center : TextAlignment.Left;
        Result.TextAlignment = vertical ? TextAlignment.Center : TextAlignment.Left;
        Actions.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left;
    }

    public void SetDirection(TranslateDirection direction)
    {
        Auto.Background = direction == TranslateDirection.Auto ? Theme.ChipHover : Theme.Chip;
        ZhEn.Background = direction == TranslateDirection.ChineseToEnglish ? Theme.ChipHover : Theme.Chip;
        EnZh.Background = direction == TranslateDirection.EnglishToChinese ? Theme.ChipHover : Theme.Chip;
    }

    public void ShowInvite(string text)
    {
        Ui.Text(Source, TrimPreview(text));
        Ui.Text(Result, "点「翻译」后才会发出去");
        Ui.Text(GoText, "翻译");
        Go.IsHitTestVisible = true;
        Go.Opacity = 1;
    }

    public void ShowBusy(bool clipped)
    {
        Ui.Text(Result, clipped ? "只发送前 400 字，正在翻译…" : "正在翻译…");
        Ui.Text(GoText, "翻译中");
        Go.IsHitTestVisible = false;
        Go.Opacity = 0.55;
    }

    public void ShowResult(string text)
    {
        Ui.Text(Result, text);
        Ui.Text(GoText, "再译");
        Go.IsHitTestVisible = true;
        Go.Opacity = 1;
    }

    public void ShowError(string message)
    {
        Ui.Text(Result, message);
        Ui.Text(GoText, "重试");
        Go.IsHitTestVisible = true;
        Go.Opacity = 1;
    }

    static string TrimPreview(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 80 ? flat : flat[..80] + "…";
    }

    void Pick(TranslateDirection direction, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        SetDirection(direction);
        DirectionClicked?.Invoke(this, direction);
    }
}
