using System.IO;
using System.Xml.Linq;

namespace GameTranslator.Tests;

internal static class ProjectFileReader
{
    public static string[] GetProjectReferences(string projectPath)
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

    public static string[] GetItemIncludes(string projectPath, string itemName)
    {
        var root = RepositoryRoot.Find();
        var fullProjectPath = Path.Combine(root, projectPath);
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)
            ?? throw new InvalidOperationException($"Project path has no directory: {projectPath}");

        return XDocument.Load(fullProjectPath)
            .Descendants(itemName)
            .Select(item => item.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!)))
            .Select(path => Path.GetRelativePath(root, path))
            .Select(NormalizePath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool HasTarget(string projectPath, string targetName)
    {
        var root = RepositoryRoot.Find();
        var fullProjectPath = Path.Combine(root, projectPath);

        return XDocument.Load(fullProjectPath)
            .Descendants("Target")
            .Any(target => string.Equals(
                target.Attribute("Name")?.Value,
                targetName,
                StringComparison.Ordinal));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
