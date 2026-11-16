using System.Net.Http;
using DynamicIsland.Core.LiveTranslate;
using IOPath = System.IO.Path;

namespace DynamicIsland.Services;

/// <summary>
/// 把 Whisper 的 ggml 模型缓存在本机 LocalAppData，不放进安装包或仓库。
/// 先走 Hugging Face 官方地址，失败再试国内镜像。下载过程可取消。
/// </summary>
static class WhisperModelStore
{
    public const string FileName = "ggml-base-q5_1.bin";
    public const long MinBytes = 40_000_000;
    public const long MaxBytes = 90_000_000;

    public static readonly string[] Sources =
    {
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin",
        "https://huggingface.co/sandrohanea/whisper.net/resolve/v5/q5_1/ggml-base.bin",
        "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin"
    };

    public static string DirectoryPath =>
        IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "models");

    public static string FilePath => IOPath.Combine(DirectoryPath, FileName);

    public static bool IsReady()
    {
        try
        {
            var info = new FileInfo(FilePath);
            return info.Exists && info.Length >= MinBytes && info.Length <= MaxBytes;
        }
        catch
        {
            return false;
        }
    }

    public static async Task DownloadAsync(Action<double, long, long?> progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (IsReady())
        {
            var ready = new FileInfo(FilePath);
            progress(1, ready.Length, ready.Length);
            return;
        }

        Exception? last = null;
        foreach (var url in Sources)
        {
            cancellation.ThrowIfCancellationRequested();
            var partial = FilePath + ".partial";
            try
            {
                if (File.Exists(partial))
                    File.Delete(partial);
                await PullAsync(url, partial, progress, cancellation).ConfigureAwait(false);
                var info = new FileInfo(partial);
                if (!info.Exists || info.Length < MinBytes || info.Length > MaxBytes)
                    throw new InvalidOperationException(LiveTranslateCopy.DownloadFailed);
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
                File.Move(partial, FilePath);
                return;
            }
            catch (OperationCanceledException)
            {
                TryDelete(partial);
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                TryDelete(partial);
                Log.Error("live-translate-model", ex);
            }
        }

        throw last ?? new InvalidOperationException(LiveTranslateCopy.DownloadFailed);
    }

    public static void TryDeleteCorrupt()
    {
        try
        {
            if (File.Exists(FilePath) && !IsReady())
                File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            Log.Error("live-translate-model", ex);
        }
    }

    static async Task PullAsync(string url, string destination, Action<double, long, long?> progress, CancellationToken cancellation)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 85_000, useAsync: true);
        var buffer = new byte[85_000];
        long received = 0;
        var lastReport = DateTime.UtcNow;
        progress(LiveTranslateCopy.Progress(0, total), 0, total);
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellation).ConfigureAwait(false);
            if (read <= 0)
                break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
            received += read;
            var now = DateTime.UtcNow;
            if (now - lastReport < TimeSpan.FromMilliseconds(200) && (total is null || received < total))
                continue;
            lastReport = now;
            progress(LiveTranslateCopy.Progress(received, total), received, total);
        }

        await output.FlushAsync(cancellation).ConfigureAwait(false);
        progress(LiveTranslateCopy.Progress(received, received), received, received);
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 半截文件删不掉也不要打断取消或下一次尝试。
        }
    }
}
