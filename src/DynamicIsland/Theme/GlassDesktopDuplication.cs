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
    ID3D11Texture2D? _staging, _desktop;
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

            // Zero-timeout latest-desktop acquisition: unchanged desktop is not a failed frame.
            Result acquired = _dup.AcquireNextFrame(0, out _, out IDXGIResource? resource);
            if (acquired.Success && resource != null)
            {
                try
                {
                    using var texture=resource.QueryInterface<ID3D11Texture2D>();
                    if (_desktop == null)
                    {
                        var desc=texture.Description;
                        // Owned GPU copy remains valid after ReleaseFrame; never retain the borrowed resource.
                        desc.Usage=ResourceUsage.Default; desc.BindFlags=BindFlags.None;
                        desc.CPUAccessFlags=CpuAccessFlags.None; desc.MiscFlags=ResourceOptionFlags.None;
                        _desktop=_device.CreateTexture2D(desc);
                    }
                    _context.CopyResource(_desktop,texture);
                }
                finally { resource.Dispose(); _dup.ReleaseFrame(); }
            }
            else
            {
                resource?.Dispose();
                if (acquired.Code != unchecked((int)0x887A0027)) // DXGI_ERROR_WAIT_TIMEOUT
                    acquired.CheckError(); // device/access loss clears cached desktop in catch
                if (_desktop == null) return false;
            }
            int srcX=destLeft-_outputLeft, srcY=destTop-_outputTop;
            EnsureStaging(destW,destH);
            if (_staging == null || _desktop == null) return false;
            _context.CopySubresourceRegion(_staging,0,0,0,0,_desktop,0,new Box(srcX,srcY,0,srcX+destW,srcY+destH,1));
            MappedSubresource mapped=_context.Map(_staging,0,MapMode.Read,MapFlags.None);
            try
            {
                unsafe
                {
                    byte* rows=(byte*)mapped.DataPointer;
                    for(int y=0;y<destH;y++)
                    {
                        int* src=(int*)(rows+y*(int)mapped.RowPitch);
                        for(int x=0;x<destW;x++) dest[y*destStride+x]=src[x] | unchecked((int)0xFF000000);
                    }
                }
            }
            finally { _context.Unmap(_staging,0); }
            return true;
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
                // Rotation needs a separate coordinate transform; prefer correct GDI fallback to rotated pixels.
                if ((int)output.Description.Rotation != 1 || (long)(rect.Right-rect.Left)*(rect.Bottom-rect.Top)>16_777_216)
                    continue; // owned desktop texture cap 64 MiB (BGRA8)
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
        if (_staging != null && _stageW >= w && _stageH >= h)
            return;
        _staging?.Dispose();
        _staging = null;
        var desc = new Texture2DDescription
        {
            Width = (uint)(((w+63)/64)*64),
            Height = (uint)(((h+63)/64)*64),
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
        _stageW = (int)desc.Width;
        _stageH = (int)desc.Height;
    }

    void DisposeDup()
    {
        _desktop?.Dispose(); _desktop=null;
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
