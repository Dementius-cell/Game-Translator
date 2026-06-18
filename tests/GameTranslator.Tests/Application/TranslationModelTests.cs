using GameTranslator.Application.Translation;

namespace GameTranslator.Tests.Application;

public sealed class TranslationModelTests
{
    [Fact]
    public void TranslateRequest_WhenValid_StoresTextAndLanguages()
    {
        var credentials = CreateCredentials();

        var request = new TranslateRequest(
            new[] { "Hello", "World" },
            " en ",
            " ru ",
            credentials);

        Assert.Equal(new[] { "Hello", "World" }, request.Texts);
        Assert.Equal("en", request.SourceLanguage);
        Assert.Equal("ru", request.TargetLanguage);
        Assert.Same(credentials, request.Credentials);
    }

    [Fact]
    public void TranslateRequest_WhenTextsAreEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new TranslateRequest(Array.Empty<string>(), "en", "ru", CreateCredentials()));
        Assert.Throws<ArgumentException>(
            () => new TranslateRequest(new[] { "Hello", " " }, "en", "ru", CreateCredentials()));
    }

    [Fact]
    public void TranslatorCredentials_ToString_RedactsAccessToken()
    {
        var credentials = CreateCredentials("SECRET_ACCESS_TOKEN");

        var text = credentials.ToString();

        Assert.DoesNotContain("SECRET_ACCESS_TOKEN", text, StringComparison.Ordinal);
        Assert.Contains("<redacted>", text, StringComparison.Ordinal);
        Assert.Contains("project-a", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TranslateRequest_ToString_RedactsCredentials()
    {
        var request = new TranslateRequest(
            new[] { "Hello" },
            "en",
            "ru",
            CreateCredentials("SECRET_ACCESS_TOKEN"));

        var text = request.ToString();

        Assert.DoesNotContain("SECRET_ACCESS_TOKEN", text, StringComparison.Ordinal);
        Assert.Contains("TextCount = 1", text, StringComparison.Ordinal);
        Assert.Contains("<redacted>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TranslateResponse_WhenTranslatedTextIsEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new TranslateResponse(new[] { "Translated", string.Empty }, DateTimeOffset.UtcNow));
    }

    private static TranslatorCredentials CreateCredentials(string accessToken = "access-token")
    {
        return new TranslatorCredentials(
            accessToken,
            "project-a",
            endpoint: new Uri("https://translation.test"));
    }
}
