namespace DynamicIsland.Core.Motion;

/// <summary>
/// 拖动时的拉伸和松手后的 Q 弹。只做数值，不碰窗口。
/// </summary>
public sealed class LiquidBounce
{
    readonly Spring _scaleX = new(LiquidDrag.Bounce, 1);
    readonly Spring _scaleY = new(LiquidDrag.Bounce, 1);
    bool _wasMoving;

    public double ScaleX => _scaleX.Value;
    public double ScaleY => _scaleY.Value;

    public bool IsSettled =>
        _scaleX.IsSettled(0.004, 0.02)
        && _scaleY.IsSettled(0.004, 0.02)
        && Math.Abs(_scaleX.Value - 1) <= 0.004
        && Math.Abs(_scaleY.Value - 1) <= 0.004;

    public void Step(double dt, bool moving, double vx, double vy, double maxX, double maxY)
    {
        var (targetX, targetY) = moving
            ? LiquidDrag.AxisScale(vx, vy, maxX, maxY)
            : (1, 1);

        if (!moving && _wasMoving)
        {
            _scaleX.Velocity += LiquidDrag.ReleaseKick(_scaleX.Value);
            _scaleY.Velocity += LiquidDrag.ReleaseKick(_scaleY.Value);
        }

        _wasMoving = moving;
        _scaleX.Target = targetX;
        _scaleY.Target = targetY;
        _scaleX.Step(dt);
        _scaleY.Step(dt);
    }

    public void Reset()
    {
        _scaleX.Jump(1);
        _scaleY.Jump(1);
        _wasMoving = false;
    }
}
