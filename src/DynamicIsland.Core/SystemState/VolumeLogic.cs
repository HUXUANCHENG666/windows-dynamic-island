namespace DynamicIsland.Core.SystemState;

public readonly record struct VolumeReading(float Level, bool Muted)
{
    public int Percent => (int)Math.Round(Math.Clamp(Level, 0f, 1f) * 100f);
}

public static class VolumeLogic
{
    public static bool ShouldAnnounce(VolumeReading? previous, VolumeReading current)
    {
        if (previous is not VolumeReading prev)
            return false;
        if (prev.Muted != current.Muted)
            return true;
        return Math.Abs(prev.Level - current.Level) >= 0.01f;
    }
}
