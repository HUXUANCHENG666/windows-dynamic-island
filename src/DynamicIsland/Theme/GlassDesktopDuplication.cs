using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using SharpGen.Runtime;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace DynamicIsland.Visuals;

/// <summary>
/// 公开的 DXGI Desktop Duplication 回退：在 BitBlt 仍带上本窗或给出黑块时，尝试复制已被排除本窗的桌面。
/// 失败必须安静回退，不抛到捕获循环外。
/// </summary>
sealed class GlassDesktopDuplication : IDisposable
{
    ID3D11Device? _device;
    ID3D11DeviceContext? _context;
    IDXGIOutputDuplication? _dup;
    ID3D11Texture2D? _staging;
    int _outputLeft, _outputTop, _outputRight, _outputBottom;
    int _stageW, _stageH;
    DateTime _retry;

    public bool TryCapture(int destLeft, int destTop, int destW, int destH, int[] dest, int destStride)
    {
        if (destW < 1 || destH < 1 || dest.Length < destStride * destH)
            return false;
        if (DateTime.UtcNow < _retry)
            return false;
        try
        {
            if (!EnsureOutput(destLeft, destTop, destW, destH))
            {
                _retry = DateTime.UtcNow.AddSeconds(10);
                return false;
            }
            if (_dup == null || _device == null || _context == null)
                return false;

            Result acquired = _dup.AcquireNextFrame(50, out _, out IDXGIResource? resource);
            if (acquired.Failure || resource == null)
            {
                // 无新帧不算设备死亡；让调用方继续用 BitBlt。
                if (resource != null) resource.Dispose();
                return false;
            }
            try
            {
                using var texture = resource.QueryInterface<ID3D11Texture2D>();
                int srcX = destLeft - _outputLeft, srcY = destTop - _outputTop;
                if (srcX < 0 || srcY < 0 || srcX + destW > _outputRight - _outputLeft || srcY + destH > _outputBottom - _outputTop)
                    return false;
                EnsureStaging(destW, destH);
                if (_staging == null)
                    return false;
                var box = new Box(srcX, srcY, 0, srcX + destW, srcY + destH, 1);
                _context.CopySubresourceRegion(_staging, 0, 0, 0, 0, texture, 0, box);
                MappedSubresource mapped = _context.Map(_staging, 0, MapMode.Read, MapFlags.None);
                try
                {
                    unsafe
                    {
                        byte* rows = (byte*)mapped.DataPointer;
                        int pitch = (int)mapped.RowPitch;
                        for (int y = 0; y < destH; y++)
                        {
                            int* src = (int*)(rows + y * pitch);
                            int destRow = y * destStride;
                            for (int x = 0; x < destW; x++)
                                dest[destRow + x] = src[x] | unchecked((int)0xFF000000);
                        }
                    }
                }
                finally { _context.Unmap(_staging, 0); }
                return true;
            }
            finally
            {
                resource.Dispose();
                _dup.ReleaseFrame();
            }
        }
        catch
        {
            DisposeDevice();
            _retry = DateTime.UtcNow.AddSeconds(10);
            return false;
        }
    }

    bool EnsureOutput(int left, int top, int w, int h)
    {
        int right = left + w, bottom = top + h;
        if (_dup != null && left >= _outputLeft && top >= _outputTop && right <= _outputRight && bottom <= _outputBottom)
            return true;
        DisposeDup();
        if (_device == null)
        {
            var result = D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0, FeatureLevel.Level_10_0], out _device, out _context);
            if (result.Failure || _device == null || _context == null)
            {
                DisposeDevice();
                return false;
            }
        }
        using var dxgi = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgi.GetAdapter();
        for (int i = 0; ; i++)
        {
            Result ev = adapter.EnumOutputs((uint)i, out IDXGIOutput? output);
            if (ev.Failure || output == null)
                break;
            using (output)
            {
                var rect = output.Description.DesktopCoordinates;
                if (left < rect.Left || top < rect.Top || right > rect.Right || bottom > rect.Bottom)
                    continue;
                using var output1 = output.QueryInterfaceOrNull<IDXGIOutput1>();
                if (output1 == null)
                    continue;
                try
                {
                    _dup = output1.DuplicateOutput(_device);
                }
                catch
                {
                    _dup = null;
                    continue;
                }
                if (_dup == null)
                    continue;
                _outputLeft = rect.Left;
                _outputTop = rect.Top;
                _outputRight = rect.Right;
                _outputBottom = rect.Bottom;
                return true;
            }
        }
        return false;
    }

    void EnsureStaging(int w, int h)
    {
        if (_device == null)
            return;
        if (_staging != null && _stageW == w && _stageH == h)
            return;
        _staging?.Dispose();
        _staging = null;
        var desc = new Texture2DDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };
        _staging = _device.CreateTexture2D(desc);
        _stageW = w;
        _stageH = h;
    }

    void DisposeDup()
    {
        _dup?.Dispose();
        _dup = null;
        _staging?.Dispose();
        _staging = null;
        _stageW = _stageH = 0;
    }

    void DisposeDevice()
    {
        DisposeDup();
        _context?.Dispose();
        _device?.Dispose();
        _context = null;
        _device = null;
    }

    public void Dispose() => DisposeDevice();
}
