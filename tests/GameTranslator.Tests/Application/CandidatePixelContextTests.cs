using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class CandidatePixelContextTests
{
    [Fact]
    public void FixedContext_RejectsChangedPixelsSourceAndExpansionOutsideEvidence()
    {
        var id = Guid.NewGuid();
        var pixels = new byte[100 * 80 * 4];
        CapturedFrame Frame(byte[] data, Guid source, int x = 0) => new(new(x, 0, 100, 80),
            100, 80, 400, "Bgra32", data, DateTimeOffset.UnixEpoch,
            new CaptureFrameOrigin(source, TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        var bounds = new BoundingBox(20, 20, 50, 30);
        var context = CandidatePixelContext.Create(Frame(pixels, id), bounds)!;
        Assert.True(context.Matches(Frame(pixels, id), new(18, 20, 52, 30)));
        Assert.False(context.Matches(Frame(pixels, id), new(17, 20, 53, 30)));
        Assert.False(context.Matches(Frame(pixels, Guid.NewGuid()), bounds));
        Assert.False(context.Matches(Frame(pixels, id, 1), bounds));
        pixels[(25 * 100 + 18) * 4] = 1; // Added content outside the old crop is still a source change.
        Assert.False(context.Matches(Frame(pixels, id), bounds));
        pixels[(25 * 100 + 18) * 4] = 0;
        pixels[0] = 1; // Unrelated zone pixels do not invalidate this candidate.
        Assert.True(context.Matches(Frame(pixels, id), bounds));
    }

    [Fact]
    public void Context_IsBoundedAndRejectsUnsupportedPixels()
    {
        var large = new CapturedFrame(new(0, 0, 600, 600), 600, 600, 2400,
            "Bgra32", new byte[600 * 600 * 4], DateTimeOffset.UnixEpoch);
        Assert.Null(CandidatePixelContext.Create(large, new(0, 0, 600, 600)));
        var unsupported = new CapturedFrame(new(0, 0, 20, 20), 20, 20, 20,
            "Gray8", new byte[400], DateTimeOffset.UnixEpoch);
        Assert.Null(CandidatePixelContext.Create(unsupported, new(1, 1, 10, 10)));
    }
}
