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

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
