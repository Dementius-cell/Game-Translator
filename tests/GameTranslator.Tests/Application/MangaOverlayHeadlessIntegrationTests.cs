using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class MangaOverlayHeadlessIntegrationTests
{
    private static readonly DateTimeOffset FrameTime = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ShownAt = FrameTime.AddMilliseconds(250);

    public static IEnumerable<object[]> OwnerMangaCases()
    {
        yield return new object[]
        {
            "S9",
            "ja",
            759,
            1080,
            CreateS9JapaneseOwnerBubbles(),
        };
        yield return new object[]
        {
            "S10",
            "zh-CN",
            1280,
            1814,
            CreateS10ChineseOwnerBubbles(),
        };
    }

    [Theory]
    [MemberData(nameof(OwnerMangaCases))]
    public void CreateSnapshot_WithOwnerMangaGeometry_HasNoOverlayGeometryRegressions(
        string scenario,
        string language,
        int width,
        int height,
        IReadOnlyList<OwnerBubble> bubbles)
    {
        var service = new OverlayPositioningService(CreateProductionTextMeasurer());
        var textStyle = OcrZoneTextStyle.Default with
        {
            FontSize = 18,
            LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
        };
        var result = CreateMangaResult(scenario, language, width, height, bubbles);

        var snapshot = service.CreateSnapshot(result, ShownAt, previousSnapshot: null, textStyle);

        Assert.Equal(bubbles.Count, snapshot.TextItems.Count);
        Assert.Equal(bubbles.Count, snapshot.MaskItems.Count);
        Assert.Empty(snapshot.DebugMetricLines);

        for (var index = 0; index < bubbles.Count; index++)
        {
            var bubble = bubbles[index];
            var text = snapshot.TextItems[index];
            var mask = snapshot.MaskItems[index];

            Assert.True(IsWithinPage(text.X, text.Y, text.Width, text.Height, width, height), $"{scenario}:{bubble.Id} translation is out of page: {FormatBounds(text.X, text.Y, text.Width, text.Height)}.");
            Assert.True(IsWithinPage(mask.X, mask.Y, mask.Width, mask.Height, width, height), $"{scenario}:{bubble.Id} mask is out of page: {FormatBounds(mask.X, mask.Y, mask.Width, mask.Height)}.");
            Assert.Equal(bubble.SourceBounds.X, mask.X);
            Assert.Equal(bubble.SourceBounds.Y, mask.Y);
            Assert.Equal(bubble.SourceBounds.Width, mask.Width);
            Assert.Equal(bubble.SourceBounds.Height, mask.Height);
        }

        for (var left = 0; left < snapshot.TextItems.Count; left++)
        {
            for (var right = left + 1; right < snapshot.TextItems.Count; right++)
            {
                Assert.False(
                    Intersects(snapshot.TextItems[left], snapshot.TextItems[right]),
                    $"{scenario}: translations {bubbles[left].Id}/{bubbles[right].Id} overlap.");
            }
        }

        for (var textIndex = 0; textIndex < snapshot.TextItems.Count; textIndex++)
        {
            for (var sourceIndex = 0; sourceIndex < bubbles.Count; sourceIndex++)
            {
                if (textIndex == sourceIndex)
                {
                    continue;
                }

                Assert.False(
                    Intersects(snapshot.TextItems[textIndex], bubbles[sourceIndex].SourceBounds),
                    $"{scenario}: translation {bubbles[textIndex].Id} overlaps source {bubbles[sourceIndex].Id}.");
            }
        }
    }

    private static OcrResult CreateMangaResult(
        string scenario,
        string language,
        int width,
        int height,
        IReadOnlyList<OwnerBubble> bubbles)
    {
        var frame = CreateFrame(width, height);
        var blocks = bubbles
            .Select(bubble => new OcrTextBlock(bubble.TranslationText, bubble.SourceBounds))
            .ToArray();
        var sources = bubbles
            .Select(bubble => new OcrTextBlockSource(
                bubble.SourceBounds,
                new[] { bubble.SourceBounds },
                OcrOrientationMode.Vertical))
            .ToArray();

        return new OcrResult(
            new OcrRequest(
                frame,
                language,
                scenario,
                orientationMode: OcrOrientationMode.Vertical,
                layoutMode: OcrLayoutMode.Comic),
            blocks,
            FrameTime,
            sources);
    }

    private static CapturedFrame CreateFrame(int width, int height)
    {
        var stride = width;

        return new CapturedFrame(
            new CaptureRegion(0, 0, width, height),
            width,
            height,
            stride,
            "Gray8",
            new byte[checked(stride * height)],
            FrameTime);
    }

    private static IOverlayTextMeasurer CreateProductionTextMeasurer()
    {
        var assembly = LoadUiAssembly();
        var type = assembly.GetType(
            "GameTranslator.UI.Services.WpfOverlayTextMeasurer",
            throwOnError: true)
            ?? throw new InvalidOperationException("WPF overlay text measurer type was not found.");

        return Assert.IsAssignableFrom<IOverlayTextMeasurer>(Activator.CreateInstance(type));
    }

    private static Assembly LoadUiAssembly()
    {
        var root = RepositoryRoot.Find();
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var assemblyPath = Path.Combine(
            root,
            "src",
            "GameTranslator.UI",
            "bin",
            configuration,
            "net9.0-windows10.0.19041.0",
            "GameTranslator.UI.dll");

        Assert.True(File.Exists(assemblyPath), $"UI assembly is missing. Build the solution first: {assemblyPath}");
        LoadOutputDependencies(Path.GetDirectoryName(assemblyPath)!);

        var loadedAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
            assembly => string.Equals(assembly.GetName().Name, "GameTranslator.UI", StringComparison.Ordinal));
        if (loadedAssembly is not null)
        {
            return loadedAssembly;
        }

        return Assembly.LoadFrom(assemblyPath);
    }

    private static void LoadOutputDependencies(string outputDirectory)
    {
        foreach (var dependencyPath in Directory.EnumerateFiles(outputDirectory, "*.dll"))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(dependencyPath);
            if (string.Equals(assemblyName, "GameTranslator.UI", StringComparison.Ordinal)
                || AssemblyLoadContext.Default.Assemblies.Any(assembly =>
                    string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal)))
            {
                continue;
            }

            try
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(dependencyPath);
            }
            catch (FileLoadException)
            {
            }
            catch (BadImageFormatException)
            {
            }
        }
    }

    private static bool IsWithinPage(int x, int y, int width, int height, int pageWidth, int pageHeight)
    {
        return x >= 0
            && y >= 0
            && width > 0
            && height > 0
            && checked(x + width) <= pageWidth
            && checked(y + height) <= pageHeight;
    }

    private static bool Intersects(OverlayTextItem left, OverlayTextItem right)
    {
        return HasMeaningfulIntersection(
            left.X,
            left.Y,
            left.Width,
            left.Height,
            right.X,
            right.Y,
            right.Width,
            right.Height);
    }

    private static bool Intersects(OverlayTextItem item, BoundingBox bounds)
    {
        return HasMeaningfulIntersection(
            item.X,
            item.Y,
            item.Width,
            item.Height,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height);
    }

    private static bool HasMeaningfulIntersection(
        int leftX,
        int leftY,
        int leftWidth,
        int leftHeight,
        int rightX,
        int rightY,
        int rightWidth,
        int rightHeight)
    {
        var width = Math.Min(leftX + leftWidth, rightX + rightWidth) - Math.Max(leftX, rightX);
        var height = Math.Min(leftY + leftHeight, rightY + rightHeight) - Math.Max(leftY, rightY);

        return width > 2 && height > 2;
    }

    private static string FormatBounds(int x, int y, int width, int height)
    {
        return $"{x},{y},{width},{height}";
    }

    private static IReadOnlyList<OwnerBubble> CreateS9JapaneseOwnerBubbles()
    {
        return new[]
        {
            new OwnerBubble("J1", new BoundingBox(606, 65, 85, 143), "No way, you are already this popular."),
            new OwnerBubble("J2", new BoundingBox(373, 82, 34, 118), "Yes, probably."),
            new OwnerBubble("J3", new BoundingBox(188, 85, 61, 142), "Does that worry you?"),
            new OwnerBubble("J4", new BoundingBox(555, 430, 100, 102), "I wonder when she found time to start that work."),
            new OwnerBubble("J5", new BoundingBox(452, 503, 28, 101), "Yes, probably."),
            new OwnerBubble("J6", new BoundingBox(117, 537, 28, 48), "No."),
            new OwnerBubble("J7", new BoundingBox(588, 668, 63, 156), "I can barely keep up with my own work right now."),
            new OwnerBubble("J8", new BoundingBox(286, 793, 54, 124), "This is my coffee."),
            new OwnerBubble("J9", new BoundingBox(102, 803, 34, 71), "Wow!"),
            new OwnerBubble("J10", new BoundingBox(421, 937, 60, 73), "The exams are soon."),
        };
    }

    private static IReadOnlyList<OwnerBubble> CreateS10ChineseOwnerBubbles()
    {
        return new[]
        {
            new OwnerBubble("C1", new BoundingBox(999, 143, 87, 246), "Teacher I..."),
            new OwnerBubble("C2", new BoundingBox(412, 545, 80, 97), "Look, Sister Nia."),
            new OwnerBubble("C3", new BoundingBox(250, 717, 52, 177), "Today here..."),
            new OwnerBubble("C4", new BoundingBox(726, 939, 107, 154), "Before you..."),
            new OwnerBubble("C5", new BoundingBox(336, 979, 61, 257), "It is all over?!"),
            new OwnerBubble("C6", new BoundingBox(1011, 993, 66, 204), "And with the teacher."),
        };
    }

    public sealed record OwnerBubble(
        string Id,
        BoundingBox SourceBounds,
        string TranslationText);
}
