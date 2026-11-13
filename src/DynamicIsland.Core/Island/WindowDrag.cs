namespace DynamicIsland.Core.Island;

/// <summary>
/// 无边框窗口松手以后，把整扇窗拉回它所在屏幕的工作区。
/// 坐标是物理像素，和 GetWindowRect 一致。
/// </summary>
public static class WindowDrag
{
    public static PixelRect KeepVisible(int x, int y, int width, int height, IReadOnlyList<TrayScreen> screens)
    {
        if (width < 1)
            width = 1;
        if (height < 1)
            height = 1;
        if (screens.Count == 0)
            return new PixelRect(x, y, width, height);

        int centerX = x + width / 2;
        int centerY = y + height / 2;
        var screen = ScreenUnder(centerX, centerY, screens) ?? Nearest(centerX, centerY, screens);
        int left = screen.WorkX;
        int top = screen.WorkY;
        int right = screen.WorkX + Math.Max(0, screen.WorkWidth);
        int bottom = screen.WorkY + Math.Max(0, screen.WorkHeight);

        int nextX = width >= screen.WorkWidth ? left : Math.Clamp(x, left, Math.Max(left, right - width));
        int nextY = height >= screen.WorkHeight ? top : Math.Clamp(y, top, Math.Max(top, bottom - height));
        return new PixelRect(nextX, nextY, width, height);
    }

    static TrayScreen? ScreenUnder(int x, int y, IReadOnlyList<TrayScreen> screens)
    {
        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            if (x >= screen.X && x < screen.X + screen.Width && y >= screen.Y && y < screen.Y + screen.Height)
                return screen;
        }

        return null;
    }

    static TrayScreen Nearest(int x, int y, IReadOnlyList<TrayScreen> screens)
    {
        var best = screens[0];
        long bestDistance = long.MaxValue;
        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            long dx = x < screen.X ? screen.X - x : x >= screen.X + screen.Width ? x - (screen.X + screen.Width - 1) : 0;
            long dy = y < screen.Y ? screen.Y - y : y >= screen.Y + screen.Height ? y - (screen.Y + screen.Height - 1) : 0;
            long distance = dx * dx + dy * dy;
            if (distance < bestDistance || (distance == bestDistance && screen.IsPrimary && !best.IsPrimary))
            {
                bestDistance = distance;
                best = screen;
            }
        }

        return best;
    }
}
