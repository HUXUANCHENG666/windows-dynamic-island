namespace DynamicIsland.Core.Motion;

/// <summary>异步结果必须仍属于当前几何；不将展开前的整张结果拉伸到展开后。</summary>
public readonly record struct GlassFrameGeometry(int Width, int Height, int Left, int Top, double ScaleX, double ScaleY)
{
    public bool Matches(GlassFrameGeometry other, double tolerance = 3) =>
        Math.Abs(Width-other.Width)<=tolerance && Math.Abs(Height-other.Height)<=tolerance &&
        Math.Abs(Left-other.Left)<=tolerance && Math.Abs(Top-other.Top)<=tolerance &&
        Math.Abs(Width*(ScaleX-other.ScaleX))<=tolerance && Math.Abs(Height*(ScaleY-other.ScaleY))<=tolerance;
    public static bool IsFresh(double ageMilliseconds) => ageMilliseconds >= 0 && ageMilliseconds <= 100;
}
