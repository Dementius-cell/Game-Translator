using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuUnchangedRegionTests
{
    private static readonly Guid Source = Guid.NewGuid();

    [Theory]
    [InlineData(512, 256, true)]
    [InlineData(513, 256, false)]
    public void ComparisonHasBoundedPixelCost(int width, int height, bool matches)
    {
        var frame = new CapturedFrame(new CaptureRegion(0, 0, width, height), width, height, width * 4,
            "Bgra32", new byte[width * height * 4], DateTimeOffset.UnixEpoch,
            new CaptureFrameOrigin(Source, TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        var region = new TextCandidateRegion(new TextCandidate(new(0, 0, width, height), .95), frame);
        Assert.Equal(matches, MenuUnchangedRegion.Matches(region, region));
    }

    [Fact]
    public void ChangedRecognitionMarginCannotReuseIdenticalCandidatePixels()
    {
        var origin = new CaptureFrameOrigin(Source, TimeSpan.Zero, DateTimeOffset.UnixEpoch);
        var pixels = new byte[64 * 24 * 4];
        CapturedFrame Frame() => new(new CaptureRegion(0, 0, 64, 24), 64, 24, 256, "Bgra32",
            pixels, DateTimeOffset.UnixEpoch, origin);
        var frame = Frame();
        var candidate = new TextCandidate(new(0, 0, 64, 24), .95);
        var baseline = new TextCandidateRegion(candidate, frame) { RecognitionContextFrame = Frame() };
        pixels[^1] = 1;
        var changed = new TextCandidateRegion(candidate, frame) { RecognitionContextFrame = Frame() };
        Assert.False(MenuUnchangedRegion.Matches(baseline, changed));
    }

    [Theory]
    [InlineData("same", true)]
    [InlineData("last-pixel", false)]
    [InlineData("source", false)]
    [InlineData("unknown", false)]
    [InlineData("position", false)]
    [InlineData("members", false)]
    [InlineData("context", false)]
    [InlineData("value-row", false)]
    public void ExactEvidenceIncludesWholeCropIdentityGeometryAndRecognitionContext(string change, bool matches)
    {
        var pixels = Enumerable.Repeat((byte)42, 64 * 24 * 4).ToArray();
        CapturedFrame Frame(byte[] data, Guid? source, int x = 0) => new(new CaptureRegion(x, 0, 64, 24),
            64, 24, 256, "Bgra32", data, DateTimeOffset.UnixEpoch,
            source is { } id ? new CaptureFrameOrigin(id, TimeSpan.Zero, DateTimeOffset.UnixEpoch) : null);
        var bounds = new BoundingBox(0, 0, 64, 24);
        var baseline = new TextCandidateRegion(new TextCandidate(bounds, .95), Frame(pixels, Source));
        if (change == "last-pixel") pixels[^4]++;
        var candidate = new TextCandidate(bounds, .95)
        {
            SourceCandidateBounds = change == "members" ? [new BoundingBox(1, 0, 63, 24)] : [bounds],
            HasMenuValueRowEvidence = change == "value-row"
        };
        var current = new TextCandidateRegion(candidate,
            Frame(pixels, change == "unknown" ? null : change == "source" ? Guid.NewGuid() : Source,
                change == "position" ? 1 : 0))
        { RecognitionContextFrame = change == "context" ? Frame(pixels, Source) : null };
        Assert.Equal(matches, MenuUnchangedRegion.Matches(baseline, current));
    }
}
