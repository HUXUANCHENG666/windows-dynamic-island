namespace DynamicIsland.Core.Motion;

/// <summary>
/// 岛身里的浅水高度场：在小网格上积分高度和速度。
/// 拖动会沿运动方向注入力，松手注入一圈脉冲。只做数值，不碰窗口。
/// </summary>
public sealed class LiquidSurface
{
    public const int MinLongSide = 48;
    public const int MaxLongSide = 80;
    public const int DefaultLongSide = 64;
    public const float MaxAbsHeight = 1.15f;
    public const float MaxAbsVelocity = 6f;
    public const float WaveSpeed = 10.5f;
    public const float Restore = 6.5f;
    public const float Damping = 2.4f;
    public const float DragInject = 0.00055f;
    public const float ReleasePulse = 0.42f;
    public const float SettledHeight = 0.012f;
    public const float SettledVelocity = 0.04f;
    const float MaxStep = 1f / 90f;
    const int MinShortSide = 16;

    int _cols;
    int _rows;
    float[] _height = Array.Empty<float>();
    float[] _velocity = Array.Empty<float>();
    float[] _scratch = Array.Empty<float>();
    float _maxAbs;
    float _maxVel;

    public int Columns => _cols;
    public int Rows => _rows;
    public float PeakAbsHeight => _maxAbs;

    public void CopyHeights(float[] dest)
    {
        if (dest == null || dest.Length == 0 || _height.Length == 0)
            return;
        Array.Copy(_height, dest, Math.Min(dest.Length, _height.Length));
    }
    public void CopySnapshotTo(LiquidSurface target)
    {
        if (target._height.Length != _height.Length) target._height = new float[_height.Length];
        _height.CopyTo(target._height, 0);
        target._cols = _cols; target._rows = _rows;
        target._maxAbs = _maxAbs; target._maxVel = _maxVel;
    }

    /// <summary>给后台渲染的独立高度快照，不与 UI 积分数组共享可变内存。</summary>
    public LiquidSurface Snapshot() => new()
    {
        _cols = _cols, _rows = _rows, _height = (float[])_height.Clone(),
        _velocity = (float[])_velocity.Clone(), _scratch = new float[_height.Length],
        _maxAbs = _maxAbs, _maxVel = _maxVel
    };

    public bool IsSettled => _maxAbs <= SettledHeight && _maxVel <= SettledVelocity;

    public void EnsureSize(double width, double height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        int longSide = DefaultLongSide;
        int cols;
        int rows;
        if (width >= height)
        {
            cols = longSide;
            rows = Math.Max(MinShortSide, (int)Math.Round(longSide * height / width));
        }
        else
        {
            rows = longSide;
            cols = Math.Max(MinShortSide, (int)Math.Round(longSide * width / height));
        }

        if (cols == _cols && rows == _rows && _height.Length == cols * rows)
            return;

        _cols = cols;
        _rows = rows;
        _height = new float[cols * rows];
        _velocity = new float[cols * rows];
        _scratch = new float[cols * rows];
        _maxAbs = 0;
        _maxVel = 0;
    }

    public void Reset()
    {
        if (_height.Length == 0)
            return;
        Array.Clear(_height);
        Array.Clear(_velocity);
        _maxAbs = 0;
        _maxVel = 0;
    }

    public void Step(double dt, double vx, double vy, bool moving, bool released, double stretch = 0)
    {
        if (_height.Length == 0)
            EnsureSize(2, 1);
        if (dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt))
            return;
        if (dt > 0.25)
            dt = 0.25;

        if (moving)
            InjectDrag(vx, vy, dt);
        if (released)
            InjectRelease(vx, vy, stretch);

        int steps = Math.Max(1, (int)Math.Ceiling(dt / MaxStep));
        float step = (float)(dt / steps);
        for (int i = 0; i < steps; i++)
            Integrate(step);
        Measure();
    }

    public void InjectForce(double u, double v, float amount, float radius = 0.22f)
    {
        if (_height.Length == 0 || Math.Abs(amount) < 1e-8f || radius <= 0)
            return;

        u = Math.Clamp(u, 0, 1);
        v = Math.Clamp(v, 0, 1);
        float inv = 1f / (radius * radius);
        for (int y = 0; y < _rows; y++)
        {
            double py = _rows == 1 ? 0.5 : y / (double)(_rows - 1);
            for (int x = 0; x < _cols; x++)
            {
                double px = _cols == 1 ? 0.5 : x / (double)(_cols - 1);
                double dx = px - u;
                double dy = py - v;
                float weight = MathF.Exp((float)(-(dx * dx + dy * dy) * inv * 2));
                if (weight < 0.01f)
                    continue;
                int i = y * _cols + x;
                _velocity[i] = Math.Clamp(_velocity[i] + amount * weight, -MaxAbsVelocity, MaxAbsVelocity);
            }
        }
    }

    public float Sample(double u, double v)
    {
        if (_height.Length == 0 || _cols < 1 || _rows < 1)
            return 0;

        u = Math.Clamp(u, 0, 1) * Math.Max(0, _cols - 1);
        v = Math.Clamp(v, 0, 1) * Math.Max(0, _rows - 1);
        int x0 = (int)Math.Floor(u);
        int y0 = (int)Math.Floor(v);
        int x1 = Math.Min(x0 + 1, _cols - 1);
        int y1 = Math.Min(y0 + 1, _rows - 1);
        float fx = (float)(u - x0);
        float fy = (float)(v - y0);
        float a = _height[y0 * _cols + x0];
        float b = _height[y0 * _cols + x1];
        float c = _height[y1 * _cols + x0];
        float d = _height[y1 * _cols + x1];
        return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
    }

    public void SampleSlope(double u, double v, out float height, out float slopeX, out float slopeY)
    {
        height = Sample(u, v);
        double e = _cols <= 1 ? 0.08 : 1.0 / (_cols - 1);
        double n = _rows <= 1 ? 0.08 : 1.0 / (_rows - 1);
        slopeX = (Sample(u + e, v) - Sample(u - e, v)) / (float)(2 * e);
        slopeY = (Sample(u, v + n) - Sample(u, v - n)) / (float)(2 * n);
    }

    public (double U, double V, float Height) PrimaryCrest() =>
        AlmostFlat() ? (0.38, 0.36, 0) : Crest(exclude: null, minDistance: 0);

    public (double U, double V, float Height) SecondaryCrest((double U, double V, float Height) primary) =>
        AlmostFlat() ? (0.68, 0.62, 0) : Crest(primary, 0.22);

    public (double U, double V, float Height) SpecularPoint()
    {
        if (AlmostFlat())
            return (0.28, 0.22, 0);

        int best = 0;
        float bestScore = float.NegativeInfinity;
        for (int y = 0; y < _rows; y++)
        {
            double v = Uv(y, _rows);
            if (v > 0.62)
                continue;
            for (int x = 0; x < _cols; x++)
            {
                int i = y * _cols + x;
                double u = Uv(x, _cols);
                SampleSlope(u, v, out _, out var sx, out var sy);
                float score = _height[i] * 0.55f - sy * 0.35f - sx * 0.2f - (float)(v * 0.15);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
        }

        return FromIndex(best);
    }

    void InjectDrag(double vx, double vy, double dt)
    {
        double speed = Math.Sqrt(vx * vx + vy * vy);
        if (speed < LiquidDrag.MinSpeed * 0.5)
            return;

        double nx = vx / speed;
        double ny = vy / speed;
        float amp = (float)(Math.Min(speed, 2800) * DragInject * Math.Min(dt, 0.05));
        InjectForce(0.5 + nx * 0.18, 0.5 + ny * 0.18, amp, 0.28f);
        InjectForce(0.5 - nx * 0.18, 0.5 - ny * 0.18, -amp * 0.85f, 0.28f);
    }

    void InjectRelease(double vx, double vy, double stretch)
    {
        double speed = Math.Sqrt(vx * vx + vy * vy);
        float motion = (float)Math.Clamp(Math.Max(stretch, speed / 1600.0), 0, 1);
        float amp = ReleasePulse * (0.4f + 0.6f * motion);
        InjectForce(0.5, 0.5, amp, 0.36f);
    }

    void Integrate(float dt)
    {
        int cols = _cols;
        int rows = _rows;
        float c2 = WaveSpeed * WaveSpeed;
        for (int y = 0; y < rows; y++)
        {
            int up = y > 0 ? y - 1 : y;
            int down = y + 1 < rows ? y + 1 : y;
            for (int x = 0; x < cols; x++)
            {
                int left = x > 0 ? x - 1 : x;
                int right = x + 1 < cols ? x + 1 : x;
                int i = y * cols + x;
                float h = _height[i];
                float lap = _height[y * cols + left] + _height[y * cols + right]
                    + _height[up * cols + x] + _height[down * cols + x] - 4 * h;
                float acc = c2 * lap - Restore * h;
                float v = (_velocity[i] + acc * dt) * MathF.Exp(-Damping * dt);
                _scratch[i] = Math.Clamp(v, -MaxAbsVelocity, MaxAbsVelocity);
            }
        }

        Array.Copy(_scratch, _velocity, _velocity.Length);
        for (int i = 0; i < _height.Length; i++)
            _height[i] = Math.Clamp(_height[i] + _velocity[i] * dt, -MaxAbsHeight, MaxAbsHeight);
    }

    void Measure()
    {
        float maxH = 0;
        float maxV = 0;
        for (int i = 0; i < _height.Length; i++)
        {
            maxH = Math.Max(maxH, Math.Abs(_height[i]));
            maxV = Math.Max(maxV, Math.Abs(_velocity[i]));
        }

        _maxAbs = maxH;
        _maxVel = maxV;
    }

    (double U, double V, float Height) Crest((double U, double V, float Height)? exclude, double minDistance)
    {
        if (_height.Length == 0)
            return (0.5, 0.5, 0);

        int best = 0;
        float bestH = float.NegativeInfinity;
        for (int i = 0; i < _height.Length; i++)
        {
            float h = _height[i];
            if (h <= bestH)
                continue;
            if (exclude is { } other)
            {
                var (u, v, _) = FromIndex(i);
                double du = u - other.U;
                double dv = v - other.V;
                if (du * du + dv * dv < minDistance * minDistance)
                    continue;
            }

            bestH = h;
            best = i;
        }

        return FromIndex(best);
    }

    (double U, double V, float Height) FromIndex(int i)
    {
        int x = i % _cols;
        int y = i / _cols;
        return (Uv(x, _cols), Uv(y, _rows), _height[i]);
    }

    static double Uv(int index, int count) => count <= 1 ? 0.5 : index / (double)(count - 1);

    bool AlmostFlat() => _height.Length == 0 || _maxAbs <= SettledHeight;
}
