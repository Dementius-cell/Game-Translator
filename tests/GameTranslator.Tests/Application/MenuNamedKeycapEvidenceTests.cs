using GameTranslator.Application.Capture;
using GameTranslator.Application.Content;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class MenuNamedKeycapEvidenceTests
{
    [Theory]
    [InlineData("Tab]", 1, 3)]
    [InlineData("[Alt]", 2, 3)]
    [InlineData(@"/ Tab \", 4, 3)]
    [InlineData("| LB |", 2, 2)]
    public void KnownDecoratedNameNeedsMatchingPixelGlyphs(string text, int scale, int glyphs)
    {
        var raw = Fixture(text, scale, glyphs);
        Assert.True(MenuNamedKeycapEvidence.Matches(raw));
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
    [InlineData("Tab]", "one-glyph")]
    [InlineData("Tab]", "two-glyphs")]
    [InlineData("Tab]", "no-glyph")]
    [InlineData("Tab]", "four-glyphs")]
    [InlineData("Tab]", "misaligned")]
    [InlineData("Tab]", "edge-connected")]
    [InlineData("LB]", "edge-connected")]
    [InlineData("Tab]", "dark")]
    [InlineData("Tab]", "dim")]
    [InlineData("Tab]", "clipped")]
    [InlineData("Tab]", "taper")]
    [InlineData("Tab]", "no-detector")]
    [InlineData("Tab]", "multiple-lines")]
    [InlineData("Tab]", "multiple-blocks")]
    [InlineData("Tab]", "small-detector")]
    [InlineData("Tab]", "vertical")]
    [InlineData("Tab]", "auto")]
    [InlineData("Tab]", "comic")]
    [InlineData("[On]", "two-glyphs")]
    [InlineData("[Cat]", "")]
    [InlineData("[tab]", "")]
    [InlineData("[Tab ]]", "")]
    [InlineData("[T ab]", "")]
    [InlineData("Ta/b]", "")]
    [InlineData("Tab.", "")]
    [InlineData("Tab Exit", "")]
    public void UnprovenShapeOrNameRetainsRecognition(string text, string condition)
    {
        var raw = Fixture(text, 1, 3, condition);
        Assert.False(MenuNamedKeycapEvidence.Matches(raw));
        // The separate one-glyph detector may legitimately exclude one-glyph shapes.
        if (condition == "one-glyph") return;
        var projected = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = raw.Request.ContentLayoutMode });
        Assert.Equal(raw.TextBlocks.Select(b => b.Text), projected.TextBlocks.Select(b => b.Text));
    }

    private static OcrResult Fixture(string text, int scale, int glyphs, string condition = "")
    {
        glyphs = condition switch { "one-glyph" => 1, "two-glyphs" => 2, "no-glyph" => 0, "four-glyphs" => 4, _ => glyphs };
        int size = 32 * scale;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            int px = x / scale, py = y / scale;
            var white = px >= (condition == "clipped" ? 0 : 2) && px < 30 && py >= 2 && py < 30;
            if (condition == "taper") white &= px < 30 - Math.Max(0, (py - 6) / 2);
            var ink = false;
            for (var g = 0; g < glyphs; g++)
            {
                var gx = glyphs == 2 ? 9 + g * 10 : glyphs == 4 ? 4 + g * 6 : 5 + g * 8;
                var gy = condition == "misaligned" && g == 1 ? 14 : 10;
                ink |= py >= gy && py < gy + 12 && (px >= gx && px < gx + 2 || py >= gy + 10 && px >= gx && px < gx + 5);
            }
            if (condition == "edge-connected" && px == 5 && py < 12) ink = true;
            var value = (byte)(white && !ink ? (condition == "dim" ? 120 : 240) : 5);
            if (condition == "dark") value = (byte)(255 - value);
            var i = (y * size + x) * 4;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }
        var frame = new CapturedFrame(new(100, 200, size, size), size, size, size * 4, "Bgra32", pixels, DateTimeOffset.UnixEpoch);
        var bounds = new BoundingBox(0, 0, size, size);
        var request = new OcrRequest(frame, "eng",
            orientationMode: condition == "vertical" ? OcrOrientationMode.Vertical : condition == "auto" ? OcrOrientationMode.Auto : OcrOrientationMode.Horizontal,
            contentLayoutMode: condition == "comic" ? ContentLayoutMode.DialogComic : ContentLayoutMode.StaticMenu)
        { DetectorLineBounds = condition == "no-detector" ? [] : condition == "multiple-lines" ? [bounds, bounds]
            : condition == "small-detector" ? [new(0, 0, size / 2, size / 2)] : [bounds] };
        var block = new OcrTextBlock(text, bounds);
        return new OcrResult(request, condition == "multiple-blocks" ? [block, block] : [block], DateTimeOffset.UnixEpoch,
            words: [new OcrWord(text, bounds, .9, "pass")]);
    }
}
