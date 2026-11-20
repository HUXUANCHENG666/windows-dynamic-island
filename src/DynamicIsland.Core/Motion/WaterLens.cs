using DynamicIsland.Core.Geometry;

namespace DynamicIsland.Core.Motion;

/// <summary>只用于有效实况底图的清水透镜；中心约 1.08x，圆角边缘平滑接回原折射。无时钟抖动、无色散。</summary>
public static class WaterLens
{
    public const double Magnification = 1.08;
    public static void Map(double x, double y, double w, double h, double radius, double dpi,
        double distance, LiquidOpticsFrame optics, double slopeX, double slopeY, double surface,
        out double u, out double v, out double alpha)
    {
        const int pad = LiquidRefraction.DefaultPad;
        double cx=w*0.5, cy=h*0.5;
        double nx=(x-cx)/cx, ny=(y-cy)/cy;
        double dome=Math.Max(0, (1-nx*nx)*(1-ny*ny));
        double contraction=(1-1/Magnification)*dome*dome;
        LiquidOptics.DisplacedUv(x,y,w,h,dpi,optics,slopeX,slopeY,out double dx,out double dy);
        double band=IslandOutline.RimBand(w,h,dpi);
        double blend=IslandOutline.Smootherstep(-3*band,-0.25*band,distance);
        double bx=x-(x-cx)*contraction+(dx-x)*0.18*(1-blend);
        double by=y-(y-cy)*contraction+(dy-y)*0.18*(1-blend);
        // 原边缘映射在 rim 内有效，过渡区将同一公式平滑延伸到内侧，避免两层图缝。
        if (blend <= 0) { u=pad+bx; v=pad+by; alpha=0.96; return; }
        var normal=IslandOutline.Normal(x,y,w,h,radius);
        double along=IslandOutline.Smootherstep(0,1,1+distance/band);
        double depth=(4+3.5*along+Math.Clamp(surface,-1,1)*LiquidRefraction.HeightWarp)*dpi;
        double ex=x+normal.Nx*(depth-distance)+slopeX/(1+Math.Abs(slopeX))*LiquidRefraction.SlopeWarp*dpi;
        double ey=y+normal.Ny*(depth-distance)+slopeY/(1+Math.Abs(slopeY))*LiquidRefraction.SlopeWarp*dpi;
        u=pad+bx+(ex-bx)*blend;
        v=pad+by+(ey-by)*blend;
        alpha=(0.96-0.16*blend)*Math.Clamp(0.5-distance,0,1);
    }
}
