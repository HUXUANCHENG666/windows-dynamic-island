namespace DynamicIsland.Core.Island;

/// <summary>
/// 24×24 网格上的图标路径。自己画，不依赖 Segoe MDL2，避免字库缺字时变成方框。
/// </summary>
public static class Glyphs
{
    public const string Play = "M8.2,4.6 L19.4,12 L8.2,19.4 Z";
    public const string Pause = "M7,4.8 H10.3 V19.2 H7 Z M13.7,4.8 H17 V19.2 H13.7 Z";
    public const string Next = "M5.2,5 L14.4,12 L5.2,19 Z M16.2,5 H19 V19 H16.2 Z";
    public const string Previous = "M18.8,5 L9.6,12 L18.8,19 Z M5,5 H7.8 V19 H5 Z";
    public const string Speaker = "M3.2,9 H7.4 L12.6,4.8 V19.2 L7.4,15 H3.2 Z";
    public const string SpeakerWave = "M15.4,9.1 C16.9,10.3 16.9,13.7 15.4,14.9 M17.6,6.8 C20.6,9.1 20.6,14.9 17.6,17.2";
    public const string Slash = "M5,5 L19,19";
    public const string Bell = "M12,3.4 C8.7,3.4 6.8,6.2 6.8,9.3 V12.8 L4.8,16.4 H19.2 L17.2,12.8 V9.3 C17.2,6.2 15.3,3.4 12,3.4 Z M10.2,17.4 A1.8,1.8 0 0 0 13.8,17.4 A1.8,1.8 0 0 1 10.2,17.4 Z";
    public const string Music = "M10.2,17.6 A2.5,2.5 0 1 1 8,15.2 L8,7.2 L17.2,5.2 V13.6 A2.5,2.5 0 1 1 15,13.2 V8.2 L10.2,9.4 Z";
    public const string ChevronLeft = "M14.6,5.4 L8.4,12 L14.6,18.6";
    public const string ChevronRight = "M9.4,5.4 L15.6,12 L9.4,18.6";
}
