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
        Assert.Equal("default", loaded.TranslateApi);
        Assert.Null(loaded.TranslateApiUrl);
        Assert.Null(loaded.TranslateApiModel);
        Assert.Null(loaded.TranslateApiKey);
        Assert.Equal("default", new AppSettings().TranslateApi);
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
        Assert.Equal("正在下载识别模型 12.0 MB", LiveTranslateCopy.Downloading(0, 12 * 1024 * 1024, null));
        Assert.Equal("12.0 MB", LiveTranslateCopy.FormatMegabytes(12 * 1024 * 1024));
        Assert.Equal("正在连接下载源…", LiveTranslateCopy.Connecting);
        Assert.Equal("正在换一个下载源…", LiveTranslateCopy.SwitchingSource);
        Assert.Equal(0.25, LiveTranslateCopy.Progress(25, 100), 5);
        Assert.Equal(0, LiveTranslateCopy.Progress(10, null));
        Assert.DoesNotContain("Hello", LiveTranslateCopy.Listening);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.Downloading(0.5));
        Assert.DoesNotContain("Hello", LiveTranslateCopy.Connecting);
        Assert.Equal("识别模型不完整，正在重新下载…", LiveTranslateCopy.ModelCorrupt);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.ModelLoadFailed);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.NativeLibraryMissing);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.NativeLibraryLoadFailed);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.CpuNotSupported);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.CaptureNoDevice);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.CaptureDenied);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.CaptureFormatUnsupported);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.CaptureFailedGeneric);
        Assert.Equal("请先在设置里填写接口地址、模型和密钥", LiveTranslateCopy.ApiNotConfigured);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.ApiNotConfigured);
        Assert.DoesNotContain("sk-", LiveTranslateCopy.ApiNotConfigured);
        Assert.DoesNotContain("Hello", LiveTranslateCopy.ApiRejected);
    }

    [Fact]
    public void ModelFile_RejectsWrongSizeOrMagic_AndAcceptsGgmlHeader()
    {
        Assert.False(WhisperModelFile.LooksValid(100, "ggml"u8));
        Assert.False(WhisperModelFile.LooksValid(50_000_000, "HTML"u8));
        Assert.False(WhisperModelFile.LooksValid(50_000_000, "ggm"u8));
        Assert.False(WhisperModelFile.LooksValid(200_000_000, "ggml"u8));
        Assert.True(WhisperModelFile.LooksValid(57_000_000, "ggml"u8));
        Assert.True(WhisperModelFile.LooksValid(57_000_000, "ggmf"u8));
        Assert.True(WhisperModelFile.LooksValid(57_000_000, "ggjt"u8));
        Assert.True(WhisperModelFile.LooksValid(57_000_000, "GGUF"u8));
        Assert.True(WhisperModelFile.LooksValid(59_707_625, "lmgg"u8));
        Assert.True(WhisperModelFile.LooksValid(57_000_000, "fmgg"u8));
        Assert.True(WhisperModelFile.LooksValid(57_000_000, "tjgg"u8));
    }

    [Fact]
    public void LoadError_MapsNativeLibraryFailures_WithoutTreatingAsCorruptModel()
    {
        Assert.True(LiveTranslateCopy.TryNativeLibraryError(
            new FileNotFoundException("Native Library not found in default paths."), out var missing));
        Assert.Equal(LiveTranslateCopy.NativeLibraryMissing, missing);

        Assert.True(LiveTranslateCopy.TryNativeLibraryError(
            new Exception("Failed to load native whisper library. Error: The specified module could not be found."), out var loadFailed));
        Assert.Equal(LiveTranslateCopy.NativeLibraryLoadFailed, loadFailed);

        Assert.True(LiveTranslateCopy.TryNativeLibraryError(
            new PlatformNotSupportedException("AVX is not supported on this platform, and noavx runtime is not available."), out var cpu));
        Assert.Equal(LiveTranslateCopy.CpuNotSupported, cpu);

        Assert.False(LiveTranslateCopy.TryNativeLibraryError(
            new InvalidOperationException("Failed to load the whisper model."), out var model));
        Assert.Equal(LiveTranslateCopy.ModelLoadFailed, model);
    }

    [Fact]
    public void WaveFormat_DetectsExtensibleIeeeFloatUsedByWindowsMix()
    {
        Assert.True(WaveFormatKind.TryDetect(WaveFormatKind.EncodingIeeeFloat, 32, null, out var ieee));
        Assert.Equal(WaveKind.Float32, ieee);
        Assert.True(WaveFormatKind.TryDetect(WaveFormatKind.EncodingPcm, 16, null, out var pcm));
        Assert.Equal(WaveKind.Pcm16, pcm);

        Assert.True(WaveFormatKind.TryDetect(
            WaveFormatKind.EncodingExtensible, 32, WaveFormatKind.SubtypeIeeeFloat, out var extFloat));
        Assert.Equal(WaveKind.Float32, extFloat);
        Assert.True(WaveFormatKind.TryDetect(
            WaveFormatKind.EncodingExtensible, 32, null, out var extBare));
        Assert.Equal(WaveKind.Float32, extBare);
        Assert.True(WaveFormatKind.TryDetect(
            WaveFormatKind.EncodingExtensible, 16, WaveFormatKind.SubtypePcm, out var extPcm));
        Assert.Equal(WaveKind.Pcm16, extPcm);
        Assert.True(WaveFormatKind.TryDetect(
            WaveFormatKind.EncodingExtensible, 32, WaveFormatKind.SubtypePcm, out var extPcm32));
        Assert.Equal(WaveKind.Pcm32, extPcm32);
        Assert.True(WaveFormatKind.TryDetect(WaveFormatKind.EncodingPcm, 24, null, out var pcm24));
        Assert.Equal(WaveKind.Pcm24, pcm24);
        Assert.True(WaveFormatKind.TryDetect(WaveFormatKind.EncodingPcm, 32, null, out var pcm32));
        Assert.Equal(WaveKind.Pcm32, pcm32);

        Assert.False(WaveFormatKind.TryDetect(WaveFormatKind.EncodingIeeeFloat, 16, null, out _));
        Assert.False(WaveFormatKind.TryDetect(WaveFormatKind.EncodingPcm, 8, null, out _));
    }

    [Fact]
    public void WaveFormat_ReadsPcmAndExtensibleLayoutsFromBytes()
    {
        var pcm16 = PackWave(1, 2, 48000, 16, 0, null);
        Assert.True(WaveFormatKind.TryReadLayout(pcm16, out var pcm));
        Assert.Equal(48000, pcm.SampleRate);
        Assert.Equal(2, pcm.Channels);
        Assert.Equal(16, pcm.BitsPerSample);
        Assert.Equal(4, pcm.BlockAlign);
        Assert.True(WaveFormatKind.TryDetect(pcm, out var pcmKind));
        Assert.Equal(WaveKind.Pcm16, pcmKind);

        var ieee = PackWave(3, 2, 44100, 32, 0, null);
        Assert.True(WaveFormatKind.TryReadLayout(ieee, out var floatLayout));
        Assert.Equal(44100, floatLayout.SampleRate);
        Assert.True(WaveFormatKind.TryDetect(floatLayout, out var floatKind));
        Assert.Equal(WaveKind.Float32, floatKind);

        var ext = PackWave(0xFFFE, 2, 48000, 32, 22, WaveFormatKind.SubtypeIeeeFloat);
        Assert.True(WaveFormatKind.TryReadLayout(ext, out var extLayout));
        Assert.Equal(WaveFormatKind.EncodingExtensible, extLayout.Encoding);
        Assert.Equal(WaveFormatKind.SubtypeIeeeFloat, extLayout.SubFormat);
        Assert.True(WaveFormatKind.TryDetect(extLayout, out var extKind));
        Assert.Equal(WaveKind.Float32, extKind);

        var extPcm24 = PackWave(0xFFFE, 2, 48000, 24, 22, WaveFormatKind.SubtypePcm);
        Assert.True(WaveFormatKind.TryReadLayout(extPcm24, out var pcm24Layout));
        Assert.True(WaveFormatKind.TryDetect(pcm24Layout, out var pcm24Kind));
        Assert.Equal(WaveKind.Pcm24, pcm24Kind);

        Assert.False(WaveFormatKind.TryReadLayout(new byte[10], out _));
        Assert.False(WaveFormatKind.TryReadLayout(PackWave(1, 0, 48000, 16, 0, null), out _));
    }

    static byte[] PackWave(int encoding, int channels, int rate, int bits, int extra, Guid? subFormat)
    {
        int block = Math.Max(1, ((bits + 7) / 8) * Math.Max(1, channels));
        int size = 18 + extra;
        var blob = new byte[size];
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(0, 2), (ushort)encoding);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(2, 2), (ushort)channels);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(4, 4), rate);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(8, 4), rate * block);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(12, 2), (ushort)block);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(14, 2), (ushort)bits);
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(16, 2), (ushort)extra);
        if (extra >= 22 && subFormat is { } guid)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(18, 2), (ushort)bits);
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(20, 4), 0);
            guid.TryWriteBytes(blob.AsSpan(24, 16));
        }

        return blob;
    }

    [Fact]
    public void CaptureError_MapsDeviceAndPermissionFailures()
    {
        Assert.Equal(
            LiveTranslateCopy.CaptureFormatUnsupported,
            LiveTranslateCopy.MessageForCaptureException(new InvalidOperationException(LiveTranslateCopy.CaptureFormatUnsupported)));
        Assert.Equal(
            LiveTranslateCopy.CaptureNoDevice,
            LiveTranslateCopy.MessageForCaptureException(new Exception("Element not found") { HResult = unchecked((int)0x80070490) }));
        Assert.Equal(
            LiveTranslateCopy.CaptureFailed,
            LiveTranslateCopy.MessageForCaptureException(new Exception("AUDCLNT_E_DEVICE_IN_USE") { HResult = unchecked((int)0x8889000A) }));
        Assert.Equal(
            LiveTranslateCopy.CaptureDenied,
            LiveTranslateCopy.MessageForCaptureException(new Exception("Access is denied") { HResult = unchecked((int)0x80070005) }));
        Assert.Equal(
            LiveTranslateCopy.CaptureOpenFailed,
            LiveTranslateCopy.MessageForCaptureException(new InvalidCastException("Specified cast is not valid.")));
        Assert.Equal(
            LiveTranslateCopy.CaptureFailedGeneric,
            LiveTranslateCopy.MessageForCaptureException(new InvalidOperationException("something else")));
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
    public void Resampler_ReadsPackedPcm24AndPcm32()
    {
        const int sourceRate = 16000;
        var pcm24 = new byte[6];
        // two mono frames of 0x400000 = 0.5
        pcm24[0] = 0x00;
        pcm24[1] = 0x00;
        pcm24[2] = 0x40;
        pcm24[3] = 0x00;
        pcm24[4] = 0x00;
        pcm24[5] = 0x40;
        var resampler24 = new StreamResampler(sourceRate, 1, WaveKind.Pcm24);
        var output24 = new List<float>();
        resampler24.Push(pcm24, output24);
        Assert.NotEmpty(output24);
        Assert.InRange(output24[0], 0.49f, 0.51f);

        var pcm32 = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(pcm32.AsSpan(0, 4), 0x40000000);
        BinaryPrimitives.WriteInt32LittleEndian(pcm32.AsSpan(4, 4), 0x40000000);
        var resampler32 = new StreamResampler(sourceRate, 1, WaveKind.Pcm32);
        var output32 = new List<float>();
        resampler32.Push(pcm32, output32);
        Assert.NotEmpty(output32);
        Assert.InRange(output32[0], 0.49f, 0.51f);
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
        Assert.Equal(520, IslandMetrics.LiveTranslateW);
        Assert.Equal(248, IslandMetrics.LiveTranslateH);
        Assert.Equal(260, IslandMetrics.VLiveTranslateW);
        Assert.Equal(440, IslandMetrics.VLiveTranslateH);
        Assert.True(horizontal.Width + 40 <= IslandMetrics.WindowWidth);
        Assert.True(horizontal.Height + IslandMetrics.TopMargin + 40 <= IslandMetrics.WindowHeight);
        Assert.True(vertical.Width + 40 <= IslandMetrics.VerticalWindowWidth);
        Assert.True(vertical.Height + IslandMetrics.TopMargin + 40 <= IslandMetrics.VerticalWindowHeight);
    }

    [Fact]
    public void Segmenter_UsesShorterHangoverAndMaxUtterance()
    {
        Assert.Equal(320, SpeechSegmenter.FrameSamples);
        Assert.Equal(18, SpeechSegmenter.HangoverFrames);
        Assert.Equal(200, SpeechSegmenter.MaxSpeechFrames);
        Assert.Equal(360, SpeechSegmenter.HangoverFrames * 20);
        Assert.Equal(4000, SpeechSegmenter.MaxSpeechFrames * 20);
    }

    [Fact]
    public void CaptionChain_ShowsTwoItems_WithSessionElapsedTimestamps()
    {
        var chain = new LiveTranslateCaptionChain();
        var start = T(0);
        chain.MarkListening(start);

        int first = chain.Preview("Hello there", T(5000));
        Assert.True(chain.HasPending);
        Assert.Equal("0:05", Assert.Single(chain.Items).Timestamp);
        Assert.Equal("Hello there", chain.Items[0].Primary);
        Assert.True(chain.Items[0].TranslationPending);

        Assert.True(chain.Complete(first, "你好", "Hello there"));
        Assert.False(chain.HasPending);
        Assert.Equal("你好", chain.Items[0].Primary);
        Assert.Equal("Hello there", chain.Items[0].Original);

        int second = chain.Preview("Open the window", T(18000));
        Assert.Equal(2, chain.Items.Count);
        Assert.Equal("0:05", chain.Items[0].Timestamp);
        Assert.Equal("你好", chain.Items[0].Primary);
        Assert.Equal("0:18", chain.Items[1].Timestamp);
        Assert.Equal("Open the window", chain.Items[1].Primary);
        Assert.True(chain.Complete(second, "请打开窗户", "Open the window"));
        Assert.Equal("请打开窗户", chain.Items[1].Primary);
        Assert.Equal("Open the window", chain.Items[1].Original);

        int third = chain.AppendFinal("已经是中文了", T(70000), alreadyChinese: true);
        Assert.Equal(2, chain.Items.Count);
        Assert.Equal("请打开窗户", chain.Items[0].Primary);
        Assert.Equal("1:10", chain.Items[1].Timestamp);
        Assert.Equal("已经是中文了", chain.Items[1].Primary);
        Assert.True(chain.Items[1].AlreadyChinese);
        Assert.Equal(third, chain.Items[1].Id);

        chain.Reset();
        Assert.Empty(chain.Items);
        chain.MarkListening(T(100000));
        int again = chain.Preview("Again", T(101000));
        Assert.Equal("0:01", Assert.Single(chain.Items).Timestamp);
        Assert.NotEqual(first, again);
        Assert.False(chain.Complete(first, "旧的不应写入", null));
        Assert.Equal("Again", chain.Items[0].Primary);
    }

    [Fact]
    public void CaptionChain_PreviewThenFail_KeepsRecognizedText()
    {
        var chain = new LiveTranslateCaptionChain();
        chain.MarkListening(T(0));
        int id = chain.Preview("Bonjour", T(2000));
        Assert.Equal("0:02", chain.Items[0].Timestamp);
        Assert.True(chain.Fail(id));
        Assert.Equal("Bonjour", chain.Items[0].Primary);
        Assert.True(chain.Items[0].TranslationFailed);
        Assert.False(chain.Items[0].TranslationPending);
        Assert.False(chain.Complete(99, "忽略", null));
    }

    [Fact]
    public void CaptionChain_FormatElapsed_UsesMinutesAndSeconds()
    {
        Assert.Equal("0:00", LiveTranslateCaptionChain.FormatElapsed(TimeSpan.Zero));
        Assert.Equal("0:09", LiveTranslateCaptionChain.FormatElapsed(TimeSpan.FromSeconds(9)));
        Assert.Equal("1:05", LiveTranslateCaptionChain.FormatElapsed(TimeSpan.FromSeconds(65)));
        Assert.Equal("10:00", LiveTranslateCaptionChain.FormatElapsed(TimeSpan.FromMinutes(10)));
        Assert.Equal("1:00:00", LiveTranslateCaptionChain.FormatElapsed(TimeSpan.FromHours(1)));
        Assert.Equal("0:00", LiveTranslateCaptionChain.FormatElapsed(TimeSpan.FromSeconds(-3)));
    }

    [Fact]
    public void TranslateApi_JoinsOpenAiCompatibleUrls_AndRejectsNonLocalHttp()
    {
        Assert.Equal(TranslateApiKind.Default, TranslateApiPolicy.Parse(null));
        Assert.Equal(TranslateApiKind.Default, TranslateApiPolicy.Parse("default"));
        Assert.Equal(TranslateApiKind.Custom, TranslateApiPolicy.Parse("custom"));
        Assert.Equal("custom", TranslateApiPolicy.ToSetting(TranslateApiKind.Custom));

        Assert.True(TranslateApiPolicy.TryNormalizeEndpoint("https://api.example.com", out var bare, out _));
        Assert.Equal("https://api.example.com/v1/chat/completions", bare);
        Assert.True(TranslateApiPolicy.TryNormalizeEndpoint("https://api.example.com/", out var slash, out _));
        Assert.Equal("https://api.example.com/v1/chat/completions", slash);
        Assert.True(TranslateApiPolicy.TryNormalizeEndpoint("https://api.example.com/v1", out var v1, out _));
        Assert.Equal("https://api.example.com/v1/chat/completions", v1);
        Assert.True(TranslateApiPolicy.TryNormalizeEndpoint("https://api.example.com/v1/", out var v1slash, out _));
        Assert.Equal("https://api.example.com/v1/chat/completions", v1slash);
        Assert.True(TranslateApiPolicy.TryNormalizeEndpoint("https://openrouter.example/api/v1", out var nested, out _));
        Assert.Equal("https://openrouter.example/api/v1/chat/completions", nested);
        Assert.True(TranslateApiPolicy.TryNormalizeEndpoint("https://api.example.com/v1/chat/completions", out var full, out _));
        Assert.Equal("https://api.example.com/v1/chat/completions", full);
        Assert.True(TranslateApiPolicy.TryNormalizeEndpoint("http://127.0.0.1:11434/v1", out var loopback, out _));
        Assert.Equal("http://127.0.0.1:11434/v1/chat/completions", loopback);
        Assert.True(TranslateApiPolicy.TryNormalizeEndpoint("http://localhost:8080", out var local, out _));
        Assert.Equal("http://localhost:8080/v1/chat/completions", local);
        Assert.True(TranslateApiPolicy.TryNormalizeEndpoint("http://[::1]:1234/v1", out var v6, out _));
        Assert.Equal("http://[::1]:1234/v1/chat/completions", v6);

        Assert.False(TranslateApiPolicy.TryNormalizeEndpoint("", out _, out var missing));
        Assert.Equal("missing", missing);
        Assert.False(TranslateApiPolicy.TryNormalizeEndpoint("not a url", out _, out var invalid));
        Assert.Equal("invalid", invalid);
        Assert.False(TranslateApiPolicy.TryNormalizeEndpoint("http://192.168.1.10:8080/v1", out _, out var scheme));
        Assert.Equal("scheme", scheme);
        Assert.False(TranslateApiPolicy.TryNormalizeEndpoint("ftp://api.example.com", out _, out _));

        var ready = TranslateApiPolicy.From("custom", "https://api.example.com/v1", "demo-model", "secret");
        Assert.True(TranslateApiPolicy.IsReady(ready));
        Assert.False(TranslateApiPolicy.IsReady(TranslateApiPolicy.From("custom", "https://api.example.com/v1", "demo-model", "")));
        Assert.True(TranslateApiPolicy.IsReady(TranslateApiPolicy.From("default", null, null, null)));
    }

    [Fact]
    public void TranslateApi_BuildsChatBody_WithoutTheKey()
    {
        var body = TranslateApiPolicy.ChatCompletionsBody("demo-model", "Hello world");
        Assert.Contains("demo-model", body, StringComparison.Ordinal);
        Assert.Contains("Hello world", body, StringComparison.Ordinal);
        Assert.Contains("简体中文", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("早上好", TranslateApiPolicy.ReadChatCompletion("""{"choices":[{"message":{"content":"早上好"}}]}"""));
        Assert.Null(TranslateApiPolicy.ReadChatCompletion("""{"error":"nope"}"""));
        Assert.Null(TranslateApiPolicy.ReadChatCompletion("not-json"));
    }

    static float[] Voice(int frames)
    {
        var samples = new float[frames * SpeechSegmenter.FrameSamples];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = i % 2 == 0 ? 0.2f : -0.2f;
        return samples;
    }
}
