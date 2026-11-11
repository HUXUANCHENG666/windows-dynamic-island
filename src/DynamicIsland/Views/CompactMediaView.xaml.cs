using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Core.Island;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

public partial class CompactMediaView : UserControl
{
    public CompactMediaView()
    {
        InitializeComponent();
        Art.Fill = Theme.Placeholder;
        NotePath.Data = Geometry.Parse(Glyphs.Music);
        NotePath.Fill = Theme.Quiet;
    }

    public void SetArt(ImageSource? source)
    {
        if (source == null)
        {
            Art.Fill = Theme.Placeholder;
            Note.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        Art.Fill = new ImageBrush(source)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
        RenderOptions.SetBitmapScalingMode(Art, BitmapScalingMode.HighQuality);
        Note.Visibility = System.Windows.Visibility.Collapsed;
    }

    public void SetWave(double phase, double level, bool playing) => Wave.Set(phase, level, playing);
}
