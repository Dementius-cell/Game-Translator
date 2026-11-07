using System.Buffers.Binary;
using System.Runtime.InteropServices;
using GameTranslator.Infrastructure.Capture;

namespace GameTranslator.Tests.Infrastructure;

public sealed class HdrCaptureTests
{
    [Fact]
    public void ModeChangesRetireCacheBeforeSwitchAndPollingIsBounded()
    {
        var monitor = new CaptureColorMonitor();
        var next = new CaptureColorInfo("SDR", 80, "synthetic");
        var reads = 0;
        var retired = new List<string>();
        CaptureColorInfo Read() { reads++; return next; }
        void Retire() => retired.Add(monitor.Current.Mode);
        Assert.True(monitor.Refresh(TimeSpan.Zero, Read, Retire));
        next = new("HDR", 240, "synthetic");
        Assert.False(monitor.Refresh(TimeSpan.FromMilliseconds(999), Read, Retire));
        Assert.Equal(1, reads);
        Assert.True(monitor.Refresh(TimeSpan.FromSeconds(1), Read, Retire));
        Assert.Equal("HDR", monitor.Current.Mode);
        Assert.False(monitor.Refresh(TimeSpan.FromSeconds(2), Read, Retire));
        next = next with { SdrWhiteNits = 300 };
        Assert.True(monitor.Refresh(TimeSpan.FromSeconds(3), Read, Retire));
        next = new("SDR", 80, "synthetic");
        Assert.True(monitor.Refresh(TimeSpan.FromSeconds(4), Read, Retire));
        Assert.Equal(new[] { "Unknown", "SDR", "HDR", "HDR" }, retired);
    }

    [Fact]
    public void FailedRetirementDoesNotCommitModeOrDelayRetry()
    {
        var monitor = new CaptureColorMonitor();
        var hdr = new CaptureColorInfo("HDR", 240, "synthetic");
        Assert.Throws<InvalidOperationException>(() => monitor.Refresh(TimeSpan.Zero, () => hdr, () => throw new InvalidOperationException()));
        Assert.Equal(CaptureColorInfo.Unknown, monitor.Current);
        Assert.True(monitor.Refresh(TimeSpan.Zero, () => hdr, () => { }));
    }

    [Theory]
    [InlineData(0u, 0u, "SDR", false)]
    [InlineData(0x31u, 0u, "SDR", false)] // supported/user-enabled is not active HDR
    [InlineData(0x33u, 2u, "HDR", true)]
    [InlineData(0xC3u, 1u, "WCG", true)]
    [InlineData(0u, 99u, "Unknown", true)]
    public void ActiveModeControlsCaptureInsteadOfCapability(uint flags, uint active, string mode, bool fp16)
    {
        var result = CaptureColorInfo.FromAdvancedColor(flags, active, 3000);
        Assert.Equal(mode, result.Mode);
        Assert.Equal(fp16, result.UseFloatCapture);
        Assert.Equal(240, result.SdrWhiteNits);
    }

    [Theory]
    [InlineData(1u, "SDR")]
    [InlineData(3u, "HDR")]
    [InlineData(7u, "WCG")]
    public void OlderWindowsUsesEnabledFlag(uint flags, string mode)
    {
        Assert.Equal(mode, CaptureColorInfo.FromAdvancedColor(flags, null, null).Mode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void InvalidWhiteLevelUsesDocumentedScRgbReference(uint? level)
    {
        Assert.Equal(80, CaptureColorInfo.FromAdvancedColor(3, 2, level).SdrWhiteNits);
        Assert.True(CaptureColorInfo.Unknown.UseFloatCapture); // never silently clip unknown HDR to UNORM
    }

    [Fact]
    public void NativeDisplayConfigPacketsMatchWindowsAbi()
    {
        Assert.Equal(20, Marshal.SizeOf<MonitorColorInfoReader.Header>());
        Assert.Equal(20, Marshal.SizeOf<MonitorColorInfoReader.SourceInfo>());
        Assert.Equal(48, Marshal.SizeOf<MonitorColorInfoReader.TargetInfo>());
        Assert.Equal(72, Marshal.SizeOf<MonitorColorInfoReader.PathInfo>());
        Assert.Equal(64, Marshal.SizeOf<MonitorColorInfoReader.ModeInfo>());
        Assert.Equal(84, Marshal.SizeOf<MonitorColorInfoReader.SourceName>());
        Assert.Equal(32, Marshal.SizeOf<MonitorColorInfoReader.ColorInfo>());
        Assert.Equal(36, Marshal.SizeOf<MonitorColorInfoReader.ColorInfo2>());
        Assert.Equal(24, Marshal.SizeOf<MonitorColorInfoReader.WhiteLevel>());
    }

    [Fact]
    public void ToneMappingKeepsHighlightStepsAndNormalizesWindowsWhite()
    {
        var mapper = new HdrToneMapper(80);
        var ramp = new[] { 0f, .003f, .18f, .5f, .75f, 1f, 2f, 4f };
        var pixels = mapper.Convert(Pixels(ramp), ramp.Length, 1, ramp.Length * 8);
        for (var i = 1; i < ramp.Length; i++) Assert.True(pixels[i * 4] > pixels[(i - 1) * 4]);
        Assert.InRange(pixels[2 * 4], 117, 119); // linear .18 encoded as sRGB
        var brighterWhite = new HdrToneMapper(240).Convert(Pixels(ramp.Select(v => v * 3).ToArray()), ramp.Length, 1, ramp.Length * 8);
        for (var i = 0; i < pixels.Length; i++) Assert.InRange(Math.Abs(pixels[i] - brighterWhite[i]), 0, 1);
    }

    [Fact]
    public void ToneMappingIsSceneIndependentAndHandlesSpecialValues()
    {
        var mapper = new HdrToneMapper(80);
        var values = new[] { -.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, .5f };
        var pixels = mapper.Convert(Pixels(values), values.Length, 1, values.Length * 8);
        Assert.Equal(new byte[] { 0, 0, 255, 0, 188 }, Enumerable.Range(0, values.Length).Select(i => pixels[i * 4]).ToArray());
        Assert.Equal(mapper.Convert(Pixels(.5f), 1, 1, 8), pixels.AsSpan(16, 4).ToArray());
        Assert.All(Enumerable.Range(0, values.Length), i => Assert.Equal(255, pixels[i * 4 + 3]));
    }

    [Fact]
    public void ToneMappingRespectsRowPaddingAndRgbaToBgraOrder()
    {
        var bytes = new byte[32];
        Write(bytes, 0, 1); // red in first row
        Write(bytes, 16 + 4, 1); // blue in second row
        Array.Fill(bytes, (byte)255, 8, 8); // padding must not become pixels
        var result = new HdrToneMapper(80).Convert(bytes, 1, 2, 16);
        Assert.True(result[2] > 200);
        Assert.Equal(0, result[0]);
        Assert.True(result[4] > 200);
        Assert.Equal(0, result[6]);
        Assert.Equal(8, result.Length);
        Assert.Throws<ArgumentException>(() => new HdrToneMapper(80).Convert(bytes, 2, 2, 8));
    }

    private static byte[] Pixels(params float[] gray)
    {
        var result = new byte[gray.Length * 8];
        for (var i = 0; i < gray.Length; i++)
            for (var c = 0; c < 3; c++) Write(result, i * 8 + c * 2, gray[i]);
        return result;
    }
    private static void Write(byte[] data, int offset, float value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), BitConverter.HalfToUInt16Bits((Half)value));
}
