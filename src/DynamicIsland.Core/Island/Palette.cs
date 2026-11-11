namespace DynamicIsland.Core.Island;

public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255);

/// <summary>
/// 颜色只改这一处。Windows 界面和预览图都读这里。
/// 岛身是近黑而不是死黑：顶部有一点点高光，看起来像一块物体，而不是一块贴纸。
/// </summary>
public static class Palette
{
    public static readonly Rgba Title = new(255, 255, 255);
    public static readonly Rgba Artist = new(255, 255, 255, 166);
    public static readonly Rgba Quiet = new(255, 255, 255, 128);
    public static readonly Rgba Faint = new(255, 255, 255, 72);
    public static readonly Rgba Track = new(255, 255, 255, 46);
    public static readonly Rgba Hover = new(255, 255, 255, 32);
    public static readonly Rgba Chip = new(255, 255, 255, 24);
    public static readonly Rgba ChipHover = new(255, 255, 255, 48);
    public static readonly Rgba Hairline = new(255, 255, 255, 40);
    public static readonly Rgba Placeholder = new(28, 28, 30);
    public static readonly Rgba FillTop = new(24, 24, 24);
    public static readonly Rgba FillMid = new(8, 8, 8);
    public static readonly Rgba FillBottom = new(0, 0, 0);
    public static readonly Rgba Danger = new(255, 69, 58);
    public static readonly Rgba Ink = new(0, 0, 0);
    public static readonly Rgba Shadow = new(0, 0, 0, 128);
}
