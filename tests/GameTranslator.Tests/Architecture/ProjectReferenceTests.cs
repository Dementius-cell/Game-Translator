using System.Xml.Linq;

namespace GameTranslator.Tests.Architecture;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void ProjectReferences_FollowCleanArchitectureDirection()
    {
        AssertProjectReferences(
            "src/GameTranslator.UI/GameTranslator.UI.csproj",
            "src/GameTranslator.Application/GameTranslator.Application.csproj");

        AssertProjectReferences(
            "src/GameTranslator.Application/GameTranslator.Application.csproj",
            "src/GameTranslator.Domain/GameTranslator.Domain.csproj");

        AssertProjectReferences(
            "src/GameTranslator.Domain/GameTranslator.Domain.csproj");

        AssertProjectReferences(
            "src/GameTranslator.Infrastructure/GameTranslator.Infrastructure.csproj",
            "src/GameTranslator.Domain/GameTranslator.Domain.csproj");

        AssertProjectReferences(
            "tests/GameTranslator.Tests/GameTranslator.Tests.csproj",
            "src/GameTranslator.Application/GameTranslator.Application.csproj",
            "src/GameTranslator.Domain/GameTranslator.Domain.csproj",
            "src/GameTranslator.Infrastructure/GameTranslator.Infrastructure.csproj");
    }

    [Fact]
    public void UiProject_DoesNotReferenceInfrastructureProject()
    {
        var references = GetProjectReferences("src/GameTranslator.UI/GameTranslator.UI.csproj");

        Assert.DoesNotContain(
            "src/GameTranslator.Infrastructure/GameTranslator.Infrastructure.csproj",
            references);
    }

    private static void AssertProjectReferences(string projectPath, params string[] expectedReferences)
    {
        var actualReferences = GetProjectReferences(projectPath);
        var normalizedExpectedReferences = expectedReferences
            .Select(NormalizePath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(normalizedExpectedReferences, actualReferences);
    }

    private static string[] GetProjectReferences(string projectPath)
    {
        var root = RepositoryRoot.Find();
        var fullProjectPath = Path.Combine(root, projectPath);
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException($"Project path has no directory: {projectPath}");

        return XDocument.Load(fullProjectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!)))
            .Select(path => Path.GetRelativePath(root, path))
            .Select(NormalizePath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
