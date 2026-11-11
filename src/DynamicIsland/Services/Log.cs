using IOPath = System.IO.Path;

namespace DynamicIsland.Services;

/// <summary>
/// 一直拿着日志文件的句柄追加，不要每条都开一次文件。
/// 通知到达和位置变化都会写日志，忙的时候开合开销很明显。
/// </summary>
static class Log
{
    const long MaxBytes = 200_000;

    static readonly object Gate = new();
    static StreamWriter? _writer;
    static long _written;

    public static string DirectoryPath =>
        IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");

    public static string FilePath => IOPath.Combine(DirectoryPath, "log.txt");

    public static void Info(string message) => Write("INF", message);

    public static void Error(string area, Exception exception) =>
        Write("ERR", area + " " + exception.ToString().ReplaceLineEndings(" | "));

    public static void Close()
    {
        lock (Gate)
        {
            try { _writer?.Dispose(); }
            catch { /* 关闭失败不影响退出。 */ }
            _writer = null;
            _written = 0;
        }
    }

    static void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}";
            lock (Gate)
            {
                if (_writer == null || _written > MaxBytes)
                    Reopen();
                _writer!.Write(line);
                _written += line.Length;
            }
        }
        catch
        {
            // 记日志不能把岛带崩。句柄坏了下次写的时候重建。
            Close();
        }
    }

    static void Reopen()
    {
        try { _writer?.Dispose(); }
        catch { /* 旧句柄已经不可用，换一个。 */ }
        _writer = null;

        Directory.CreateDirectory(DirectoryPath);
        var info = new FileInfo(FilePath);
        if (info.Exists && info.Length > MaxBytes)
            File.Delete(FilePath);

        var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream) { AutoFlush = true };
        _written = stream.Length;
    }
}
