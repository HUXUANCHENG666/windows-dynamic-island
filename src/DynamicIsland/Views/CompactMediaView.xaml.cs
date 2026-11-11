using System.Windows;
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

        Art.Fill = ArtBrush.Create(source, ArtHost.Width, ArtHost.Height);
        Note.Visibility = System.Windows.Visibility.Collapsed;
    }

    public void SetWave(double phase, double level, bool playing) => Wave.Set(phase, level, playing);

    public void SetVertical(bool vertical)
    {
        if (vertical)
        {
            Layout.VerticalAlignment = VerticalAlignment.Center;
            Layout.HorizontalAlignment = HorizontalAlignment.Center;
            ColArt.Width = GridLength.Auto;
            ColGap.Width = new GridLength(0);
            ColWave.Width = new GridLength(0);
            RowArt.Height = GridLength.Auto;
            RowGap.Height = new GridLength(8);
            RowWave.Height = GridLength.Auto;
            Grid.SetColumn(ArtHost, 0);
            Grid.SetRow(ArtHost, 0);
            Grid.SetColumn(Wave, 0);
            Grid.SetRow(Wave, 2);
            Wave.Width = 22;
            Wave.Height = 28;
            return;
        }

        Layout.VerticalAlignment = VerticalAlignment.Stretch;
        Layout.HorizontalAlignment = HorizontalAlignment.Stretch;
        ColArt.Width = GridLength.Auto;
        ColGap.Width = new GridLength(1, GridUnitType.Star);
        ColWave.Width = GridLength.Auto;
        RowArt.Height = new GridLength(1, GridUnitType.Star);
        RowGap.Height = new GridLength(0);
        RowWave.Height = new GridLength(0);
        Grid.SetColumn(ArtHost, 0);
        Grid.SetRow(ArtHost, 0);
        Grid.SetColumn(Wave, 2);
        Grid.SetRow(Wave, 0);
        Wave.Width = 28;
        Wave.Height = 16;
    }
}
