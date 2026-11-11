using System.Windows;
using System.Windows.Media;
using DynamicIsland.Visuals;

namespace DynamicIsland.Views;

sealed class BatteryIcon : FrameworkElement
{
    static readonly Pen BodyPen = CreatePen(Theme.Title, 1.5);
    static readonly Geometry Bolt = Geometry.Parse("M15.2,6.4 L9.4,15.2 H13.2 L12.2,23.6 L19.8,13.2 H15.6 Z");

    public double Percent { get; private set; } = 100;
    public bool Charging { get; private set; }

    public void Set(double percent, bool charging)
    {
        Percent = percent;
        Charging = charging;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available) => new(34, 22);

    protected override void OnRender(DrawingContext dc)
    {
        var body = new Rect(1.2, 5.2, 24, 12.2);
        dc.DrawRoundedRectangle(null, BodyPen, body, 3, 3);
        dc.DrawRoundedRectangle(Theme.Title, null, new Rect(25.6, 8.6, 2.4, 5.2), 1, 1);

        double fraction = Math.Clamp(Percent, 0, 100) / 100.0;
        double inner = 19.2 * fraction;
        if (inner > 0.8)
        {
            var fill = Percent <= 15 && !Charging ? Theme.Danger : Theme.Title;
            dc.DrawRoundedRectangle(fill, null, new Rect(3.2, 7.3, inner, 8), 1.6, 1.6);
        }

        if (Charging)
            dc.DrawGeometry(Theme.Title, null, Bolt);
    }

    static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}
