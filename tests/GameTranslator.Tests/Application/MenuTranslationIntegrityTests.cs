using GameTranslator.Application.Content;
using GameTranslator.Application.Translation;

namespace GameTranslator.Tests.Application;

public sealed class MenuTranslationIntegrityTests
{
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
