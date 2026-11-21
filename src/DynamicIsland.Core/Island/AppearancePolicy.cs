namespace DynamicIsland.Core.Island;

public enum IslandSkin
{
    Default,
    Glass
}

/// <summary>
/// 岛的大小和皮肤会记住。大小按 5% 一档，限制在 85% 到 140%。
/// 默认皮肤仍是原来的近黑实心，液态玻璃要用户自己选。
/// </summary>
public static class AppearancePolicy
{
    public const double DefaultScale = 1.00;
    public const double MinScale = 0.85;
    public const double MaxScale = 1.40;
    public const double ScaleStep = 0.05;
    public const string DefaultSkinSetting = "default";
    public const string GlassSkinSetting = "glass";

    public static double ClampScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale))
            return DefaultScale;
        double clamped = Math.Clamp(scale, MinScale, MaxScale);
        double snapped = Math.Round(clamped / ScaleStep, MidpointRounding.AwayFromZero) * ScaleStep;
        snapped = Math.Round(snapped, 2);
        return Math.Clamp(snapped, MinScale, MaxScale);
    }

    public static int ToPercent(double scale) =>
        (int)Math.Round(ClampScale(scale) * 100);

    public static double FromPercent(double percent) =>
        ClampScale(percent / 100.0);

    public static IslandSkin ParseSkin(string? value) =>
        string.Equals(value, GlassSkinSetting, StringComparison.OrdinalIgnoreCase)
            ? IslandSkin.Glass
            : IslandSkin.Default;

    public static string ToSetting(IslandSkin skin) =>
        skin == IslandSkin.Glass ? GlassSkinSetting : DefaultSkinSetting;

    public static void Sanitize(Settings.AppSettings settings)
    {
        if (settings is null)
            return;
        settings.IslandScale = ClampScale(settings.IslandScale);
        settings.IslandSkin = ToSetting(ParseSkin(settings.IslandSkin));
    }
}
