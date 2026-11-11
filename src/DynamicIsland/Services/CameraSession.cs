using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

sealed class CameraSession : IDisposable
{
    readonly Dispatcher _dispatcher;
    MediaCapture? _capture;
    MediaFrameReader? _reader;
    DateTime _lastFrameUtc;
    int _closed;

    public CameraSession(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public bool IsOpen { get; private set; }
    public event Action<byte[], int, int>? Frame;

    public async Task<string?> StartAsync()
    {
        Release();
        Interlocked.Exchange(ref _closed, 0);
        DeviceInformationCollection devices;
        try
        {
            devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        }
        catch (Exception ex)
        {
            Log.Error("camera-devices", ex);
            return "没有找到摄像头。";
        }

        if (devices.Count == 0)
            return "没有找到摄像头。";

        var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = devices[0].Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            });
        }
        catch (UnauthorizedAccessException)
        {
            capture.Dispose();
            return "没有摄像头权限。请打开「设置 → 隐私和安全性 → 摄像头」，允许访问，并允许桌面应用使用摄像头。";
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            capture.Dispose();
            return "没有摄像头权限。请打开「设置 → 隐私和安全性 → 摄像头」，允许访问，并允许桌面应用使用摄像头。";
        }
        catch (Exception ex)
        {
            Log.Error("camera-init", ex);
            capture.Dispose();
            return "打不开摄像头。它可能正被别的程序使用。";
        }

        if (Volatile.Read(ref _closed) == 1)
        {
            capture.Dispose();
            return null;
        }

        var source = capture.FrameSources.Values.FirstOrDefault(item => item.SupportedFormats.Count > 0);
        if (source == null)
        {
            capture.Dispose();
            return "这台摄像头没有可用的画面。";
        }

        MediaFrameFormat? pick = null;
        int best = int.MaxValue;
        foreach (var format in source.SupportedFormats)
        {
            var video = format.VideoFormat;
            if (video == null || video.Width < 160 || video.Height < 120)
                continue;
            int pixels = (int)video.Width * (int)video.Height;
            if (pixels is < (160 * 120) or > (640 * 480))
                continue;
            if (pixels < best)
            {
                best = pixels;
                pick = format;
            }
        }

        try
        {
            if (pick != null)
                await source.SetFormatAsync(pick);
            var reader = await capture.CreateFrameReaderAsync(source);
            reader.FrameArrived += OnFrame;
            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                reader.FrameArrived -= OnFrame;
                reader.Dispose();
                capture.Dispose();
                return "摄像头没有开始预览。";
            }

            if (Volatile.Read(ref _closed) == 1)
            {
                reader.FrameArrived -= OnFrame;
                reader.Dispose();
                capture.Dispose();
                return null;
            }

            _capture = capture;
            _reader = reader;
            if (Volatile.Read(ref _closed) == 1)
            {
                Release();
                return null;
            }

            IsOpen = true;
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("camera-start", ex);
            capture.Dispose();
            return "摄像头预览没有启动。";
        }
    }

    void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (!IsOpen)
            return;
        var now = DateTime.UtcNow;
        if (now - _lastFrameUtc < TimeSpan.FromMilliseconds(80))
        {
            using var skipped = sender.TryAcquireLatestFrame();
            return;
        }

        using var frame = sender.TryAcquireLatestFrame();
        var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap == null)
            return;
        _lastFrameUtc = now;
        if (!TryCopy(bitmap, out var pixels, out int width, out int height))
            return;
        _dispatcher.BeginInvoke(() =>
        {
            if (IsOpen)
                Frame?.Invoke(pixels, width, height);
        });
    }

    static bool TryCopy(SoftwareBitmap bitmap, out byte[] pixels, out int width, out int height)
    {
        pixels = Array.Empty<byte>();
        width = bitmap.PixelWidth;
        height = bitmap.PixelHeight;
        SoftwareBitmap? converted = null;
        try
        {
            var source = bitmap;
            if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                source = converted;
                width = source.PixelWidth;
                height = source.PixelHeight;
            }

            uint size = (uint)(width * height * 4);
            if (size == 0 || size > 8_000_000)
                return false;
            var buffer = new Windows.Storage.Streams.Buffer(size);
            source.CopyToBuffer(buffer);
            pixels = new byte[size];
            var reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(pixels);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("camera-frame", ex);
            return false;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    public void Release()
    {
        Interlocked.Exchange(ref _closed, 1);
        IsOpen = false;
        var reader = _reader;
        _reader = null;
        if (reader != null)
        {
            reader.FrameArrived -= OnFrame;
            try { _ = reader.StopAsync(); } catch { }
            try { reader.Dispose(); } catch { }
        }

        var capture = _capture;
        _capture = null;
        if (capture != null)
        {
            try { capture.Dispose(); } catch { }
        }
    }

    public void Dispose() => Release();
}
