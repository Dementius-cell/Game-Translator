using GameTranslator.Application.Capture;
using GameTranslator.Application.Content;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class MenuControlTextProjectionTests
{
    [Theory]
    [InlineData("B")]
    [InlineData("LB")]
    [InlineData("Enter")]
    [InlineData("Shift")]
    [InlineData("F12")]
    public void StandaloneHints_AreExcludedOnlyFromMenuProjection(string token)
    {
        var raw = Raw(new[] { token });
        var menu = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = ContentLayoutMode.StaticMenu });
        Assert.Empty(menu.TextBlocks);
        Assert.Equal(token, Assert.Single(raw.TextBlocks).Text);
        var comic = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = ContentLayoutMode.DialogComic });
        Assert.Equal(token, Assert.Single(comic.TextBlocks).Text);
    }

    [Theory]
    [InlineData("A useful tool")]
    [InlineData("Hold B to continue.")]
    [InlineData("LB changes the selected item.")]
    [InlineData("Enter the room")]
    [InlineData("Standard")]
    public void StandaloneHints_DoNotRemoveWordsFromProse(string text)
    {
        var raw = Raw(new[] { text });
        var result = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = ContentLayoutMode.StaticMenu });
        Assert.Equal(text, Assert.Single(result.TextBlocks).Text);
    }
    private static OcrResult Raw(string[] tokens, int lineCount = 1)
    {
        var frame = new CapturedFrame(new(0, 0, 800, 100), 800, 100, 3200, "Bgra32", new byte[320000], DateTimeOffset.UnixEpoch);
        var request = new OcrRequest(frame, "eng", orientationMode: OcrOrientationMode.Horizontal,
            contentLayoutMode: ContentLayoutMode.StaticMenu)
        { DetectorLineBounds = Enumerable.Range(0, lineCount).Select(i => new BoundingBox(0, i * 30, 800, 20)).ToArray() };
        return new OcrResult(request, new[] { new OcrTextBlock(string.Join(' ', tokens), new(0, 0, 800, 20)) },
            DateTimeOffset.UnixEpoch, words: tokens.Select((t, i) => new OcrWord(t, new(i * 90, 0, t.StartsWith('(') ? 20 : 80, 20), 95, "same-pass")));
    }

    [Fact]
    public void MenuControls_StaySeparateEvenWhenTransientZoneUsesWholeZone()
    {
        var raw = Raw(new[] { "Inventory", "(A)", "Use", "(B)", "Back" });
        var result = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = ContentLayoutMode.StaticMenu, TranslationGroupingMode = TranslationGroupingMode.WholeZone });
        Assert.Equal(new[] { "Inventory", "Use", "Back" }, result.TextBlocks.Select(b => b.Text));
        Assert.Single(raw.TextBlocks);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MenuControls_DoNotSplitInstructions(int lineCount)
    {
        Assert.Null(MenuControlTextProjection.TryCreate(Raw(new[] { "Press", "(A)", "then", "(B)" }, lineCount)));
        Assert.Null(MenuControlTextProjection.TryCreate(Raw(new[] { "Hold", "(A)", "to", "continue." }, lineCount)));
    }
}
