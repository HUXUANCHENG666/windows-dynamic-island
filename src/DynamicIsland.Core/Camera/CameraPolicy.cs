namespace DynamicIsland.Core.Camera;

public enum CameraLifetime
{
    Showing,
    Drag,
    Snap,
    Layout,
    Rebind,
    PointerLeave,
    Collapse,
    UserOff,
    Occlusion,
    Exit
}

/// <summary>
/// 摄像头默认关，不写入开机后的状态。只有用户这次明确打开，并且岛正展开显示它，设备才可以开着。
/// 拖动、吸附、布局重排、重新绑定预览，都不能放开设备。收起、关掉、隐身、全屏、退出才放开。
/// </summary>
public static class CameraPolicy
{
    public const bool EnabledByDefault = false;
    public const bool PersistsAcrossLaunches = false;

    public static bool MayStart(bool userRequested, bool occluded) => userRequested && !occluded;

    public static bool ShouldKeepDevice(bool userEnabled, bool expanded, bool showingCamera) =>
        userEnabled && expanded && showingCamera;

    public static CameraLifetime Classify(bool sessionActive, bool expanded, bool occluded, bool dragging)
    {
        if (occluded)
            return CameraLifetime.Occlusion;
        if (dragging)
            return CameraLifetime.Drag;
        if (sessionActive && expanded)
            return CameraLifetime.Showing;
        return CameraLifetime.Collapse;
    }

    public static bool ShouldRelease(CameraLifetime reason) => reason switch
    {
        CameraLifetime.Collapse or CameraLifetime.UserOff or CameraLifetime.Occlusion or CameraLifetime.Exit => true,
        _ => false
    };
}

/// <summary>
/// 把摄像头画面铺进岛里：两个轴用同一个缩放，多出来的部分从中心裁掉。横竖停靠用同一条规则。
/// </summary>
public static class CameraFraming
{
    public readonly record struct CoverFit(double Scale, double X, double Y, double Width, double Height);

    public static CoverFit Cover(double sourceWidth, double sourceHeight, double destWidth, double destHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || destWidth <= 0 || destHeight <= 0
            || double.IsNaN(sourceWidth) || double.IsNaN(sourceHeight)
            || double.IsNaN(destWidth) || double.IsNaN(destHeight))
            return new CoverFit(0, 0, 0, 0, 0);

        double scale = Math.Max(destWidth / sourceWidth, destHeight / sourceHeight);
        double width = sourceWidth * scale;
        double height = sourceHeight * scale;
        return new CoverFit(scale, (destWidth - width) / 2.0, (destHeight - height) / 2.0, width, height);
    }

    /// <summary>
    /// 源图上被保留的那一块，坐标是相对整张图的 0 到 1。
    /// 宽和高跟目标格子的比例一致，中心跟原图中心重合，所以铺进格子时不会拉扁，也不会靠向某一边。
    /// </summary>
    public readonly record struct RelRect(double X, double Y, double Width, double Height);

    public static RelRect CenteredViewbox(double sourceWidth, double sourceHeight, double destWidth, double destHeight)
    {
        var fit = Cover(sourceWidth, sourceHeight, destWidth, destHeight);
        if (fit.Scale <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
            return new RelRect(0, 0, 0, 0);

        double cropW = destWidth / fit.Scale;
        double cropH = destHeight / fit.Scale;
        double cropX = (sourceWidth - cropW) / 2.0;
        double cropY = (sourceHeight - cropH) / 2.0;
        return new RelRect(cropX / sourceWidth, cropY / sourceHeight, cropW / sourceWidth, cropH / sourceHeight);
    }
}
