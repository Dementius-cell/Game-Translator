using GameTranslator.Application.Content;
using GameTranslator.Application.Translation;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class MenuTranslationIntegrityTests
{
    [Theory]
    [InlineData("EE", 1)]
    [InlineData("ED", 2)]
    [InlineData("ny", 4)]
    public void KeycapEvidence_ExcludesProjectionWithoutChangingRawRecognition(string text, int scale)
    {
        var raw = Keycap(text, scale);
        var projected = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = ContentLayoutMode.StaticMenu });
        Assert.Empty(projected.TextBlocks);
        Assert.Equal(text, Assert.Single(raw.TextBlocks).Text);
        Assert.Equal("pass", Assert.Single(raw.Words).RecognitionPassId);
        Assert.Same(raw.Request, projected.Request);
        Assert.Equal(raw.RecognizedAt, projected.RecognizedAt);
        Assert.Equal(32 * scale, raw.InputWidth);
    }

    [Theory]
    [InlineData("two-glyphs")]
    [InlineData("no-glyph")]
    [InlineData("dark")]
    [InlineData("connected-to-edge")]
    [InlineData("no-detector")]
    [InlineData("multiple-lines")]
    [InlineData("multiple-blocks")]
    [InlineData("comic")]
    [InlineData("vertical")]
    [InlineData("prose")]
    public void KeycapEvidence_RetainsOrdinaryLabelsAndUnsupportedRoutes(string condition)
    {
        var raw = Keycap(condition == "prose" ? "Press E" : "On", 1, condition);
        var projected = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = condition == "comic" ? ContentLayoutMode.DialogComic : ContentLayoutMode.StaticMenu });
        Assert.Equal(raw.TextBlocks.Select(b => b.Text), projected.TextBlocks.Select(b => b.Text));
    }

    [Theory]
    [InlineData("Q", true)]
    [InlineData("R", true)]
    [InlineData("ESC", true)]
    [InlineData("SPACE", true)]
    [InlineData("Space", true)]
    [InlineData("space", false)]
    [InlineData("SPACE STORAGE", false)]
    [InlineData("Press SPACE", false)]
    [InlineData("SPACES", false)]
    [InlineData("Escape", false)]
    [InlineData("Quality", false)]
    [InlineData("Press Q", false)]
    [InlineData("EQ", false)]
    public void StandaloneKeyHint_DoesNotMatchLettersInsideText(string text, bool expected) =>
        Assert.Equal(expected, MenuTranslationIntegrity.IsStandaloneControlHint(text));

    [Theory]
    [InlineData(ContentLayoutMode.StaticMenu, "SPACE", true)]
    [InlineData(ContentLayoutMode.StaticMenu, "SPACE STORAGE", false)]
    [InlineData(ContentLayoutMode.StaticMenu, "space", false)]
    [InlineData(ContentLayoutMode.DialogComic, "SPACE", false)]
    public void SpaceKeyProjection_PreservesRawAndOrdinaryText(ContentLayoutMode mode, string text, bool excluded)
    {
        var raw = Keycap(text, 1, "dark");
        var projected = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = mode });
        Assert.Equal(excluded ? 0 : 1, projected.TextBlocks.Count);
        Assert.Equal(text, Assert.Single(raw.TextBlocks).Text);
        Assert.Same(raw.Request, projected.Request);
        Assert.Equal(raw.RecognizedAt, projected.RecognizedAt);
        Assert.Equal(raw.Words, projected.Words);
    }

    private static OcrResult Keycap(string text, int scale, string condition = "")
    {
        var size = 32 * scale;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var px = x / scale; var py = y / scale;
            var white = px >= 2 && px < 30 && py >= 2 && py < 30;
            var glyph = condition == "two-glyphs"
                ? py >= 8 && py < 24 && ((px >= 9 && px < 13) || (px >= 20 && px < 24))
                : px >= 12 && px < 19 && py >= 8 && py < 24;
            if (condition == "no-glyph") glyph = false;
            if (condition == "connected-to-edge" && px == 15 && py < 10) glyph = true;
            var value = (byte)(white && !glyph ? 240 : 5);
            if (condition == "dark") value = (byte)(255 - value);
            var offset = (y * size + x) * 4;
            pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = value;
            pixels[offset + 3] = 255;
        }
        var frame = new CapturedFrame(new(0, 0, size, size), size, size, size * 4, "Bgra32", pixels, DateTimeOffset.UnixEpoch);
        var bounds = new BoundingBox(0, 0, size, size);
        var request = new OcrRequest(frame, "eng", orientationMode: condition == "vertical" ? OcrOrientationMode.Vertical : OcrOrientationMode.Horizontal,
            contentLayoutMode: condition == "comic" ? ContentLayoutMode.DialogComic : ContentLayoutMode.StaticMenu)
        { DetectorLineBounds = condition == "no-detector" ? [] : condition == "multiple-lines" ? [bounds, bounds] : [bounds] };
        var block = new OcrTextBlock(text, bounds);
        return new OcrResult(request, condition == "multiple-blocks" ? [block, new OcrTextBlock("Description", bounds)] : [block],
            DateTimeOffset.UnixEpoch, words: [new OcrWord(text, bounds, .9, "pass")]);
    }

    [Theory]
    [InlineData("HP 120/150 [E]", "Здоровье 120/150 [E]", false)]
    [InlineData("HP 120/150 [E]", "Здоровье 120/150 [У]", true)]
    [InlineData("Chance 50%", "Шанс 60%", true)]
    [InlineData("Cost 12.5", "Цена", true)]
    [InlineData("Continue", "Продолжить 2", true)]
    [InlineData("Continue", "Продолжить", false)]
    [InlineData("Power Cell x2", "Энергоячейка x2", false)]
    [InlineData("Power Cell x2", "Энергоячейка х2", true)]
    [InlineData("Memory 4.5 GB", "Память 4.5 ГБ", true)]
    [InlineData("LB", "фунт", true)]
    [InlineData("B", "Б", true)]
    [InlineData("LB", "LB", false)]
    [InlineData("Power Cell X2", "Энергоячейка Х2", true)]
    [InlineData("Power Cell X2", "Энергоячейка X2", false)]
    [InlineData("Power Cell ×2", "Энергоячейка ×2", false)]
    public void Validate_ProtectsRecognizedValuesWithoutGuessing(string source, string translated, bool rejected)
    {
        var response = new TranslateResponse(new[] { translated, "Настройки" }, DateTimeOffset.UnixEpoch, "test");
        var result = MenuTranslationIntegrity.Validate(new[] { source, "Settings" }, response);
        Assert.Equal(rejected ? source : translated, result.TranslatedTexts[0]);
        Assert.Equal("Настройки", result.TranslatedTexts[1]);
        Assert.Equal(rejected, result.DiagnosticMessage.Contains(MenuTranslationIntegrity.Warning, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("2560 * 1440", true)]
    [InlineData("9.67/9.82 GB", true)]
    [InlineData("1/15", true)]
    [InlineData("50%", true)]
    [InlineData("7th Patrol", false)]
    [InlineData("A", false)]
    [InlineData("On", false)]
    public void IsNumericDisplay_LeavesLabelsAndLanguageAmbiguityAlone(string text, bool expected) =>
        Assert.Equal(expected, MenuTranslationIntegrity.IsNumericDisplay(text));
}
