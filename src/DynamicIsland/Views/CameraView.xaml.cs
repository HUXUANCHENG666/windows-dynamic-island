using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class CameraView : UserControl
{
    public CameraView()
    {
        InitializeComponent();
        LiveText.FontFamily = new FontFamily(IslandFonts.Ui);
        Message.FontFamily = LiveText.FontFamily;
        LiveText.Foreground = Theme.Title;
        Message.Foreground = Theme.Title;
        Dot.Fill = Theme.Danger;
        FrameHost.Background = Theme.Placeholder;
        SetLive(false);
    }

    public void SetVertical(bool vertical)
    {
        LiveRow.HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        Message.TextAlignment = TextAlignment.Center;
    }

    public void ShowWaiting()
    {
        SetLive(false);
        Preview.Source = null;
        Ui.Text(Message, "正在打开摄像头…");
        Message.Visibility = Visibility.Visible;
    }

    public void ShowError(string message)
    {
        SetLive(false);
        Preview.Source = null;
        Ui.Text(Message, message);
        Message.Visibility = Visibility.Visible;
    }

    public void SetLive(bool live)
    {
        LiveRow.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetFrame(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            return;
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), bgra, width * 4, 0);
        bitmap.Freeze();
        Preview.Source = bitmap;
        if (Message.Visibility != Visibility.Collapsed)
            Message.Visibility = Visibility.Collapsed;
        if (LiveRow.Visibility != Visibility.Visible)
            SetLive(true);
    }
}
