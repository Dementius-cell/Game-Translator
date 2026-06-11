using System.IO;
using System.Xml.Linq;

namespace GameTranslator.Tests.Smoke;

public sealed class SprintZeroScaffoldTests
{
    [Fact]
    public void RequiredSolutionAndProjectFiles_Exist()
    {
        var root = RepositoryRoot.Find();

        var requiredFiles = new[]
        {
            "GameTranslator.sln",
            "src/GameTranslator.UI/GameTranslator.UI.csproj",
            "src/GameTranslator.Application/GameTranslator.Application.csproj",
            "src/GameTranslator.Domain/GameTranslator.Domain.csproj",
            "src/GameTranslator.Infrastructure/GameTranslator.Infrastructure.csproj",
            "tests/GameTranslator.Tests/GameTranslator.Tests.csproj",
        };

        foreach (var requiredFile in requiredFiles)
        {
            Assert.True(
                File.Exists(Path.Combine(root, requiredFile)),
                $"Missing required Sprint 0 file: {requiredFile}");
        }
    }

    [Fact]
    public void UiProject_TargetsWpfOnNet9()
    {
        var root = RepositoryRoot.Find();
        var projectPath = Path.Combine(root, "src/GameTranslator.UI/GameTranslator.UI.csproj");
        var project = XDocument.Load(projectPath);

        Assert.Equal("net9.0-windows", GetPropertyValue(project, "TargetFramework"));
        Assert.Equal("true", GetPropertyValue(project, "UseWPF"));
    }

    private static string? GetPropertyValue(XDocument project, string propertyName)
    {
        return project
            .Descendants(propertyName)
            .Select(element => element.Value)
            .SingleOrDefault();
    }
}
