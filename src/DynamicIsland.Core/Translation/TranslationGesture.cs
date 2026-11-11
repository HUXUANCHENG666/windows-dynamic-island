namespace DynamicIsland.Core.Translation;

public enum TranslationHit
{
    None,
    ResultText,
    CopyControl,
    DismissControl
}

/// <summary>
/// 译文上的选字和复制不能把岛拖走，也不能因为指针离开就收起。
/// 点在译文上才允许窗口短暂拿焦点，好让 Ctrl+C 作用在这段文字上。
/// </summary>
public static class TranslationGesture
{
    public static bool ShouldArmDrag(TranslationHit hit, bool selecting) =>
        !selecting
        && hit is not TranslationHit.ResultText
        && hit is not TranslationHit.CopyControl
        && hit is not TranslationHit.DismissControl;

    public static bool AllowsFocusForSelection(TranslationHit hit) =>
        hit == TranslationHit.ResultText;

    /// <summary>
    /// 译文正开着、还没复制时，指针离开不能收起。
    /// 正在拖选时，就算已经复制过，也不能收起。
    /// </summary>
    public static bool ShouldHoldAgainstPointerLeave(bool resultOpen, bool copied, bool selecting) =>
        selecting || (resultOpen && !copied);
}
