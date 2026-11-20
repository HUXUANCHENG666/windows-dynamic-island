using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Motion;
using Xunit;

namespace DynamicIsland.Core.Tests;
public class WaterLensTests
{
    const int W=320,H=140,P=LiquidRefraction.DefaultPad;
    static (double U,double V,double A) Map(double x,double y,double dpi=1)
    {
        double w=W*dpi,h=H*dpi,r=35*dpi;
        WaterLens.Map(x,y,w,h,r,dpi,IslandOutline.SignedDistance(x,y,w,h,r),LiquidOpticsFrame.Identity,0,0,0,out var u,out var v,out var a);
        return(u-P,v-P,a);
    }
    [Theory]
    [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void SettledLensMagnifiesModeratelyAndIsDpiInvariant(double dpi)
    {
        var c=Map(160*dpi,70*dpi,dpi);
        var n=Map(161*dpi,70*dpi,dpi);
        double magnification=dpi/(n.U-c.U);
        Assert.InRange(magnification,1.07,1.09);
        Assert.Equal(160*dpi,c.U,6); Assert.Equal(70*dpi,c.V,6);
        Assert.InRange(c.A,0.95,0.97);
    }
    [Fact]
    public void CrossSectionIsContinuousAndMonotonicThroughInnerRim()
    {
        double previous=Map(160,70).U;
        for(double x=160.1;x<320;x+=0.1)
        {
            double u=Map(x,70).U;
            Assert.InRange(u-previous,-0.000001,0.6);
            previous=u;
        }
    }
    [Fact]
    public void CurrentSourceChangesImmediatelyAndNoLiveStaysClear()
    {
        int[] source=new int[(W+2*P)*(H+2*P)],a=new int[W*H],b=new int[W*H];
        Array.Fill(source,unchecked((int)0xFF101010));
        GlassCpuRenderer.Render(a,W,H,W,H,35,1,null,source,W,H,35,liveInterior:true);
        Array.Fill(source,unchecked((int)0xFFE0E0E0));
        GlassCpuRenderer.Render(b,W,H,W,H,35,1,null,source,W,H,35,liveInterior:true);
        Assert.True((b[70*W+160]&255)>(a[70*W+160]&255)+100);
        GlassCpuRenderer.Render(b,W,H,W,H,35,1,null,source,W,H,35,liveInterior:false);
        Assert.Equal(0,b[70*W+160]);
    }
    [Fact]
    public void AsyncResultsRejectOldSizeOldPositionAndStaleAge()
    {
        var g=new GlassFrameGeometry(320,140,100,100,1,1);
        Assert.True(g.Matches(g with {Left=102}));
        Assert.False(g.Matches(g with {Width=400}));
        Assert.False(g.Matches(g with {Top=110}));
        Assert.False(g.Matches(g with {ScaleX=1.1}));
        Assert.True(GlassFrameGeometry.IsFresh(16.67));
        Assert.False(GlassFrameGeometry.IsFresh(101));
        Assert.False(GlassFrameGeometry.IsFresh(-1));
    }
}
