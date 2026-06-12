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
            "src/GameTranslator.Application/GameTranslator.Application.csproj",
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
        var references = ProjectFileReader.GetProjectReferences("src/GameTranslator.UI/GameTranslator.UI.csproj");

        Assert.DoesNotContain(
            "src/GameTranslator.Infrastructure/GameTranslator.Infrastructure.csproj",
            references);
    }

    [Fact]
    public void UiProject_CopiesInfrastructureCompositionModuleWithoutProjectReference()
    {
        var moduleIncludes = ProjectFileReader.GetItemIncludes(
            "src/GameTranslator.UI/GameTranslator.UI.csproj",
            "InfrastructureCompositionModule");

        Assert.Contains(
            "src/GameTranslator.Infrastructure/bin/$(Configuration)/net9.0-windows10.0.19041.0/GameTranslator.Infrastructure.dll",
            moduleIncludes);
        Assert.True(ProjectFileReader.HasTarget(
            "src/GameTranslator.UI/GameTranslator.UI.csproj",
            "BuildInfrastructureCompositionModule"));
        Assert.True(ProjectFileReader.HasTarget(
            "src/GameTranslator.UI/GameTranslator.UI.csproj",
            "CopyInfrastructureCompositionModule"));
    }

    private static void AssertProjectReferences(string projectPath, params string[] expectedReferences)
    {
        var actualReferences = ProjectFileReader.GetProjectReferences(projectPath);
        var normalizedExpectedReferences = expectedReferences
            .Select(NormalizePath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(normalizedExpectedReferences, actualReferences);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
