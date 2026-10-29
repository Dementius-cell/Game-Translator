using GameTranslator.Application.Capture;
using GameTranslator.Application.Content;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class MenuTriggerKeycapEvidenceTests
{
    [Theory]
    [InlineData(1, false, "G")]
    [InlineData(2, true, "RT,")]
    [InlineData(4, false, "xy")]
    public void TaperedTwoGlyphKeycap_ExcludesOnlyTranslation(int scale, bool mirror, string text)
    {
        var raw = Fixture(scale, mirror, text);
        Assert.True(MenuTriggerKeycapEvidence.Matches(raw));
        var projected = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = ContentLayoutMode.StaticMenu });
        Assert.Empty(projected.TextBlocks);
        Assert.Equal(text, Assert.Single(raw.TextBlocks).Text);
        Assert.Equal("pass", Assert.Single(raw.Words).RecognitionPassId);
        Assert.Same(raw.Request, projected.Request);
        Assert.Equal(raw.RecognizedAt, projected.RecognizedAt);
        Assert.Equal(raw.Region, projected.Region);
    }

    [Theory]
    [InlineData("rectangle")]
    [InlineData("symmetric")]
    [InlineData("reverse")]
    [InlineData("one-glyph")]
    [InlineData("third-glyph")]
    [InlineData("misaligned")]
    [InlineData("dim")]
    [InlineData("comic")]
    [InlineData("vertical")]
    [InlineData("no-detector")]
    [InlineData("multiple-lines")]
    [InlineData("prose")]
    public void AmbiguousShapeOrUnsupportedRoute_RetainsText(string condition)
    {
        var raw = Fixture(1, false, condition == "prose" ? "Press RT" : "On", condition);
        Assert.False(MenuTriggerKeycapEvidence.Matches(raw));
        var projected = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = raw.Request.ContentLayoutMode });
        Assert.Equal(raw.TextBlocks.Select(b => b.Text), projected.TextBlocks.Select(b => b.Text));
    }

    private static OcrResult Fixture(int scale, bool mirror, string text, string condition = "")
    {
        int width = 32 * scale, height = 36 * scale;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var px = (mirror ? width - 1 - x : x) / scale;
            var py = y / scale;
            var taper = Math.Max(0, (py - 9) / 2);
            var left = condition == "rectangle" ? 1 : condition == "reverse" ? 14 - taper : 1 + taper;
            var right = condition == "symmetric" ? 30 - taper : 30;
            var white = py >= 3 && px >= left && px <= right;
            var first = py >= 13 && py <= 24 && ((px >= 12 && px <= 13) || (py >= 23 && px >= 12 && px <= 17));
            var secondY = condition == "misaligned" ? py - 5 : py;
            var second = secondY >= 13 && secondY <= 24 && ((secondY <= 14 && px >= 19 && px <= 25) || (px >= 22 && px <= 23));
            if (condition == "one-glyph") second = false;
            var third = condition == "third-glyph" && py >= 13 && py <= 24 && px >= 27 && px <= 28;
            var value = (byte)(white && !first && !second && !third ? (condition == "dim" ? 100 : 240) : 5);
            var offset = y * width * 4 + x * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        var frame = new CapturedFrame(new(100, 200, width, height), width, height, width * 4, "Bgra32", pixels, DateTimeOffset.UnixEpoch);
        var bounds = new BoundingBox(0, 0, width, height);
        var request = new OcrRequest(frame, "eng",
            orientationMode: condition == "vertical" ? OcrOrientationMode.Vertical : OcrOrientationMode.Horizontal,
            contentLayoutMode: condition == "comic" ? ContentLayoutMode.DialogComic : ContentLayoutMode.StaticMenu)
        { DetectorLineBounds = condition == "no-detector" ? [] : condition == "multiple-lines" ? [bounds, bounds] : [bounds] };
        return new OcrResult(request, [new OcrTextBlock(text, bounds)], DateTimeOffset.UnixEpoch,
            words: [new OcrWord(text, bounds, .9, "pass")]);
    }
}
