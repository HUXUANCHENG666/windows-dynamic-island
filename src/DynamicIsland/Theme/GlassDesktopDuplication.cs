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
                return false;
            if (_dup == null || _device == null || _context == null)
                return false;
            if (!DynamicIsland.Core.Motion.GlassCaptureBounds.TryClipToOutput(
                    destLeft, destTop, destW, destH,
                    _outputLeft, _outputTop, _outputRight, _outputBottom,
                    out int srcX, out int srcY, out int destX, out int destY, out int copyW, out int copyH))
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
            if (destX != 0 || destY != 0 || copyW != destW || copyH != destH)
                Array.Clear(dest, 0, destStride * destH);
            EnsureStaging(copyW,copyH);
            if (_staging == null || _desktop == null) return false;
            _context.CopySubresourceRegion(_staging,0,0,0,0,_desktop,0,new Box(srcX,srcY,0,srcX+copyW,srcY+copyH,1));
            MappedSubresource mapped=_context.Map(_staging,0,MapMode.Read,MapFlags.None);
            try
            {
                unsafe
                {
                    byte* rows=(byte*)mapped.DataPointer;
                    for(int y=0;y<copyH;y++)
                    {
                        int* src=(int*)(rows+y*(int)mapped.RowPitch);
                        int di=(y+destY)*destStride+destX;
                        for(int x=0;x<copyW;x++) dest[di+x]=src[x] | unchecked((int)0xFF000000);
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
        // 部分越界仍保留当前输出会话并在 TryCapture 里裁切，避免快拖贴边就拆掉 DDA、冷却 10 秒改走 GDI。
        if (_dup != null && DynamicIsland.Core.Motion.GlassCaptureBounds.Intersects(
                left, top, w, h, _outputLeft, _outputTop, _outputRight, _outputBottom))
            return true;
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
        IDXGIOutput1? best = null;
        int bestArea = 0, bestL = 0, bestT = 0, bestR = 0, bestB = 0;
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
                int area = DynamicIsland.Core.Motion.GlassCaptureBounds.IntersectionArea(
                    left, top, w, h, rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (area <= bestArea)
                    continue;
                var output1 = output.QueryInterfaceOrNull<IDXGIOutput1>();
                if (output1 == null)
                    continue;
                best?.Dispose();
                best = output1;
                bestArea = area;
                bestL = rect.Left; bestT = rect.Top; bestR = rect.Right; bestB = rect.Bottom;
            }
        }
        if (best == null)
            return false;
        try
        {
            var next = best.DuplicateOutput(_device);
            if (next == null)
                return false;
            DisposeDup();
            _dup = next;
            _outputLeft = bestL; _outputTop = bestT; _outputRight = bestR; _outputBottom = bestB;
            return true;
        }
        catch
        {
            return false;
        }
        finally { best.Dispose(); }
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
