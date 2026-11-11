namespace DynamicIsland.Core.Island;

/// <summary>
/// 模糊和高帧率只允许在展开时，或展开/收回的弹簧还在走的时候。
/// 收起、小点、停稳之后不再订 CompositionTarget.Rendering，也不挂内容模糊。
/// </summary>
public static class EffectsPolicy
{
    public static bool UseHighFrameRate(bool expanded, bool transitioning, bool liveMotion)
    {
        if (transitioning)
            return true;
        if (!expanded)
            return false;
        return liveMotion;
    }

    public static bool AllowBlur(bool expanded, bool transitioning) => expanded || transitioning;
}
