using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Core.Camera;

namespace DynamicIsland.Views;

static class ArtBrush
{
    public static ImageBrush Create(ImageSource source, double slotWidth, double slotHeight)
    {
        var brush = new ImageBrush(source)
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Stretch = Stretch.UniformToFill,
            Viewport = new System.Windows.Rect(0, 0, 1, 1),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox
        };
        if (source is BitmapSource bitmap
            && bitmap.PixelWidth > 0
            && bitmap.PixelHeight > 0
            && slotWidth > 0
            && slotHeight > 0)
        {
            var box = CameraFraming.CenteredViewbox(bitmap.PixelWidth, bitmap.PixelHeight, slotWidth, slotHeight);
            if (box.Width > 0 && box.Height > 0)
            {
                brush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
                brush.Viewbox = new System.Windows.Rect(box.X, box.Y, box.Width, box.Height);
                brush.Stretch = Stretch.Fill;
            }
        }

        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        return brush;
    }
}
