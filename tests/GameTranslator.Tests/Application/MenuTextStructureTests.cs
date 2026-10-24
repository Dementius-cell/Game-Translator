using GameTranslator.Application.Content;

namespace GameTranslator.Tests.Application;

public sealed class MenuTextStructureTests
{
    public static byte[] Pixels(int scale, int background, bool changedStroke = false)
    {
        var width = 60 * scale; var height = 24 * scale;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var xx = x / scale; var yy = y / scale;
            var ink = yy is >= 4 and <= 19 && xx is >= 5 and <= 50 && xx % 10 < 3;
            if (changedStroke && xx == 11 && yy == 10) ink = false;
            var value = (byte)(ink ? 240 : background + x / (10 * scale));
            var i = (y * width + x) * 4;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SmoothBackgroundMayChangeButOneStrokeMustNot(int scale)
    {
        var before = Pixels(scale, 25);
        Assert.True(MenuTextStructure.Matches(before, Pixels(scale, 55), 60 * scale, 24 * scale, 240 * scale));
        Assert.False(MenuTextStructure.Matches(before, Pixels(scale, 55, true), 60 * scale, 24 * scale, 240 * scale));
    }

    [Fact]
    public void FlatBlankFlashAndAlphaChangesAreNotEvidence()
    {
        var before = Pixels(1, 25);
        Assert.False(MenuTextStructure.Matches(before, Pixels(1, 150), 60, 24, 240));
        Assert.False(MenuTextStructure.Matches(new byte[5760], new byte[5760], 60, 24, 240));
        var changed = Pixels(1, 40); changed[3] = 0;
        Assert.False(MenuTextStructure.Matches(before, changed, 60, 24, 240));
        Assert.False(MenuTextStructure.Matches(before, before.AsSpan(4), 60, 24, 240));
    }
}
