using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
/// 用 D3D11 在显卡上做边缘折射、色差和高光。初始化失败或设备丢失时由调用方退回 CPU。
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
    ID3D11Texture2D? _staging;
    WriteableBitmap? _bitmap;
    int _padW, _padH, _fieldW, _fieldH, _outW, _outH;
    bool _lost;
    bool _disposed;

    public bool IsAvailable => !_disposed && !_lost && _device != null && _context != null && _ps != null;
    public ImageSource? Image => _bitmap;

    public static GlassGpuRenderer? TryCreate()
    {
        try
        {
            var gpu = new GlassGpuRenderer();
            if (gpu.Initialize())
                return gpu;
            gpu.Dispose();
        }
        catch
        {
            // 显卡或编译器不可用时走 CPU，不能让初始化把窗口打掉。
        }

        return null;
    }

    bool Initialize()
    {
        if (!TryCreateDevice(out _device, out _context) || _device == null || _context == null)
            return false;

        if (!TryCompile("VSMain", "vs_5_0", out var vsBlob) && !TryCompile("VSMain", "vs_4_0", out vsBlob))
            return false;
        if (!TryCompile("PSMain", "ps_5_0", out var psBlob) && !TryCompile("PSMain", "ps_4_0", out psBlob))
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

    static bool TryCreateDevice(out ID3D11Device? device, out ID3D11DeviceContext? context)
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
            DriverType.Hardware,
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
            ShaderSource,
            entry,
            "GlassGpu.hlsl",
            profile,
            out blob,
            out Blob? errors);
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
        bool moving)
    {
        if (!IsAvailable || islandW < 8 || islandH < 8)
            return false;

        try
        {
            var (outW, outH) = OutputSize(islandW, islandH, moving);
            EnsureTarget(outW, outH);
            bool hasPad = pad != null && padW >= 2 && padH >= 2 && pad!.Length >= padW * padH;
            if (hasPad)
                UploadPad(pad!, padW, padH);
            UploadField(surface);

            if (_target == null || _rtv == null || _staging == null || _constants == null || _context == null)
                return false;

            var data = new GpuConstants
            {
                IslandW = islandW,
                IslandH = islandH,
                Radius = (float)(radius < 0 ? Math.Min(islandW, islandH) * 0.5 : radius),
                Pad = LiquidRefraction.DefaultPad,
                SrcW = hasPad ? sourceIslandW : islandW,
                SrcH = hasPad ? sourceIslandH : islandH,
                SrcPadW = hasPad ? padW : 1,
                SrcPadH = hasPad ? padH : 1,
                FieldCols = surface?.Columns ?? 0,
                FieldRows = surface?.Rows ?? 0,
                HasPad = hasPad ? 1 : 0,
                HasField = surface != null && surface.Columns > 0 ? 1 : 0,
                SampleGain = (float)LiquidRefraction.SampleGain,
                Chromatic = (float)LiquidRefraction.Chromatic,
                HeightWarp = (float)LiquidRefraction.HeightWarp,
                SlopeWarp = (float)LiquidRefraction.SlopeWarp,
                RimFraction = (float)Core.Geometry.IslandOutline.RimFraction,
                MinInnerFrac = (float)Core.Geometry.IslandOutline.MinInnerCornerFraction,
                MinInnerCorner = (float)Core.Geometry.IslandOutline.MinInnerCorner
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
            _context.CopyResource(_staging, _target);
            CopyStagingToBitmap(outW, outH);
            return _bitmap != null;
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
        if (_target != null && _outW == width && _outH == height)
            return;
        DisposeTarget();
        _outW = width;
        _outH = height;
        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
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
        _staging = _device.CreateTexture2D(desc);
    }

    void UploadPad(int[] pad, int width, int height)
    {
        if (_padTex == null || _padW != width || _padH != height)
        {
            _padView?.Dispose();
            _padTex?.Dispose();
            _padW = width;
            _padH = height;
            _padTex = _device!.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width,
                Height = (uint)height,
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
                _context!.UpdateSubresource(_padTex, 0, null, (IntPtr)src, (uint)(width * 4), 0);
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

        var heights = new float[cols * rows];
        surface.CopyHeights(heights);
        unsafe
        {
            fixed (float* src = heights)
            {
                _context!.UpdateSubresource(_fieldTex, 0, null, (IntPtr)src, (uint)(cols * 4), 0);
            }
        }
    }

    void CopyStagingToBitmap(int width, int height)
    {
        MappedSubresource mapped = _context!.Map(_staging!, 0, MapMode.Read, MapFlags.None);
        try
        {
            if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
                _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
            _bitmap.Lock();
            try
            {
                nint dest = _bitmap.BackBuffer;
                int destPitch = _bitmap.BackBufferStride;
                int srcPitch = (int)mapped.RowPitch;
                int rowBytes = width * 4;
                for (int y = 0; y < height; y++)
                {
                    unsafe
                    {
                        Buffer.MemoryCopy(
                            (void*)(mapped.DataPointer + y * srcPitch),
                            (void*)(dest + y * destPitch),
                            destPitch,
                            rowBytes);
                    }
                }
                _bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                _bitmap.Unlock();
            }
        }
        finally
        {
            _context.Unmap(_staging, 0);
        }
    }

    static (int Width, int Height) OutputSize(int width, int height, bool moving)
    {
        int cap = moving ? Math.Max(GlassWork.MovingOutputLongSide, 240) : GlassWork.GpuOutputLongSide;
        double scale = Math.Min(1, cap / (double)Math.Max(width, height));
        return (Math.Max(8, (int)Math.Round(width * scale)), Math.Max(8, (int)Math.Round(height * scale)));
    }

    void DisposeTarget()
    {
        _rtv?.Dispose();
        _target?.Dispose();
        _staging?.Dispose();
        _rtv = null;
        _target = null;
        _staging = null;
        _outW = _outH = 0;
    }

    public void Dispose()
    {
        _disposed = true;
        _lost = true;
        _bitmap = null;
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
        public float SampleGain, Chromatic, HeightWarp, SlopeWarp;
        public float RimFraction, MinInnerFrac, MinInnerCorner, Unused;
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
    float SampleGain;
    float Chromatic;
    float HeightWarp;
    float SlopeWarp;
    float RimFraction;
    float MinInnerFrac;
    float MinInnerCorner;
    float Unused;
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

float RimBand(float w, float h)
{
    return max(8, min(max(1, w), max(1, h)) * RimFraction);
}

float InnerCornerRadius(float w, float h, float r)
{
    r = SafeRadius(w, h, r);
    float inset = RimBand(w, h);
    float innerShort = max(1, min(w, h) - 2 * inset);
    float maxR = innerShort * 0.5;
    float minR = min(maxR, max(MinInnerCorner, r * MinInnerFrac));
    return min(max(minR, r - inset), maxR);
}

float InnerSignedDistance(float2 p, float w, float h, float r)
{
    float inset = RimBand(w, h);
    float innerW = max(1, w - 2 * inset);
    float innerH = max(1, h - 2 * inset);
    float innerR = InnerCornerRadius(w, h, r);
    return SignedDistance(p - inset, innerW, innerH, innerR);
}

float Along(float2 p, float w, float h, float r)
{
    float dOuter = SignedDistance(p, w, h, r);
    if (dOuter >= 0)
        return 1;
    float dInner = InnerSignedDistance(p, w, h, r);
    if (dInner <= 0)
        return 0;
    float span = dInner - dOuter;
    if (span <= 1e-6)
        return 1;
    return Smootherstep(0, 1, dInner / span);
}

float2 NormalOf(float2 p, float w, float h, float r)
{
    const float e = 0.75;
    float dx = SignedDistance(p + float2(e, 0), w, h, r) - SignedDistance(p - float2(e, 0), w, h, r);
    float dy = SignedDistance(p + float2(0, e), w, h, r) - SignedDistance(p - float2(0, e), w, h, r);
    float len = length(float2(dx, dy));
    if (len < 1e-8)
        return float2(0, -1);
    return float2(dx, dy) / len;
}

float SampleField(float2 uv, out float slopeX, out float slopeY)
{
    slopeX = 0;
    slopeY = 0;
    if (HasField < 0.5)
        return 0;
    float h = FieldTex.SampleLevel(LinearClamp, uv, 0).r;
    float e = FieldCols <= 1 ? 0.08 : 1.0 / max(1, FieldCols - 1);
    float n = FieldRows <= 1 ? 0.08 : 1.0 / max(1, FieldRows - 1);
    slopeX = (FieldTex.SampleLevel(LinearClamp, uv + float2(e, 0), 0).r - FieldTex.SampleLevel(LinearClamp, uv - float2(e, 0), 0).r) / (2 * e);
    slopeY = (FieldTex.SampleLevel(LinearClamp, uv + float2(0, n), 0).r - FieldTex.SampleLevel(LinearClamp, uv - float2(0, n), 0).r) / (2 * n);
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
    return float4(float3(0.956, 0.984, 1) * alpha, alpha);
}

float MapAxis(float x, float source, float target)
{
    if (x < Pad)
        return x;
    if (x >= Pad + target)
        return x - target + source;
    if (target <= 0)
        return Pad;
    return Pad + (x - Pad) * source / target;
}

float2 MapToSource(float2 uv)
{
    return float2(MapAxis(uv.x, SrcW, IslandW), MapAxis(uv.y, SrcH, IslandH));
}

float4 SamplePad(float2 uv)
{
    float2 tex = (MapToSource(uv) + 0.5) / max(float2(SrcPadW, SrcPadH), 1);
    float4 c = PadTex.SampleLevel(LinearClamp, tex, 0);
    if (dot(c.rgb, 1) <= 0 && c.a <= 0)
        return 0;
    if (c.a <= 0)
        c.a = 1;
    return c;
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
    float2 halfSize = float2(IslandW, IslandH) * 0.5;
    float tx = abs(nrm.x) < 1e-8 ? 1e9 : halfSize.x / abs(nrm.x);
    float ty = abs(nrm.y) < 1e-8 ? 1e9 : halfSize.y / abs(nrm.y);
    float tRect = min(tx, ty);
    if (tRect <= 0)
        return false;
    float offset = 0.6 + along * max(4, (Pad - 1) * SampleGain) + surface * Pad * HeightWarp;
    float2 sample = halfSize + Pad + nrm * (tRect + offset) + slope * Pad * SlopeWarp;
    sample = clamp(sample, 0, float2(IslandW, IslandH) + Pad * 2 - 1);
    float chroma = Chromatic * Pad * along * along * (1 - 0.55 * Smootherstep(0.72, 1, along));
    u = sample;
    ru = clamp(sample + nrm * chroma, 0, float2(IslandW, IslandH) + Pad * 2 - 1);
    bu = clamp(sample - nrm * chroma, 0, float2(IslandW, IslandH) + Pad * 2 - 1);
    alpha = Smootherstep(0, 0.88, along) * (1 - 0.20 * Smootherstep(0.84, 1, along));
    return alpha > 0.01;
}

float4 PSMain(VSOut i) : SV_Target
{
    float2 p = i.uv * float2(IslandW, IslandH);
    float2 uv = i.uv;
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
                float4 red = SamplePad(ru);
                float4 blue = SamplePad(bu);
                if (dot(red, 1) <= 0) red = green;
                if (dot(blue, 1) <= 0) blue = green;
                float a = saturate(alpha);
                float4 refr = float4(red.r, green.g, blue.b, 1) * a;
                color = refr + color * (1 - a);
            }
        }
    }
    return color;
}
""";
}
