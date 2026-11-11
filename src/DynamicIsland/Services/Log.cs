using IOPath = System.IO.Path;

namespace DynamicIsland.Services;

static class Log
{
    static readonly object Gate = new();

    public static string DirectoryPath =>
        IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");

    public static string FilePath => IOPath.Combine(DirectoryPath, "log.txt");

    public static void Info(string message) => Write("INF", message);

    public static void Error(string area, Exception exception) =>
        Write("ERR", area + " " + exception.ToString().ReplaceLineEndings(" | "));

    static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}";
            lock (Gate)
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 200_000)
                    File.Delete(FilePath);
                File.AppendAllText(FilePath, line);
            }
        }
        catch
        {
            // Logging must never take the island down with it.
        }
    }
}
