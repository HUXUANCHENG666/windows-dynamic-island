using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Views;

/// <summary>
/// 四根圆角竖条。播放时按相位摆动，高度再乘系统输出的峰值；暂停时收回去。
/// SMTC 不提供波形采样，所以这是设计过的指示，不是那首歌的频谱。
/// </summary>
sealed class WaveformControl : FrameworkElement
{
    public double Phase { get; private set; }
    public double Level { get; private set; } = 0.65;
    public bool Playing { get; private set; }

    public void Set(double phase, double level, bool playing)
    {
        Phase = phase;
        Level = level;
        Playing = playing;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available) => new(28, 16);

    protected override void OnRender(DrawingContext dc)
    {
        const int bars = 4;
        const double barWidth = 2.4;
        const double gap = 2.0;
        double total = bars * barWidth + (bars - 1) * gap;
        double x0 = Math.Max(0, (ActualWidth - total) / 2);
        double mid = ActualHeight / 2;
        double amp = 0.35 + 0.65 * Math.Clamp(Level, 0, 1);
        for (int i = 0; i < bars; i++)
        {
            double height;
            if (!Playing)
            {
                height = 3.2;
            }
            else
            {
                double a = Math.Sin(Phase * 5.4 + i * 1.2);
                double b = Math.Sin(Phase * 2.2 + i * 0.65);
                double n = 0.45 + 0.55 * (0.5 + 0.5 * (0.7 * a + 0.3 * b));
                height = 3.0 + n * amp * Math.Max(0, ActualHeight - 3.0);
            }

            double x = x0 + i * (barWidth + gap);
            var rect = new Rect(x, mid - height / 2, barWidth, height);
            dc.DrawRoundedRectangle(Brushes.White, null, rect, barWidth / 2, barWidth / 2);
        }
    }
}
