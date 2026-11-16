using System.Buffers.Binary;
using DynamicIsland.Core.Island;
using DynamicIsland.Core.LiveTranslate;
using DynamicIsland.Core.Settings;
using DynamicIsland.Core.Translation;

namespace DynamicIsland.Core.Tests;

public class LiveTranslateTests
{
    static DateTime T(int ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

    [Fact]
    public void Policy_IsOffByDefault_AndDoesNotPersist()
    {
        Assert.False(LiveTranslatePolicy.EnabledByDefault);
        Assert.False(LiveTranslatePolicy.PersistsAcrossLaunches);
        var loaded = SettingsJson.Deserialize("{}");
        Assert.False(new AppSettings().Stealth);
        Assert.Equal("auto", loaded.TranslateDirection);
    }

    [Fact]
    public void Plan_SkipsEmptyAndNonSpeechMarks()
    {
        Assert.Equal(LiveTranslateDecision.Empty, LiveTranslatePolicy.Plan(null, "en").Decision);
        Assert.Equal(LiveTranslateDecision.Empty, LiveTranslatePolicy.Plan("   ", "en").Decision);
        Assert.Equal(LiveTranslateDecision.Empty, LiveTranslatePolicy.Plan("[BLANK_AUDIO]", "en").Decision);
        Assert.Equal(LiveTranslateDecision.Empty, LiveTranslatePolicy.Plan("(music)", "en").Decision);
        Assert.Equal(LiveTranslateDecision.Empty, LiveTranslatePolicy.Plan("12345", "en").Decision);
    }

    [Fact]
    public void Plan_ShowsChineseAsIs_WithoutATranslateRequest()
    {
        var byGlyphs = LiveTranslatePolicy.Plan("今天天气很好。", "en");
        Assert.Equal(LiveTranslateDecision.AlreadyChinese, byGlyphs.Decision);
        Assert.Equal("今天天气很好。", byGlyphs.DisplayText);
        Assert.Null(byGlyphs.Request);

        var byLanguage = LiveTranslatePolicy.Plan("你好世界", "zh");
        Assert.Equal(LiveTranslateDecision.AlreadyChinese, byLanguage.Decision);
        Assert.Null(byLanguage.Request);

        var cantonese = LiveTranslatePolicy.Plan("食咗未", "yue");
        Assert.Equal(LiveTranslateDecision.AlreadyChinese, cantonese.Decision);

        Assert.True(LiveTranslatePolicy.IsChineseLanguage("zh-CN"));
        Assert.True(LiveTranslatePolicy.IsChineseLanguage("ZH"));
        Assert.False(LiveTranslatePolicy.IsChineseLanguage("en"));
        Assert.False(LiveTranslatePolicy.IsAlreadyChinese("Good morning", "en"));
    }

    [Fact]
    public void Plan_ShapesARequestToSimplifiedChinese()
    {
        var plan = LiveTranslatePolicy.Plan("  Hello   world  ", "en");
        Assert.Equal(LiveTranslateDecision.Translate, plan.Decision);
        Assert.Equal("Hello world", plan.DisplayText);
        Assert.NotNull(plan.Request);
        Assert.Equal("Hello world", plan.Request!.Value.Text);
        Assert.Equal("en", plan.Request.Value.Source);
        Assert.Equal("zh-CN", plan.Request.Value.Target);
        Assert.False(plan.Request.Value.Clipped);

        var japanese = LiveTranslatePolicy.Plan("こんにちは", "ja");
        Assert.Equal(LiveTranslateDecision.Translate, japanese.Decision);
        Assert.Equal("ja", japanese.Request!.Value.Source);
        Assert.Equal("zh-CN", japanese.Request.Value.Target);

        var unknown = LiveTranslatePolicy.Plan("Bonjour", null);
        Assert.Equal("autodetect", unknown.Request!.Value.Source);
        Assert.Equal("zh-CN", unknown.Request.Value.Target);

        var longText = "Alpha " + new string('b', TranslatePolicy.MaxSendChars);
        var clipped = LiveTranslatePolicy.Plan(longText, "en");
        Assert.True(clipped.Request!.Value.Clipped);
        Assert.Equal(TranslatePolicy.MaxSendChars, clipped.Request.Value.Text.Length);
        Assert.Equal("zh-CN", clipped.Request.Value.Target);
        Assert.EndsWith("…", LiveTranslatePolicy.ForIsland(longText));
        Assert.True(LiveTranslatePolicy.ForIsland(longText).Length <= LiveTranslatePolicy.MaxDisplayChars + 1);
    }

    [Fact]
    public void Copy_ProgressLabel_DoesNotIncludeTranscripts()
    {
        Assert.Equal("正在下载识别模型…", LiveTranslateCopy.Downloading(0));
        Assert.Equal("正在下载识别模型 42%", LiveTranslateCopy.Downloading(0.42));
        Assert.Equal("正在下载识别模型 100%", LiveTranslateCopy.Downloading(1));
        Assert.Equal(0.25, LiveTranslateCopy.Progress(25, 100), 5);
        Assert.Equal(0, LiveTranslateCopy.Progress(10, null));
        Assert.DoesNotContain("Hello", LiveTranslateCopy.Listening);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.Downloading(0.5));
    }

    [Fact]
    public void Resampler_MixesStereoAndDownsamplesTo16k()
    {
        const int sourceRate = 48000;
        const int seconds = 1;
        var raw = new byte[sourceRate * seconds * 2 * 4];
        // Left = 0.5, right = -0.5, mono mix should be ~0.
        for (int i = 0; i < sourceRate * seconds; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(i * 8, 4), 0.5f);
            BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(i * 8 + 4, 4), -0.5f);
        }

        var resampler = new StreamResampler(sourceRate, 2, WaveKind.Float32);
        var output = new List<float>();
        resampler.Push(raw, output);
        Assert.InRange(output.Count, StreamResampler.TargetRate - 2, StreamResampler.TargetRate + 2);
        Assert.All(output, sample => Assert.InRange(sample, -0.0001f, 0.0001f));
    }

    [Fact]
    public void Resampler_KeepsPhaseAcrossChunks_AndReadsPcm16()
    {
        const int sourceRate = 16000;
        var first = new byte[2];
        var second = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(first, 16384);
        BinaryPrimitives.WriteInt16LittleEndian(second, 16384);
        var resampler = new StreamResampler(sourceRate, 1, WaveKind.Pcm16);
        var output = new List<float>();
        resampler.Push(first, output);
        Assert.Empty(output);
        resampler.Push(second, output);
        Assert.NotEmpty(output);
        Assert.InRange(output[0], 0.49f, 0.51f);
    }

    [Fact]
    public void Segmenter_IgnoresSilenceAndEmitsAfterHangover()
    {
        var segmenter = new SpeechSegmenter();
        var silence = new float[SpeechSegmenter.FrameSamples * 20];
        Assert.Empty(segmenter.Push(silence));

        var speech = Voice(SpeechSegmenter.MinSpeechFrames + 5);
        Assert.Empty(segmenter.Push(speech));

        var pause = new float[SpeechSegmenter.FrameSamples * SpeechSegmenter.HangoverFrames];
        var segments = segmenter.Push(pause);
        var segment = Assert.Single(segments);
        Assert.True(segment.Length >= SpeechSegmenter.FrameSamples * SpeechSegmenter.MinSpeechFrames);
    }

    [Fact]
    public void Segmenter_DropsShortBlips_AndCutsLongUtterances()
    {
        var shortLived = new SpeechSegmenter();
        var blip = Voice(4);
        Assert.Empty(shortLived.Push(blip));
        Assert.Empty(shortLived.Push(new float[SpeechSegmenter.FrameSamples * SpeechSegmenter.HangoverFrames]));
        Assert.Null(shortLived.Flush());

        var longLived = new SpeechSegmenter();
        var segments = longLived.Push(Voice(SpeechSegmenter.MaxSpeechFrames + 8));
        Assert.NotEmpty(segments);
        Assert.True(segments[0].Length >= SpeechSegmenter.FrameSamples * SpeechSegmenter.MaxSpeechFrames);
    }

    [Fact]
    public void Director_LiveTranslate_StaysOpenOnLeaveNoticeAndFullscreen()
    {
        var director = new IslandDirector();
        director.SetLiveTranslate(true);
        Assert.True(director.LiveTranslateOpen);
        Assert.True(director.LiveTranslateSession);
        Assert.True(director.Expanded);
        Assert.Equal(IslandMode.LiveTranslate, director.Mode);

        director.PointerExited(T(20));
        director.Tick(T(2000));
        Assert.True(director.Expanded);
        Assert.Equal(IslandMode.LiveTranslate, director.Mode);

        director.PushNotice(IslandMode.Volume, T(2100));
        Assert.Equal(IslandMode.Volume, director.Mode);
        Assert.True(director.LiveTranslateOpen);
        Assert.True(director.LiveTranslateSession);
        director.Tick(T(2100 + 1700));
        Assert.Equal(IslandMode.LiveTranslate, director.Mode);
        Assert.True(director.LiveTranslateSession);

        director.SetOccluded(true, T(5000));
        Assert.True(director.Expanded);
        Assert.False(director.ShowAsDot);
        Assert.Equal(IslandMode.LiveTranslate, director.Mode);

        director.SetAmbient(AmbientKind.Media);
        Assert.Equal(IslandMode.LiveTranslate, director.Mode);

        director.TrayClick(T(5100));
        Assert.True(director.Expanded);
        Assert.Equal(IslandMode.LiveTranslate, director.Mode);

        director.ShowTranslationResult("一条旧译文", T(5200));
        Assert.Equal(IslandMode.LiveTranslate, director.Mode);
        Assert.False(director.RevealTranslation(T(5200)));

        director.SetLiveTranslate(false);
        Assert.False(director.LiveTranslateOpen);
        Assert.False(director.LiveTranslateSession);
        Assert.False(director.Expanded);
        Assert.True(director.ShowAsDot);
    }

    [Fact]
    public void Director_LiveTranslate_AndCameraReplaceEachOther()
    {
        var director = new IslandDirector();
        director.SetLiveTranslate(true);
        director.SetCamera(true);
        Assert.False(director.LiveTranslateOpen);
        Assert.True(director.CameraOpen);
        Assert.Equal(IslandMode.Camera, director.Mode);

        director.SetLiveTranslate(true);
        Assert.False(director.CameraOpen);
        Assert.True(director.LiveTranslateOpen);
        Assert.Equal(IslandMode.LiveTranslate, director.Mode);

        director.SetLiveTranslate(false);
        Assert.False(director.LiveTranslateOpen);
        Assert.Equal(IslandMode.Idle, director.Mode);
    }

    [Fact]
    public void Metrics_IncludeALiveTranslateFrame()
    {
        var horizontal = IslandMetrics.FrameFor(IslandMode.LiveTranslate, true, IslandOrientation.Horizontal);
        var vertical = IslandMetrics.FrameFor(IslandMode.LiveTranslate, true, IslandOrientation.Vertical);
        Assert.Equal(IslandMetrics.LiveTranslateW, horizontal.Width);
        Assert.Equal(IslandMetrics.LiveTranslateH, horizontal.Height);
        Assert.Equal(IslandMetrics.VLiveTranslateW, vertical.Width);
        Assert.Equal(IslandMetrics.VLiveTranslateH, vertical.Height);
        Assert.True(horizontal.Radius > 0);
        Assert.True(vertical.Radius > 0);
    }

    static float[] Voice(int frames)
    {
        var samples = new float[frames * SpeechSegmenter.FrameSamples];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = i % 2 == 0 ? 0.2f : -0.2f;
        return samples;
    }
}
