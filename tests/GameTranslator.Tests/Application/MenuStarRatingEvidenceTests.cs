using GameTranslator.Application.Capture;
using GameTranslator.Application.Content;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class MenuStarRatingEvidenceTests
{
    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 4)]
    [InlineData(4, 5)]
    public void RepeatedFilledStarsExcludeProjectionWithoutChangingRawOcr(int scale, int count)
    {
        var raw = Fixture(scale, count);
        Assert.True(MenuStarRatingEvidence.Matches(raw));
        var projected = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = ContentLayoutMode.StaticMenu });
        Assert.Empty(projected.TextBlocks);
        Assert.Equal("Synthetic OCR", Assert.Single(raw.TextBlocks).Text);
        Assert.Equal("pass", Assert.Single(raw.Words).RecognitionPassId);
        Assert.Single(raw.TextBlockSources);
        Assert.Same(raw.Request, projected.Request);
        Assert.Equal(raw.Region, projected.Region);
        Assert.Equal(raw.RecognizedAt, projected.RecognizedAt);
    }

    [Theory]
    [InlineData("circles")]
    [InlineData("rectangles")]
    [InlineData("triangles")]
    [InlineData("letters")]
    [InlineData("digits")]
    [InlineData("punctuation")]
    [InlineData("hollow")]
    [InlineData("partial")]
    [InlineData("mixed")]
    [InlineData("dim")]
    [InlineData("one")]
    [InlineData("two")]
    [InlineData("six")]
    [InlineData("misaligned")]
    [InlineData("extra-mark")]
    [InlineData("comic")]
    [InlineData("vertical")]
    [InlineData("auto")]
    [InlineData("no-detector")]
    [InlineData("multi-detector")]
    [InlineData("multi-block")]
    public void TextOtherShapesAndUnsupportedRoutesRetainProjection(string condition)
    {
        var raw = Fixture(1, condition == "one" ? 1 : condition == "two" ? 2 : condition == "six" ? 6 : 3, condition);
        Assert.False(MenuStarRatingEvidence.Matches(raw));
        var projected = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = raw.Request.ContentLayoutMode });
        Assert.Equal(raw.TextBlocks.Select(b => b.Text), projected.TextBlocks.Select(b => b.Text));
    }

    private static OcrResult Fixture(int scale, int count, string condition = "")
    {
        int width = (count * 28 + 4) * scale, height = 30 * scale;
        var pixels = new byte[width * height * 4];
        var vertices = Enumerable.Range(0, 10).Select(i =>
        {
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            var radius = i % 2 == 0 ? 12 : 12 * .382;
            return (X: 12.5 + radius * Math.Cos(angle), Y: 12 + radius * Math.Sin(angle));
        }).ToArray();
        string[] letter = ["10001", "10001", "10001", "10101", "10101", "11011", "10001"];
        string[] digit = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var local = x / scale - 2; var index = local / 28;
            var px = local % 28 + .5; var py = y / scale - 2 + .5;
            if (condition == "misaligned" && index == 1) py -= 5;
            var ink = false;
            if (local >= 0 && index < count && px < 24 && py >= 0 && py < 24)
            {
                for (var i = 0; i < vertices.Length; i++)
                {
                    var a = vertices[i]; var b = vertices[(i + 9) % 10];
                    if ((a.Y > py) != (b.Y > py) && px < (b.X - a.X) * (py - a.Y) / (b.Y - a.Y) + a.X) ink = !ink;
                }
                if (condition == "circles") ink = Math.Pow(px - 12, 2) + Math.Pow(py - 12, 2) < 120;
                if (condition == "rectangles") ink = true;
                if (condition == "triangles") ink = Math.Abs(px - 12) < py / 2;
                if (condition is "letters" or "digits") ink = (condition == "letters" ? letter : digit)[(int)(py * 7 / 24)][(int)(px * 5 / 24)] == '1';
                if (condition == "punctuation") ink = py > 20 && px > 10 && px < 14;
                if (condition == "hollow" && Math.Pow(px - 12, 2) + Math.Pow(py - 12, 2) < 25) ink = false;
                if (condition == "partial" && py > 14) ink = false;
                if (condition == "mixed" && index == count - 1) ink = letter[(int)(py * 7 / 24)][(int)(px * 5 / 24)] == '1';
            }
            if (condition == "extra-mark" && x / scale == 28 && y / scale == 1) ink = true;
            var value = ink ? (byte)(condition == "dim" ? 100 : 240) : (byte)5;
            var offset = (y * width + x) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value; pixels[offset + 3] = 255;
        }
        var bounds = new BoundingBox(0, 0, width, height);
        var frame = new CapturedFrame(new(100, 200, width, height), width, height, width * 4, "Bgra32", pixels, DateTimeOffset.UnixEpoch);
        var request = new OcrRequest(frame, "eng",
            orientationMode: condition == "vertical" ? OcrOrientationMode.Vertical : condition == "auto" ? OcrOrientationMode.Auto : OcrOrientationMode.Horizontal,
            contentLayoutMode: condition == "comic" ? ContentLayoutMode.DialogComic : ContentLayoutMode.StaticMenu)
        { DetectorLineBounds = condition == "no-detector" ? [] : condition == "multi-detector" ? [bounds, bounds] : [bounds] };
        return new(request, condition == "multi-block" ? [new("Synthetic OCR", bounds), new("Extra text", bounds)] : [new("Synthetic OCR", bounds)],
            DateTimeOffset.UnixEpoch, words: [new("Synthetic OCR", bounds, .9, "pass")]);
    }
}
