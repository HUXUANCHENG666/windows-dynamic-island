using DynamicIsland.Core.Geometry;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Translation;

namespace DynamicIsland.Core.Tests;

public class SettingsPageTests
{
    [Theory]
    [InlineData(1366, 768, 1)]
    [InlineData(1366, 768, 1.25)]
    [InlineData(1366, 768, 1.5)]
    [InlineData(1024, 768, 1.5)]
    [InlineData(1280, 720, 1.5)]
    public void WindowFitsWorkAreaInLogicalDip(double width, double height, double dpi)
    {
        var size = SettingsPagePolicy.Fit(width / dpi, (height - 48) / dpi);
        Assert.InRange(size.Width, 1, width / dpi - 24);
        Assert.InRange(size.Height, 1, (height - 48) / dpi - 24);
    }

    [Theory]
    [InlineData("https://example.invalid", "model", "key", true)]
    [InlineData("http://127.0.0.1:8080", "model", "key", true)]
    [InlineData("http://remote.invalid", "model", "key", false)]
    [InlineData("https://user:password@example.invalid", "model", "key", false)]
    [InlineData("", "model", "key", false)]
    [InlineData("https://example.invalid", "", "key", false)]
    [InlineData("https://example.invalid", "model", " ", false)]
    public void ApiValidationIsLocalAndNeverEchoesInputs(string url, string model, string key, bool valid)
    {
        var error = SettingsPagePolicy.ValidateApi(TranslateApiKind.Custom, url, model, key);
        Assert.Equal(valid, error.Length == 0);
        Assert.DoesNotContain("password", error);
        Assert.DoesNotContain("example.invalid", error);
        Assert.Equal("", SettingsPagePolicy.ValidateApi(TranslateApiKind.Default, url, model, key));
    }

    [Theory]
    [InlineData(0.85, 1)]
    [InlineData(1, 1.25)]
    [InlineData(1.4, 1.5)]
    [InlineData(1.4, 2)]
    public void NineDipRimHasSmallIslandCapAndNoExpandedGrowth(double scale, double dpi)
    {
        Assert.Equal(9, IslandOutline.RimWidthDip);
        foreach (var (w, h) in new[] { (180.0, 40.0), (40.0, 180.0), (534.0, 230.0), (12.0, 12.0) })
        {
            var width = w * scale * dpi;
            var height = h * scale * dpi;
            Assert.Equal(Math.Min(9 * dpi, Math.Min(width, height) * .20), IslandOutline.RimBand(width, height, dpi), 9);
        }
    }
}
