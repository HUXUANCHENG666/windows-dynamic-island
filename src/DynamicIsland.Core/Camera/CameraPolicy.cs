namespace DynamicIsland.Core.Camera;

/// <summary>
/// 摄像头默认关，不写入开机后的状态。只有用户这次明确打开，并且岛正展开显示它，设备才可以开着。
/// 收起、隐身、全屏时必须关掉。
/// </summary>
public static class CameraPolicy
{
    public const bool EnabledByDefault = false;
    public const bool PersistsAcrossLaunches = false;

    public static bool MayStart(bool userRequested, bool occluded) => userRequested && !occluded;

    public static bool ShouldKeepDevice(bool userEnabled, bool expanded, bool showingCamera) =>
        userEnabled && expanded && showingCamera;
}
