namespace DynamicIsland.Core.Media;

public enum PlaybackPhase
{
    None,
    Open,
    Playing
}

public static class PlaybackMap
{
    public static PlaybackPhase Map(string? status)
    {
        return status switch
        {
            "Playing" => PlaybackPhase.Playing,
            "Paused" or "Changing" => PlaybackPhase.Open,
            _ => PlaybackPhase.None
        };
    }
}
