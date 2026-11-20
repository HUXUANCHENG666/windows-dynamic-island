using System.Runtime.InteropServices;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.Motion;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace DynamicIsland.Visuals;

/// <summary>
/// 用 D3D11 在显卡上做局部多点边缘折射和高光（RGB 共用位置，不做色散）。初始化失败或设备丢失时由调用方退回 CPU。
/// </summary>
sealed class GlassGpuRenderer : IDisposable
{
    ID3D11Device? _device;
    ID3D11DeviceContext? _context;
    ID3D11VertexShader? _vs;
    ID3D11PixelShader? _ps;
    ID3D11SamplerState? _sampler;
    ID3D11Buffer? _constants;
    ID3D11Texture2D? _padTex;
    ID3D11ShaderResourceView? _padView;
    ID3D11Texture2D? _fieldTex;
    ID3D11ShaderResourceView? _fieldView;
    ID3D11Texture2D? _target;
    ID3D11RenderTargetView? _rtv;
    sealed class Readback
    {
        public ID3D11Texture2D? Texture;
        public bool Pending;
        public int Width, Height;
        public long Sequence, Started;
    }
    readonly Readback[] _readbacks = { new(), new(), new() };
    long _sequence;
    GlassPixels? _frame;
    long _lastPadVersion;
    float[] _heights = Array.Empty<float>();
    public bool Skipped { get; private set; }
    public int Dropped { get; private set; }
    int _padW, _padH, _fieldW, _fieldH, _outW, _outH;
    bool _lost;
    bool _disposed;

    public bool IsAvailable => !_disposed && !_lost && _device != null && _context != null && _ps != null;
    public GlassPixels? TakeFrame() { var frame = _frame; _frame = null; return frame; }

    public static GlassGpuRenderer? TryCreate()
    {
        var gpu = new GlassGpuRenderer();
        try { if (gpu.Initialize()) return gpu; }
        catch { /* 不让编译器/设备初始化打掉窗口。 */ }
        gpu.Dispose();
        return null;
    }

#if GLASS_VERIFICATION
    // 仅 headless 验证项目编译此入口；正式程序绝不把 WARP 当硬件路径。
    public static GlassGpuRenderer? TryCreateWarpForVerification()
    {
        var gpu = new GlassGpuRenderer();
        try { if (gpu.Initialize(warp: true)) return gpu; }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        gpu.Dispose(); return null;
    }
#endif

    bool Initialize(bool warp = false)
    {
        if (!TryCreateDevice(out _device, out _context, warp) || _device == null || _context == null)
            return false;

        bool shader5 = (int)_device.FeatureLevel >= (int)FeatureLevel.Level_11_0;
        if (!TryCompile("VSMain", shader5 ? "vs_5_0" : "vs_4_0", out var vsBlob) && !TryCompile("VSMain", "vs_4_0", out vsBlob))
            return false;
        if (!TryCompile("PSMain", shader5 ? "ps_5_0" : "ps_4_0", out var psBlob) && !TryCompile("PSMain", "ps_4_0", out psBlob))
        {
            vsBlob?.Dispose();
            return false;
        }

        if (vsBlob == null || psBlob == null)
            return false;
        _vs = _device.CreateVertexShader(vsBlob, null);
        _ps = _device.CreatePixelShader(psBlob, null);
        vsBlob.Dispose();
        psBlob.Dispose();

        _sampler = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MaxLOD = float.MaxValue,
            MinLOD = 0,
            MaxAnisotropy = 1,
            ComparisonFunc = ComparisonFunction.Never
        });

        _constants = _device.CreateBuffer(
            (uint)((Marshal.SizeOf<GpuConstants>() + 15) / 16 * 16),
            BindFlags.ConstantBuffer,
            ResourceUsage.Dynamic,
            CpuAccessFlags.Write,
            ResourceOptionFlags.None,
            0);

        return _vs != null && _ps != null && _sampler != null && _constants != null;
    }

    static bool TryCreateDevice(out ID3D11Device? device, out ID3D11DeviceContext? context, bool warp = false)
    {
        device = null;
        context = null;
        FeatureLevel[] levels =
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0
        };
        var flags = DeviceCreationFlags.BgraSupport;
        var result = D3D11.D3D11CreateDevice(
            IntPtr.Zero,
            warp ? DriverType.Warp : DriverType.Hardware,
            flags,
            levels,
            out device,
            out context);
        if (result.Success && device != null && context != null)
            return true;

        device?.Dispose();
        context?.Dispose();
        device = null;
        context = null;

        if (warp) return false;
        IDXGIFactory1? factory;
        try
        {
            factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        }
        catch
        {
            return false;
        }

        using (factory)
        {
            for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1 adapter).Success; i++)
            {
                using (adapter)
                {
                    AdapterDescription1 desc = adapter.Description1;
                    if ((desc.Flags & AdapterFlags.Software) != 0)
                        continue;
                    result = D3D11.D3D11CreateDevice(
                        adapter,
                        DriverType.Unknown,
                        flags,
                        levels,
                        out device,
                        out context);
                    if (result.Success && device != null && context != null)
                        return true;
                    device?.Dispose();
                    context?.Dispose();
                    device = null;
                    context = null;
                }
            }
        }

        return false;
    }

    static bool TryCompile(string entry, string profile, out Blob? blob)
    {
        blob = null;
        Result result = Compiler.Compile(
            ShaderSource, Array.Empty<ShaderMacro>(), null!, entry, "GlassGpu.hlsl", profile,
            ShaderFlags.OptimizationLevel3, EffectFlags.None, out blob, out Blob? errors);
        errors?.Dispose();
        return result.Success && blob != null;
    }

    public bool TryPresent(
        int[]? pad,
        int padW,
        int padH,
        int islandW,
        int islandH,
        int sourceIslandW,
        int sourceIslandH,
        double radius,
        LiquidSurface? surface,
        bool moving, double dpi = 1, double sourceRadius = 0, double shiftX = 0, double shiftY = 0, long started = 0, long captureVersion = 0, double scaleX = 1, double scaleY = 1)
    {
        if (!IsAvailable || islandW < 8 || islandH < 8)
            return false;

        try
        {
            Skipped = false; Dropped = 0;
            TryReadback(); // DO_NOT_WAIT：上一帧未完成则不阻塞、不重试自旋。
            var (outW, outH) = OutputSize(islandW, islandH, moving);
            EnsureTarget(outW, outH);
            Readback? slot = null;
            foreach (var candidate in _readbacks) if (!candidate.Pending) { slot = candidate; break; }
            if (slot == null) { Skipped = true; return true; }
            bool hasPad = pad != null && padW >= 2 && padH >= 2 && pad!.Length >= padW * padH;
            if (hasPad && (captureVersion == 0 || captureVersion != _lastPadVersion))
            { UploadPad(pad!, padW, padH); _lastPadVersion = captureVersion; }
            UploadField(surface);

            if (_target == null || _rtv == null || _constants == null || _context == null)
                return false;

            var data = new GpuConstants
            {
                IslandW = islandW,
                IslandH = islandH,
                Radius = (float)(radius < 0 ? Math.Min(islandW, islandH) * 0.5 : radius),
                Pad = LiquidRefraction.DefaultPad,
                SrcW = hasPad ? sourceIslandW : islandW,
                SrcH = hasPad ? sourceIslandH : islandH,
                SrcPadW = hasPad ? _padW : 1,
                SrcPadH = hasPad ? _padH : 1,
                FieldCols = surface?.Columns ?? 0,
                FieldRows = surface?.Rows ?? 0,
                HasPad = hasPad ? 1 : 0,
                HasField = surface != null && surface.Columns > 0 ? 1 : 0,
                RimWidth = (float)Core.Geometry.IslandOutline.RimWidthDip,
                Chromatic = (float)LiquidRefraction.Chromatic,
                HeightWarp = (float)LiquidRefraction.HeightWarp,
                SlopeWarp = (float)LiquidRefraction.SlopeWarp,
                Dpi = (float)dpi, SrcRadius = (float)sourceRadius, ShiftX = (float)shiftX, ShiftY = (float)shiftY,
                ScaleX = (float)scaleX, ScaleY = (float)scaleY
            };
            WriteConstants(data);

            _context.OMSetRenderTargets(_rtv, null!);
            _context.RSSetViewport(0, 0, outW, outH, 0, 1);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.VSSetShader(_vs);
            _context.PSSetShader(_ps);
            _context.PSSetConstantBuffer(0, _constants);
            _context.PSSetSampler(0, _sampler);
            if (hasPad && _padView != null)
                _context.PSSetShaderResource(0, _padView);
            else
                _context.PSUnsetShaderResource(0);
            if (_fieldView != null)
                _context.PSSetShaderResource(1, _fieldView);
            else
                _context.PSUnsetShaderResource(1);
            _context.Draw(3, 0);
            _context.PSUnsetShaderResource(0);
            _context.PSUnsetShaderResource(1);
            _context.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>(), null);
            _context.CopySubresourceRegion(slot.Texture!, 0, 0, 0, 0, _target, 0, new Box(0, 0, 0, outW, outH, 1));
            slot.Pending = true; slot.Width = outW; slot.Height = outH;
            slot.Sequence = ++_sequence; slot.Started = started;
            _context.Flush(); // 提交命令，不等待 GPU；让后续非阻塞 Map 有进展。
            return true;
        }
        catch (SharpGenException)
        {
            _lost = true;
            return false;
        }
    }

    void WriteConstants(in GpuConstants data)
    {
        MappedSubresource mapped = _context!.Map(_constants!, 0, MapMode.WriteDiscard, MapFlags.None);
        try
        {
            Marshal.StructureToPtr(data, mapped.DataPointer, false);
        }
        finally
        {
            _context.Unmap(_constants, 0);
        }
    }

    void EnsureTarget(int width, int height)
    {
        if (_target != null && _outW >= width && _outH >= height)
            return;
        DisposeTarget();
        _outW = ((width + 63) / 64) * 64;
        _outH = ((height + 63) / 64) * 64;
        var desc = new Texture2DDescription
        {
            Width = (uint)_outW,
            Height = (uint)_outH,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
        };
        _target = _device!.CreateTexture2D(desc);
        _rtv = _device.CreateRenderTargetView(_target);
        desc.Usage = ResourceUsage.Staging;
        desc.BindFlags = BindFlags.None;
        desc.CPUAccessFlags = CpuAccessFlags.Read;
        foreach (var slot in _readbacks) slot.Texture = _device.CreateTexture2D(desc);
    }

    void UploadPad(int[] pad, int width, int height)
    {
        if (_padTex == null || _padW < width || _padH < height)
        {
            _padView?.Dispose();
            _padTex?.Dispose();
            _padW = ((width + 63) / 64) * 64;
            _padH = ((height + 63) / 64) * 64;
            _padTex = _device!.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_padW,
                Height = (uint)_padH,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource
            });
            _padView = _device.CreateShaderResourceView(_padTex);
        }

        unsafe
        {
            fixed (int* src = pad)
            {
                _context!.UpdateSubresource(_padTex, 0, new Box(0, 0, 0, width, height, 1), (IntPtr)src, (uint)(width * 4), 0);
            }
        }
    }

    void UploadField(LiquidSurface? surface)
    {
        if (surface == null || surface.Columns < 1 || surface.Rows < 1)
        {
            return;
        }

        int cols = surface.Columns;
        int rows = surface.Rows;
        if (_fieldTex == null || _fieldW != cols || _fieldH != rows)
        {
            _fieldView?.Dispose();
            _fieldTex?.Dispose();
            _fieldW = cols;
            _fieldH = rows;
            _fieldTex = _device!.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)cols,
                Height = (uint)rows,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R32_Float,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource
            });
            _fieldView = _device.CreateShaderResourceView(_fieldTex);
        }

        if (_heights.Length < cols * rows) _heights = new float[cols * rows];
        surface.CopyHeights(_heights);
        unsafe
        {
            fixed (float* src = _heights)
            {
                _context!.UpdateSubresource(_fieldTex, 0, null, (IntPtr)src, (uint)(cols * 4), 0);
            }
        }
    }

    void TryReadback()
    {
        Readback? oldest = null;
        MappedSubresource mapped = default;
        long below = long.MaxValue;
        // 最多检查三个槽，取最新已完成帧；不阻塞、不自旋等 GPU。
        for (int attempt = 0; attempt < _readbacks.Length; attempt++)
        {
            Readback? candidate = null;
            foreach (var slot in _readbacks)
                if (slot.Pending && slot.Sequence < below && (candidate == null || slot.Sequence > candidate.Sequence)) candidate = slot;
            if (candidate?.Texture == null) break;
            below = candidate.Sequence;
            Result status = _context!.Map(candidate.Texture, 0, MapMode.Read, MapFlags.DoNotWait, out mapped);
            if (status.Code == unchecked((int)0x887A000A)) continue;
            status.CheckError(); oldest = candidate; break;
        }
        if (oldest?.Texture == null) return;
        foreach (var slot in _readbacks)
            if (slot.Pending && slot.Sequence < oldest.Sequence) { slot.Pending = false; Dropped++; }
        var frame = new GlassPixels(oldest.Width, oldest.Height, true, oldest.Started);
        try
        {
            unsafe
            {
                fixed (int* dest = frame.Pixels)
                    for (int y = 0; y < oldest.Height; y++)
                        Buffer.MemoryCopy((void*)(mapped.DataPointer + y * (int)mapped.RowPitch),
                            dest + y * oldest.Width, oldest.Width * 4, oldest.Width * 4);
            }
            _frame?.Dispose(); _frame = frame;
        }
        catch { frame.Dispose(); throw; }
        finally { _context!.Unmap(oldest.Texture, 0); oldest.Pending = false; }
    }

    static (int Width, int Height) OutputSize(int width, int height, bool moving)
    {
        int cap = GlassWork.GpuOutputLongSide;
        double scale = Math.Min(1, cap / (double)Math.Max(width, height));
        return (Math.Max(8, (int)Math.Round(width * scale)), Math.Max(8, (int)Math.Round(height * scale)));
    }

    void DisposeTarget()
    {
        _rtv?.Dispose();
        _target?.Dispose();
        foreach (var slot in _readbacks) { if (slot.Pending) Dropped++; slot.Texture?.Dispose(); slot.Texture = null; slot.Pending = false; }
        _rtv = null;
        _target = null;

        _outW = _outH = 0;
    }

    public void Dispose()
    {
        _disposed = true;
        _lost = true;
        _frame?.Dispose(); _frame = null;
        DisposeTarget();
        _padView?.Dispose();
        _padTex?.Dispose();
        _fieldView?.Dispose();
        _fieldTex?.Dispose();
        _constants?.Dispose();
        _sampler?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _padView = null;
        _padTex = null;
        _fieldView = null;
        _fieldTex = null;
        _constants = null;
        _sampler = null;
        _ps = null;
        _vs = null;
        _context = null;
        _device = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GpuConstants
    {
        public float IslandW, IslandH, Radius, Pad;
        public float SrcW, SrcH, SrcPadW, SrcPadH;
        public float FieldCols, FieldRows, HasPad, HasField;
        public float RimWidth, Chromatic, HeightWarp, SlopeWarp;
        public float Dpi, SrcRadius, ShiftX, ShiftY;
        public float ScaleX, ScaleY, Unused1, Unused2;
    }

    const string ShaderSource = """
cbuffer Params : register(b0)
{
    float IslandW;
    float IslandH;
    float Radius;
    float Pad;
    float SrcW;
    float SrcH;
    float SrcPadW;
    float SrcPadH;
    float FieldCols;
    float FieldRows;
    float HasPad;
    float HasField;
    float RimWidth;
    float Chromatic;
    float HeightWarp;
    float SlopeWarp;
    float Dpi;
    float SrcRadius;
    float ShiftX;
    float ShiftY;
    float ScaleX;
    float ScaleY;
    float Unused1;
    float Unused2;
};

Texture2D PadTex : register(t0);
Texture2D FieldTex : register(t1);
SamplerState LinearClamp : register(s0);

struct VSOut
{
    float4 pos : SV_Position;
    float2 uv : TEXCOORD0;
};

VSOut VSMain(uint id : SV_VertexID)
{
    VSOut o;
    o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

float Smootherstep(float e0, float e1, float x)
{
    if (e1 <= e0)
        return x >= e1 ? 1 : 0;
    float t = saturate((x - e0) / (e1 - e0));
    return t * t * t * (t * (t * 6 - 15) + 10);
}

float SafeRadius(float w, float h, float r)
{
    return clamp(r, 0, min(w, h) * 0.5);
}

float SignedDistance(float2 p, float w, float h, float r)
{
    w = max(1, w);
    h = max(1, h);
    float2 halfSize = float2(w, h) * 0.5;
    r = SafeRadius(w, h, r);
    float2 b = max(0, halfSize - r);
    float2 q = abs(p - halfSize) - b;
    return length(max(q, 0)) + min(max(q.x, q.y), 0) - r;
}

float RimBand(float w, float h) { return min(RimWidth * Dpi, min(w, h) * 0.20); }
float Along(float2 p, float w, float h, float r)
{
    return Smootherstep(0, 1, 1 + SignedDistance(p, w, h, r) / RimBand(w, h));
}
float2 NormalOf(float2 p, float w, float h, float r)
{
    float2 halfSize = float2(w, h) * 0.5;
    r = SafeRadius(w, h, r);
    float2 q = abs(p - halfSize) - (halfSize - r);
    float2 o = max(q, 0);
    float len = length(o);
    float2 direction = float2(p.x < halfSize.x ? -1 : 1, p.y < halfSize.y ? -1 : 1);
    if (len > 1e-9) return o / len * direction;
    return q.x > q.y ? float2(direction.x, 0) : float2(0, direction.y);
}
float FieldAt(float2 uv)
{
    float2 size = float2(FieldCols, FieldRows);
    return FieldTex.SampleLevel(LinearClamp, (saturate(uv) * (size - 1) + 0.5) / size, 0).r;
}

float SampleField(float2 uv, out float slopeX, out float slopeY)
{
    slopeX = 0;
    slopeY = 0;
    if (HasField < 0.5)
        return 0;
    float h = FieldAt(uv);
    float e = FieldCols <= 1 ? 0.08 : 1.0 / max(1, FieldCols - 1);
    float n = FieldRows <= 1 ? 0.08 : 1.0 / max(1, FieldRows - 1);
    slopeX = (FieldAt(uv + float2(e, 0)) - FieldAt(uv - float2(e, 0))) / (2 * e);
    slopeY = (FieldAt(uv + float2(0, n)) - FieldAt(uv - float2(0, n))) / (2 * n);
    return h;
}

float4 ShadeColor(float2 p, float surface)
{
    float d = SignedDistance(p, IslandW, IslandH, Radius);
    if (d >= 0.5)
        return 0;
    float along = Along(p, IslandW, IslandH, Radius);
    if (along <= 0)
        return 0;
    float2 n = NormalOf(p, IslandW, IslandH, Radius);
    float light = max(0, n.x * -0.28 + n.y * -0.96);
    float meniscus = along * 0.50;
    float specular = along * along * light;
    float caustic = along * clamp(surface, -1, 1);
    float alpha = saturate(meniscus * 0.42 + specular * 0.90 + max(0, caustic) * 0.36);
    alpha = min(alpha, 0.70);
    if (alpha <= 1.0 / 255.0)
        return 0;
    return float4(alpha, alpha, alpha, alpha);
}

float2 SafeRoundedPoint(float2 p, float2 size, float radius)
{
    float2 local = p - Pad;
    float d = SignedDistance(local, size.x, size.y, radius);
    float guard = 3 * Dpi;
    if (d < guard) p += NormalOf(local, size.x, size.y, radius) * (guard - d);
    return clamp(p, 1, size + 2 * Pad - 2);
}
// 捕获已将无效像素清零、有效 RGB 标 alpha=1；硬件双线性同时滤 RGB 和有效性。
float4 PadTap(float2 p)
{
    float2 size = float2(SrcPadW, SrcPadH);
    p = clamp(p, 0, size - 1);
    return PadTex.SampleLevel(LinearClamp, (p + 0.5) / size, 0);
}

float4 SamplePad(float2 uv)
{
    float2 p = Pad + (uv - Pad) * float2(ScaleX, ScaleY) + float2(ShiftX, ShiftY);
    if (any(p < 0) || any(p > float2(SrcW, SrcH) + 2 * Pad - 1)) return 0;
    const float tap = 0.65;
    float4 c = PadTap(p) + PadTap(p + float2(tap, 0)) + PadTap(p - float2(tap, 0))
        + PadTap(p + float2(0, tap)) + PadTap(p - float2(0, tap));
    return c.a > 1e-6 ? float4(c.rgb / c.a, saturate(c.a / 5)) : 0;
}

bool TryMap(float2 p, float2 slope, float surface, out float2 u, out float2 ru, out float2 bu, out float alpha)
{
    u = ru = bu = 0;
    alpha = 0;
    if (IslandW < 2 || IslandH < 2 || Pad < 4)
        return false;
    float r = SafeRadius(IslandW, IslandH, Radius);
    float d = SignedDistance(p, IslandW, IslandH, r);
    if (d >= 0.5)
        return false;
    float along = Along(p, IslandW, IslandH, r);
    if (along <= 0)
        return false;
    float2 nrm = NormalOf(p, IslandW, IslandH, r);
    float depth = (4 + 3.5 * along + clamp(surface, -1, 1) * HeightWarp) * Dpi;
    float2 warped = Pad + p + nrm * (depth - d) + slope / (1 + abs(slope)) * SlopeWarp * Dpi;
    u = SafeRoundedPoint(warped, float2(IslandW, IslandH), r);
    ru = bu = u; // RGB shares the exact same spatial footprint: no artificial dispersion.
    alpha = Smootherstep(0, 0.88, along) * (1 - 0.20 * Smootherstep(0.84, 1, along));
    return alpha > 0.01;
}

float4 PSMain(VSOut i) : SV_Target
{
    float2 p = i.uv * float2(IslandW, IslandH);
    float2 uv = i.uv;
    float distance = SignedDistance(p, IslandW, IslandH, Radius);
    if (distance >= 0.5 || distance <= -RimBand(IslandW, IslandH)) return 0;
    float slopeX, slopeY;
    float surface = SampleField(uv, slopeX, slopeY);
    float4 shade = ShadeColor(p, surface);
    float4 color = shade;
    if (HasPad > 0.5)
    {
        float2 u, ru, bu;
        float alpha;
        if (TryMap(p, float2(slopeX, slopeY), surface, u, ru, bu, alpha))
        {
            float4 green = SamplePad(u);
            if (dot(green, 1) > 0)
            {
                float a = saturate(alpha * green.a);
                float4 refr = float4(green.rgb, 1) * a;
                color = refr + color * (1 - a);
            }
        }
    }
    return color;
}
""";
}
