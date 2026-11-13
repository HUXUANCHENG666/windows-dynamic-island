using DynamicIsland.Core.Island;

namespace DynamicIsland.Core.Tests;

public class HideListEditorTests
{
    [Fact]
    public void Add_NormalizesTheExeName_AndThenThatAppHidesOnTheIslandMonitor()
    {
        var list = new List<string>();
        Assert.Equal(HideListOutcome.Added, HideListEditor.Add(list, @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        Assert.Equal(new[] { "chrome" }, list);
        Assert.True(HidePolicy.IsListed(list, "CHROME.EXE"));
        Assert.True(HidePolicy.IsOccluded(new HideSignals(false, true, true, false, "chrome.exe"), list));
        Assert.False(HidePolicy.IsOccluded(new HideSignals(false, true, false, false, "chrome"), list));
    }

    [Fact]
    public void Add_SecondTime_DoesNotRemoveTheName()
    {
        var list = new List<string>();
        Assert.Equal(HideListOutcome.Added, HideListEditor.Add(list, "WeChat"));
        Assert.Equal(HideListOutcome.AlreadyListed, HideListEditor.Add(list, "wechat.exe"));
        Assert.Equal(new[] { "wechat" }, list);
    }

    [Fact]
    public void Remove_DropsTheName_AndAMissingNameStaysMissing()
    {
        var list = new List<string> { "notepad" };
        Assert.Equal(HideListOutcome.Removed, HideListEditor.Remove(list, "NOTEPAD.EXE"));
        Assert.Empty(list);
        Assert.Equal(HideListOutcome.NotListed, HideListEditor.Remove(list, "notepad"));
        Assert.False(HidePolicy.IsOccluded(new HideSignals(false, true, true, false, "notepad"), list));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dynamicisland")]
    [InlineData("DynamicIsland.exe")]
    [InlineData("Microsoft Edge")]
    [InlineData("..")]
    [InlineData("chrome|bad")]
    [InlineData("applicationframehost")]
    public void Add_RejectsNamesThatWouldNeverMatchARealApp(string raw)
    {
        var list = new List<string>();
        Assert.Equal(HideListOutcome.Rejected, HideListEditor.Add(list, raw));
        Assert.Empty(list);
    }

    [Fact]
    public void Candidates_AreSorted_SkipListedAndOurOwnProcess_AndStopAtTheCap()
    {
        var shown = HideListEditor.Candidates(
            new string?[] { "notepad", "chrome", "chrome.exe", "DynamicIsland", "WeChat", null, "dwm", "explorer" },
            new[] { "notepad" });

        Assert.Equal(new[] { "chrome", "explorer", "wechat" }, shown);

        var many = Enumerable.Range(0, 20).Select(i => (string?)$"app{i:00}").ToArray();
        var capped = HideListEditor.Candidates(many, Array.Empty<string>());
        Assert.Equal(HideListEditor.MaxCandidates, capped.Count);
        Assert.Equal(capped.OrderBy(item => item, StringComparer.Ordinal), capped);
    }
}
