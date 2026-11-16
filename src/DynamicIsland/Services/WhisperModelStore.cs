using System.Net;
using System.Net.Http;
using DynamicIsland.Core.LiveTranslate;
using IOPath = System.IO.Path;

namespace DynamicIsland.Services;

/// <summary>
/// 把 Whisper 的 ggml 模型缓存在本机 LocalAppData，不放进安装包或仓库。
/// 先走国内镜像，失败或长时间没进度再试官方地址。下载过程可取消。
/// </summary>
static class WhisperModelStore
{
    public const string FileName = "ggml-base-q5_1.bin";
    public const long MinBytes = 40_000_000;
    public const long MaxBytes = 90_000_000;
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(20);

    // 国内访问 huggingface.co 经常会一直挂住而不报错，所以镜像放在最前，并给连接/停顿设短超时。
    public static readonly string[] Sources =
    {
        "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin",
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin",
        "https://huggingface.co/sandrohanea/whisper.net/resolve/v5/q5_1/ggml-base.bin"
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

    public static async Task DownloadAsync(Action<string, double> progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (IsReady())
        {
            progress(LiveTranslateCopy.Downloading(1), 1);
            return;
        }

        Exception? last = null;
        bool anyAttempted = false;
        foreach (var url in Sources)
        {
            cancellation.ThrowIfCancellationRequested();
            progress(anyAttempted ? LiveTranslateCopy.SwitchingSource : LiveTranslateCopy.Connecting, 0);
            anyAttempted = true;
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

    static async Task PullAsync(string url, string destination, Action<string, double> progress, CancellationToken cancellation)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = ConnectTimeout,
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };
        using var http = new HttpClient(handler) { Timeout = TransferTimeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0");
        http.DefaultRequestVersion = HttpVersion.Version11;
        http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        using var response = await GetHeadersAsync(http, url, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 85_000, useAsync: true);
        var buffer = new byte[85_000];
        long received = 0;
        var lastReport = DateTime.UtcNow;
        Report(progress, received, total);
        while (true)
        {
            int read = await ReadWithStallAsync(input, buffer, cancellation).ConfigureAwait(false);
            if (read <= 0)
                break;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
            received += read;
            var now = DateTime.UtcNow;
            if (now - lastReport < TimeSpan.FromMilliseconds(200) && (total is null || received < total))
                continue;
            lastReport = now;
            Report(progress, received, total);
        }

        await output.FlushAsync(cancellation).ConfigureAwait(false);
        Report(progress, received, received);
    }

    static void Report(Action<string, double> progress, long received, long? total)
    {
        double fraction = LiveTranslateCopy.Progress(received, total);
        progress(LiveTranslateCopy.Downloading(fraction, received, total), fraction);
    }

    static async Task<HttpResponseMessage> GetHeadersAsync(HttpClient http, string url, CancellationToken cancellation)
    {
        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        headerCts.CancelAfter(HeaderTimeout);
        try
        {
            return await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headerCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("连接下载源超时。");
        }
    }

    static async Task<int> ReadWithStallAsync(Stream input, byte[] buffer, CancellationToken cancellation)
    {
        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stallCts.CancelAfter(StallTimeout);
        try
        {
            return await input.ReadAsync(buffer.AsMemory(0, buffer.Length), stallCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TimeoutException("下载长时间没有进度。");
        }
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
