using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Calibration;

public sealed class FullScreenMixedContentCalibrationTests
{
    private const int FrameWidth = 1920;
    private const int FrameHeight = 1080;
    private const string FixtureId = "full-screen-mixed-content-frame";
    private static readonly DateTimeOffset FixtureTime = new(2026, 7, 3, 9, 45, 0, TimeSpan.Zero);

    [Fact]
    public void FullScreenMixedContentFixture_WhenGenerated_WritesReadableSingleZoneEvidence()
    {
        var fixture = CreateFixture();
        var candidates = CreateCandidateSweep(fixture);
        var selected = candidates.Single(candidate => candidate.CandidateId == "jpn-vert-auto-nearby-6_5");

        var artifactDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration", fixture.Id);
        Directory.CreateDirectory(artifactDirectory);

        var framePath = Path.Combine(artifactDirectory, "clean-frame.png");
        var evidencePath = Path.Combine(artifactDirectory, "readable-final-overlays.png");
        var cropEvidencePath = Path.Combine(artifactDirectory, "readable-final-crops.png");
        var scorecardPath = Path.Combine(artifactDirectory, "candidate-scorecard.json");
        var manifestPath = Path.Combine(artifactDirectory, "manifest.json");

        SaveCleanFramePng(fixture, framePath);
        SaveReadableEvidencePng(fixture, selected, evidencePath);
        SaveReadableCropEvidencePng(fixture, selected, cropEvidencePath);

        var scorecardJson = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                testOnly = true,
                fixtureId = fixture.Id,
                fullScreenOcrZone = fixture.FullScreenOcrZone,
                selectedCandidateId = selected.CandidateId,
                candidates,
            },
            CreateJsonOptions());
        File.WriteAllText(scorecardPath, scorecardJson);

        var manifestJson = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                testOnly = true,
                fixture.Id,
                fixture.CaseType,
                fixture.FullScreenOcrZone,
                expectedSourceBlocks = fixture.Blocks,
                expectedSemanticGroups = fixture.ExpectedGroups,
                generatedArtifacts = new[]
                {
                    Path.GetFileName(framePath),
                    Path.GetFileName(evidencePath),
                    Path.GetFileName(cropEvidencePath),
                    Path.GetFileName(scorecardPath),
                },
            },
            CreateJsonOptions());
        File.WriteAllText(manifestPath, manifestJson);

        Assert.Equal(new Bounds(0, 0, FrameWidth, FrameHeight), fixture.FullScreenOcrZone);
        Assert.Equal(14, fixture.Blocks.Count);
        Assert.Equal(6, fixture.ExpectedGroups.Count);
        Assert.Equal(6, selected.GroupedTextCount);
        Assert.Equal(6, selected.OverlayBounds.Count);
        Assert.Equal(1d, selected.GroupingScore);
        Assert.True(selected.OverlayBounds.All(bounds => bounds.Width > 0 && bounds.Height > 0));
        Assert.True(File.Exists(framePath), framePath);
        Assert.True(File.Exists(evidencePath), evidencePath);
        Assert.True(new FileInfo(evidencePath).Length > 0);
        Assert.True(File.Exists(cropEvidencePath), cropEvidencePath);
        Assert.True(new FileInfo(cropEvidencePath).Length > 0);
        Assert.Contains("jpn-vert-auto-nearby-6_5", scorecardJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", scorecardJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", scorecardJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", manifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", manifestJson, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CalibrationCandidateResult> CreateCandidateSweep(FullScreenFixture fixture)
    {
        var candidateSettings = new[]
        {
            new CandidateSettings("eng-horizontal-block", "eng", OcrOrientationMode.Horizontal, TranslationGroupingMode.BlockByBlock, 6.5),
            new CandidateSettings("jpn-vert-auto-whole", "jpn_vert", OcrOrientationMode.Auto, TranslationGroupingMode.WholeZone, 6.5),
            new CandidateSettings("jpn-vert-auto-nearby-3_5", "jpn_vert", OcrOrientationMode.Auto, TranslationGroupingMode.NearbyBlocks, 3.5),
            new CandidateSettings("jpn-vert-auto-nearby-6_5", "jpn_vert", OcrOrientationMode.Auto, TranslationGroupingMode.NearbyBlocks, 6.5),
            new CandidateSettings("jpn-vert-horizontal-nearby-6_5", "jpn_vert", OcrOrientationMode.Horizontal, TranslationGroupingMode.NearbyBlocks, 6.5),
            new CandidateSettings("jpn-vert-vertical-nearby-6_5", "jpn_vert", OcrOrientationMode.Vertical, TranslationGroupingMode.NearbyBlocks, 6.5),
            new CandidateSettings("tha-auto-nearby-6_5", "tha", OcrOrientationMode.Auto, TranslationGroupingMode.NearbyBlocks, 6.5),
            new CandidateSettings("kor-auto-nearby-6_5", "kor", OcrOrientationMode.Auto, TranslationGroupingMode.NearbyBlocks, 6.5),
            new CandidateSettings("chi-sim-vert-auto-nearby-6_5", "chi_sim_vert", OcrOrientationMode.Auto, TranslationGroupingMode.NearbyBlocks, 6.5),
            new CandidateSettings("jpn-vert-auto-nearby-10_0", "jpn_vert", OcrOrientationMode.Auto, TranslationGroupingMode.NearbyBlocks, 10.0),
        };

        return candidateSettings
            .Select(settings => CreateCandidateResult(fixture, settings))
            .ToArray();
    }

    private static CalibrationCandidateResult CreateCandidateResult(
        FullScreenFixture fixture,
        CandidateSettings settings)
    {
        var sourceResult = CreateSourceResult(fixture, settings);
        var zone = new OcrZone
        {
            Id = "full-screen-calibration-zone",
            Name = "Full-screen mixed content calibration zone",
            TranslationGroupingMode = settings.GroupingMode,
            TextGrouping = new OcrZoneTextGroupingSettings
            {
                MergeDistancePercent = settings.MergeDistancePercent,
            },
        };
        var groupedResult = TranslationTextGroupingService.CreateTranslationSourceResult(sourceResult, zone);
        var translatedResult = CreateTranslatedResult(groupedResult);
        var snapshot = new OverlayPositioningService().CreateSnapshot(
            translatedResult,
            FixtureTime.AddSeconds(1),
            previousSnapshot: null,
            CreateTextStyle());
        var groupingScore = ScoreGrouping(fixture, groupedResult);
        var orientationScore = ScoreOrientation(fixture, groupedResult);
        var overlayBounds = snapshot.TextItems
            .Select(item => new Bounds(item.X, item.Y, item.Width, item.Height))
            .ToArray();

        return new CalibrationCandidateResult(
            settings.CandidateId,
            settings.OcrLanguage,
            settings.OrientationMode.ToString(),
            settings.GroupingMode.ToString(),
            settings.MergeDistancePercent,
            fixture.Blocks.Count,
            groupedResult.TextBlocks.Count,
            groupingScore,
            orientationScore,
            Math.Round(groupingScore * 0.75d + orientationScore * 0.25d, 4),
            groupedResult.TextBlocks.Select(block => block.Text).ToArray(),
            overlayBounds);
    }

    private static OcrResult CreateSourceResult(FullScreenFixture fixture, CandidateSettings settings)
    {
        var frame = CreateEmptyFrame(fixture.FullScreenOcrZone);
        var request = new OcrRequest(
            frame,
            settings.OcrLanguage,
            "full-screen-calibration-zone",
            OcrPreprocessingSettings.Default,
            OcrSettings.TesseractEngineId,
            settings.OrientationMode);
        var blocks = fixture.Blocks
            .Select(block => new OcrTextBlock(block.Text, ToBoundingBox(block.Bounds)))
            .ToArray();
        var sources = fixture.Blocks
            .Select(block =>
            {
                var bounds = ToBoundingBox(block.Bounds);
                return new OcrTextBlockSource(
                    bounds,
                    new[] { bounds },
                    ResolveSourceOrientation(settings.OrientationMode, block.Orientation));
            })
            .ToArray();

        return new OcrResult(request, blocks, FixtureTime, sources);
    }

    private static OcrResult CreateTranslatedResult(OcrResult groupedResult)
    {
        var translatedBlocks = groupedResult.TextBlocks
            .Select(block => new OcrTextBlock(CreateTranslation(block.Text), block.Bounds))
            .ToArray();

        return new OcrResult(
            groupedResult.Request,
            translatedBlocks,
            groupedResult.RecognizedAt,
            groupedResult.TextBlockSources);
    }

    private static CapturedFrame CreateEmptyFrame(Bounds bounds)
    {
        var stride = bounds.Width * 4;
        return new CapturedFrame(
            new CaptureRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            bounds.Width,
            bounds.Height,
            stride,
            "Bgra32",
            new byte[stride * bounds.Height],
            FixtureTime);
    }

    private static double ScoreGrouping(FullScreenFixture fixture, OcrResult groupedResult)
    {
        if (groupedResult.TextBlocks.Count == 0)
        {
            return 0d;
        }

        var matchedGroups = fixture.ExpectedGroups.Count(expected =>
            groupedResult.TextBlocks.Any(actual => TextContainsAllFragments(actual.Text, expected.RequiredFragments)));
        var expectedCount = fixture.ExpectedGroups.Count;
        var countPenalty = Math.Abs(groupedResult.TextBlocks.Count - expectedCount) / (double)expectedCount;

        return Math.Max(0d, matchedGroups / (double)expectedCount - countPenalty);
    }

    private static double ScoreOrientation(FullScreenFixture fixture, OcrResult groupedResult)
    {
        if (groupedResult.TextBlockSources.Count == 0)
        {
            return 0d;
        }

        var matchedOrientations = 0;
        foreach (var expected in fixture.ExpectedGroups)
        {
            var index = groupedResult.TextBlocks.ToList().FindIndex(actual =>
                TextContainsAllFragments(actual.Text, expected.RequiredFragments));
            if (index < 0)
            {
                continue;
            }

            var actualOrientation = groupedResult.TextBlockSources[index].OrientationMode;
            if (actualOrientation == expected.Orientation)
            {
                matchedOrientations++;
            }
        }

        return matchedOrientations / (double)fixture.ExpectedGroups.Count;
    }

    private static bool TextContainsAllFragments(string text, IReadOnlyList<string> fragments)
    {
        var normalized = OcrTextNormalizer.NormalizeForComparison(text);
        return fragments.All(fragment =>
            normalized.Contains(OcrTextNormalizer.NormalizeForComparison(fragment), StringComparison.Ordinal));
    }

    private static void SaveCleanFramePng(FullScreenFixture fixture, string path)
    {
        RenderOnSta(() =>
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                DrawFixtureFrame(context, fixture);
            }

            SaveVisual(path, visual, FrameWidth, FrameHeight);
        });
    }

    private static void SaveReadableEvidencePng(
        FullScreenFixture fixture,
        CalibrationCandidateResult selected,
        string path)
    {
        var selectedResult = CreateSelectedPipelineResult(fixture, selected);
        var snapshot = selectedResult.Snapshot;

        RenderOnSta(() =>
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                DrawFixtureFrame(context, fixture);
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(96, 15, 23, 42)), null, new Rect(0, 0, FrameWidth, FrameHeight));
                DrawBounds(context, fixture.FullScreenOcrZone, null, CreatePen(Color.FromRgb(37, 99, 235), 3, dashed: true));

                foreach (var block in fixture.Blocks)
                {
                    DrawBounds(context, block.Bounds, null, CreatePen(Color.FromRgb(250, 204, 21), 2));
                }

                foreach (var group in fixture.ExpectedGroups)
                {
                    DrawBounds(context, group.Bounds, null, CreatePen(Color.FromRgb(34, 197, 94), 3));
                }

                for (var index = 0; index < snapshot.TextItems.Count; index++)
                {
                    var item = snapshot.TextItems[index];
                    var bounds = new Bounds(item.X, item.Y, item.Width, item.Height);
                    DrawBounds(
                        context,
                        bounds,
                        new SolidColorBrush(Color.FromArgb(218, 24, 94, 165)),
                        CreatePen(Color.FromRgb(219, 234, 254), 3));
                    DrawOutlinedText(context, item.Text, bounds);
                    DrawLabel(context, (index + 1).ToString(CultureInfo.InvariantCulture), bounds.X, bounds.Y - 24, 15);
                }

                DrawLegend(context, selected);
            }

            SaveVisual(path, visual, FrameWidth, FrameHeight);
        });
    }

    private static void SaveReadableCropEvidencePng(
        FullScreenFixture fixture,
        CalibrationCandidateResult selected,
        string path)
    {
        const int width = 1920;
        const int margin = 56;
        const int gap = 36;
        const int headerHeight = 94;
        const int panelWidth = (width - margin * 2 - gap) / 2;
        const int panelHeight = 500;
        const int cropInset = 18;
        const int cropTopInset = 74;

        var selectedResult = CreateSelectedPipelineResult(fixture, selected);
        var height = headerHeight + 3 * panelHeight + 2 * gap + margin;

        RenderOnSta(() =>
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 42)), null, new Rect(0, 0, width, height));
                DrawText(
                    context,
                    "Readable final groups from one full-screen OCR capture",
                    margin,
                    30,
                    28,
                    Brushes.White,
                    semiBold: true,
                    maxWidth: 1200);
                DrawText(
                    context,
                    $"Selected: {selected.CandidateId} | OCR {selected.OcrLanguage} | {selected.GroupingMode} {selected.MergeDistancePercent:0.0}% | {selected.GroupedTextCount} groups from {selected.SourceBlockCount} blocks",
                    margin,
                    66,
                    16,
                    Brushes.LightGray,
                    maxWidth: 1500);

                for (var index = 0; index < fixture.ExpectedGroups.Count; index++)
                {
                    var expected = fixture.ExpectedGroups[index];
                    var matchIndex = FindMatchingGroupIndex(selectedResult.GroupedResult, expected);
                    var overlayItem = selectedResult.Snapshot.TextItems[matchIndex];
                    var overlayBounds = new Bounds(overlayItem.X, overlayItem.Y, overlayItem.Width, overlayItem.Height);
                    var cropBounds = ExpandAndClamp(Union(expected.Bounds, overlayBounds), 92, 76);
                    var column = index % 2;
                    var row = index / 2;
                    var panelX = margin + column * (panelWidth + gap);
                    var panelY = headerHeight + row * (panelHeight + gap);

                    DrawCropPanel(
                        context,
                        fixture,
                        selectedResult,
                        expected,
                        overlayBounds,
                        overlayItem.Text,
                        cropBounds,
                        index + 1,
                        panelX,
                        panelY,
                        panelWidth,
                        panelHeight,
                        cropInset,
                        cropTopInset);
                }
            }

            SaveVisual(path, visual, width, height);
        });
    }

    private static SelectedPipelineResult CreateSelectedPipelineResult(
        FullScreenFixture fixture,
        CalibrationCandidateResult selected)
    {
        var settings = new CandidateSettings(
            selected.CandidateId,
            selected.OcrLanguage,
            Enum.Parse<OcrOrientationMode>(selected.OrientationMode),
            Enum.Parse<TranslationGroupingMode>(selected.GroupingMode),
            selected.MergeDistancePercent);
        var sourceResult = CreateSourceResult(fixture, settings);
        var groupedResult = TranslationTextGroupingService.CreateTranslationSourceResult(
            sourceResult,
            new OcrZone
            {
                Id = "full-screen-calibration-zone",
                Name = "Full-screen mixed content calibration zone",
                TranslationGroupingMode = settings.GroupingMode,
                TextGrouping = new OcrZoneTextGroupingSettings
                {
                    MergeDistancePercent = settings.MergeDistancePercent,
                },
            });
        var translated = CreateTranslatedResult(groupedResult);
        var snapshot = new OverlayPositioningService().CreateSnapshot(
            translated,
            FixtureTime.AddSeconds(1),
            previousSnapshot: null,
            CreateTextStyle());

        return new SelectedPipelineResult(groupedResult, snapshot);
    }

    private static void DrawCropPanel(
        DrawingContext context,
        FullScreenFixture fixture,
        SelectedPipelineResult selectedResult,
        ExpectedGroup expected,
        Bounds overlayBounds,
        string overlayText,
        Bounds cropBounds,
        int ordinal,
        int panelX,
        int panelY,
        int panelWidth,
        int panelHeight,
        int cropInset,
        int cropTopInset)
    {
        var panel = new Rect(panelX, panelY, panelWidth, panelHeight);
        var viewport = new Rect(
            panelX + cropInset,
            panelY + cropTopInset,
            panelWidth - cropInset * 2,
            panelHeight - cropTopInset - cropInset);
        var scale = Math.Min(viewport.Width / cropBounds.Width, viewport.Height / cropBounds.Height);
        var scaledWidth = cropBounds.Width * scale;
        var scaledHeight = cropBounds.Height * scale;
        var originX = viewport.X + (viewport.Width - scaledWidth) / 2d;
        var originY = viewport.Y + (viewport.Height - scaledHeight) / 2d;

        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 41, 59)), CreatePen(Color.FromRgb(71, 85, 105), 2), panel);
        DrawText(
            context,
            $"{ordinal}. {expected.Id} -> {overlayText}",
            panelX + 20,
            panelY + 16,
            18,
            Brushes.White,
            semiBold: true,
            maxWidth: panelWidth - 40);
        DrawText(
            context,
            $"{expected.Language} | expected {expected.Orientation} | fragments: {string.Join(" + ", expected.RequiredFragments)}",
            panelX + 20,
            panelY + 42,
            13,
            Brushes.LightGray,
            maxWidth: panelWidth - 40);
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(2, 6, 23)), CreatePen(Color.FromRgb(100, 116, 139), 1), viewport);

        context.PushClip(new RectangleGeometry(viewport));
        context.PushTransform(new MatrixTransform(scale, 0, 0, scale, originX - cropBounds.X * scale, originY - cropBounds.Y * scale));
        DrawFixtureFrame(context, fixture, includeLabels: false);

        foreach (var block in fixture.Blocks)
        {
            DrawBounds(context, block.Bounds, null, CreatePen(Color.FromRgb(250, 204, 21), 2));
        }

        DrawBounds(context, expected.Bounds, null, CreatePen(Color.FromRgb(34, 197, 94), 4));
        DrawBounds(
            context,
            overlayBounds,
            new SolidColorBrush(Color.FromArgb(224, 24, 94, 165)),
            CreatePen(Color.FromRgb(219, 234, 254), 3));
        DrawOutlinedText(context, overlayText, overlayBounds);
        context.Pop();
        context.Pop();

        DrawText(
            context,
            $"grouped source: {selectedResult.GroupedResult.TextBlocks[FindMatchingGroupIndex(selectedResult.GroupedResult, expected)].Text}",
            panelX + 20,
            panelY + panelHeight - 24,
            13,
            Brushes.LightGray,
            maxWidth: panelWidth - 40);
    }

    private static int FindMatchingGroupIndex(OcrResult groupedResult, ExpectedGroup expected)
    {
        for (var index = 0; index < groupedResult.TextBlocks.Count; index++)
        {
            if (TextContainsAllFragments(groupedResult.TextBlocks[index].Text, expected.RequiredFragments))
            {
                return index;
            }
        }

        throw new InvalidOperationException($"Expected group '{expected.Id}' was not present in the selected grouped OCR result.");
    }

    private static Bounds ExpandAndClamp(Bounds bounds, int horizontalPadding, int verticalPadding)
    {
        var x = Math.Max(0, bounds.X - horizontalPadding);
        var y = Math.Max(0, bounds.Y - verticalPadding);
        var right = Math.Min(FrameWidth, bounds.X + bounds.Width + horizontalPadding);
        var bottom = Math.Min(FrameHeight, bounds.Y + bounds.Height + verticalPadding);

        return new Bounds(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    private static Bounds Union(Bounds first, Bounds second)
    {
        var x = Math.Min(first.X, second.X);
        var y = Math.Min(first.Y, second.Y);
        var right = Math.Max(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Max(first.Y + first.Height, second.Y + second.Height);

        return new Bounds(x, y, right - x, bottom - y);
    }

    private static void DrawFixtureFrame(DrawingContext context, FullScreenFixture fixture, bool includeLabels = true)
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 24, 39)), null, new Rect(0, 0, FrameWidth, FrameHeight));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(31, 41, 55)), null, new Rect(60, 60, 1800, 960));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 18, 32)), CreatePen(Color.FromRgb(75, 85, 99), 2), new Rect(96, 92, 520, 220));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 78, 99)), CreatePen(Color.FromRgb(34, 211, 238), 2), new Rect(730, 110, 430, 260));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(41, 37, 36)), CreatePen(Color.FromRgb(245, 158, 11), 2), new Rect(1280, 96, 420, 240));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(55, 65, 81)), CreatePen(Color.FromRgb(156, 163, 175), 2), new Rect(96, 760, 640, 210));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 24, 27)), CreatePen(Color.FromRgb(168, 85, 247), 2), new Rect(1000, 708, 780, 250));
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 47, 73)), CreatePen(Color.FromRgb(14, 165, 233), 2), new Rect(1380, 442, 380, 178));

        foreach (var block in fixture.Blocks)
        {
            DrawSourceText(context, block);
        }

        if (includeLabels)
        {
            DrawLabel(context, "Clean full-screen mixed-content fixture", 80, 34, 22);
            DrawLabel(context, "Single OCR capture zone is the full 1920x1080 frame.", 80, 1016, 16);
        }
    }

    private static void DrawSourceText(DrawingContext context, SourceBlock block)
    {
        if (block.Orientation == OcrOrientationMode.Vertical)
        {
            var glyphs = block.TextElements;
            for (var index = 0; index < glyphs.Count; index++)
            {
                var y = block.Bounds.Y + index * Math.Max(20, block.Bounds.Height / Math.Max(1, glyphs.Count));
                DrawText(
                    context,
                    glyphs[index],
                    block.Bounds.X + 2,
                    y,
                    block.FontSize,
                    Brushes.White,
                    semiBold: true,
                    fontFamily: block.FontFamily,
                    maxWidth: Math.Max(28, block.Bounds.Width + 12));
            }

            return;
        }

        DrawText(
            context,
            block.Text,
            block.Bounds.X,
            block.Bounds.Y,
            block.FontSize,
            Brushes.White,
            semiBold: true,
            fontFamily: block.FontFamily,
            maxWidth: block.Bounds.Width + 8);
    }

    private static void DrawLegend(DrawingContext context, CalibrationCandidateResult selected)
    {
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(220, 248, 250, 252)), CreatePen(Color.FromRgb(148, 163, 184), 1), new Rect(48, 940, 1824, 96));
        DrawText(
            context,
            $"Selected calibration candidate: {selected.CandidateId} | OCR {selected.OcrLanguage} | orientation {selected.OrientationMode} | grouping {selected.GroupingMode} {selected.MergeDistancePercent:0.0}% | grouped {selected.GroupedTextCount} of {selected.SourceBlockCount} OCR blocks",
            72,
            960,
            16,
            Brushes.Black,
            semiBold: true,
            maxWidth: 1700);
        DrawText(
            context,
            "Blue dashed=full-screen OCR zone, yellow=approved OCR blocks, green=expected semantic groups, dark blue=final translated overlays.",
            72,
            994,
            14,
            Brushes.DimGray,
            maxWidth: 1700);
    }

    private static void DrawOutlinedText(DrawingContext context, string text, Bounds bounds)
    {
        var clip = new Rect(bounds.X + 6, bounds.Y + 5, Math.Max(12, bounds.Width - 12), Math.Max(12, bounds.Height - 10));
        context.PushClip(new RectangleGeometry(clip));
        foreach (var (dx, dy) in OutlineOffsets)
        {
            DrawText(context, text, clip.X + dx, clip.Y + dy, 16, Brushes.Black, semiBold: true, maxWidth: clip.Width);
        }

        DrawText(context, text, clip.X, clip.Y, 16, Brushes.White, semiBold: true, maxWidth: clip.Width);
        context.Pop();
    }

    private static void DrawBounds(DrawingContext context, Bounds bounds, Brush? fill, Pen? outline)
    {
        context.DrawRectangle(fill, outline, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
    }

    private static void DrawLabel(DrawingContext context, string text, double x, double y, double fontSize)
    {
        DrawText(context, text, x, y, fontSize, Brushes.White, semiBold: true, maxWidth: 760);
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        double x,
        double y,
        double fontSize,
        Brush brush,
        bool semiBold = false,
        string fontFamily = "Segoe UI",
        double maxWidth = 240)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(fontFamily),
                FontStyles.Normal,
                semiBold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStretches.Normal),
            fontSize,
            brush,
            1.0)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = Math.Max(fontSize * 4, fontSize + 4),
            Trimming = TextTrimming.CharacterEllipsis,
        };

        context.DrawText(formatted, new Point(x, y));
    }

    private static Pen CreatePen(Color color, double thickness, bool dashed = false)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        if (dashed)
        {
            pen.DashStyle = DashStyles.Dash;
        }

        pen.Freeze();
        return pen;
    }

    private static void SaveVisual(string path, DrawingVisual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void RenderOnSta(Action render)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                render();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("Failed to render full-screen mixed-content calibration evidence.", failure);
        }
    }

    private static FullScreenFixture CreateFixture()
    {
        return new FullScreenFixture(
            FixtureId,
            "full_screen_mixed_content_single_zone",
            new Bounds(0, 0, FrameWidth, FrameHeight),
            new[]
            {
                Block("ja-save-0", "\u30bb", new[] { "\u30bb" }, "ja-JP", OcrOrientationMode.Vertical, new Bounds(146, 130, 28, 32), 22, "Yu Gothic UI"),
                Block("ja-save-1", "\u30fc", new[] { "\u30fc" }, "ja-JP", OcrOrientationMode.Vertical, new Bounds(146, 166, 28, 32), 22, "Yu Gothic UI"),
                Block("ja-save-2", "\u30d6", new[] { "\u30d6" }, "ja-JP", OcrOrientationMode.Vertical, new Bounds(146, 202, 28, 32), 22, "Yu Gothic UI"),
                Block("ja-save-3", "\u3057", new[] { "\u3057" }, "ja-JP", OcrOrientationMode.Vertical, new Bounds(146, 238, 28, 32), 22, "Yu Gothic UI"),
                Block("en-title-0", "OPTIONS", new[] { "OPTIONS" }, "en", OcrOrientationMode.Horizontal, new Bounds(316, 132, 154, 34), 24, "Segoe UI"),
                Block("en-title-1", "MENU", new[] { "MENU" }, "en", OcrOrientationMode.Horizontal, new Bounds(316, 178, 118, 34), 24, "Segoe UI"),
                Block("tha-dialog-0", "\u0e1e\u0e23\u0e49\u0e2d\u0e21", new[] { "\u0e1e\u0e23\u0e49\u0e2d\u0e21" }, "tha", OcrOrientationMode.Horizontal, new Bounds(776, 180, 178, 34), 25, "Leelawadee UI"),
                Block("tha-dialog-1", "\u0e41\u0e25\u0e49\u0e27", new[] { "\u0e41\u0e25\u0e49\u0e27" }, "tha", OcrOrientationMode.Horizontal, new Bounds(776, 228, 134, 34), 25, "Leelawadee UI"),
                Block("tha-dialog-2", "\u0e44\u0e1b\u0e01\u0e31\u0e19", new[] { "\u0e44\u0e1b\u0e01\u0e31\u0e19" }, "tha", OcrOrientationMode.Horizontal, new Bounds(776, 276, 150, 34), 25, "Leelawadee UI"),
                Block("zh-vertical-0", "\u4f60", new[] { "\u4f60" }, "zh-CN", OcrOrientationMode.Vertical, new Bounds(1334, 126, 30, 36), 25, "Microsoft YaHei UI"),
                Block("zh-vertical-1", "\u597d", new[] { "\u597d" }, "zh-CN", OcrOrientationMode.Vertical, new Bounds(1334, 168, 30, 36), 25, "Microsoft YaHei UI"),
                Block("ko-dialog-0", "\uc800\uc7a5 \uc644\ub8cc", new[] { "\uc800\uc7a5", "\uc644\ub8cc" }, "kor", OcrOrientationMode.Horizontal, new Bounds(1408, 504, 240, 40), 27, "Malgun Gothic"),
                Block("en-book-0", "THE OLD CITY", new[] { "THE OLD CITY" }, "en", OcrOrientationMode.Horizontal, new Bounds(154, 820, 260, 34), 24, "Segoe UI"),
                Block("en-book-1", "WAS QUIET", new[] { "WAS QUIET" }, "en", OcrOrientationMode.Horizontal, new Bounds(154, 866, 214, 34), 24, "Segoe UI"),
            },
            new[]
            {
                GroupExpectation("vertical-japanese-save", "ja-JP", OcrOrientationMode.Vertical, new Bounds(136, 122, 58, 158), new[] { "\u30bb", "\u30fc", "\u30d6", "\u3057" }),
                GroupExpectation("english-options-menu", "en", OcrOrientationMode.Horizontal, new Bounds(300, 124, 188, 100), new[] { "OPTIONS", "MENU" }),
                GroupExpectation("thai-three-line-dialogue", "tha", OcrOrientationMode.Horizontal, new Bounds(760, 172, 210, 146), new[] { "\u0e1e\u0e23\u0e49\u0e2d\u0e21", "\u0e41\u0e25\u0e49\u0e27", "\u0e44\u0e1b\u0e01\u0e31\u0e19" }),
                GroupExpectation("vertical-chinese-greeting", "zh-CN", OcrOrientationMode.Vertical, new Bounds(1324, 118, 58, 100), new[] { "\u4f60", "\u597d" }),
                GroupExpectation("korean-save-complete", "kor", OcrOrientationMode.Horizontal, new Bounds(1396, 492, 270, 64), new[] { "\uc800\uc7a5", "\uc644\ub8cc" }),
                GroupExpectation("english-book-two-line", "en", OcrOrientationMode.Horizontal, new Bounds(140, 812, 292, 100), new[] { "THE OLD CITY", "WAS QUIET" }),
            });
    }

    private static SourceBlock Block(
        string id,
        string text,
        IReadOnlyList<string> textElements,
        string language,
        OcrOrientationMode orientation,
        Bounds bounds,
        double fontSize,
        string fontFamily)
    {
        return new SourceBlock(id, text, textElements, language, orientation, bounds, fontSize, fontFamily);
    }

    private static ExpectedGroup GroupExpectation(
        string id,
        string language,
        OcrOrientationMode orientation,
        Bounds bounds,
        IReadOnlyList<string> requiredFragments)
    {
        return new ExpectedGroup(id, language, orientation, bounds, requiredFragments);
    }

    private static OcrOrientationMode ResolveSourceOrientation(
        OcrOrientationMode candidateOrientation,
        OcrOrientationMode blockOrientation)
    {
        return candidateOrientation switch
        {
            OcrOrientationMode.Horizontal => OcrOrientationMode.Horizontal,
            OcrOrientationMode.Vertical => OcrOrientationMode.Vertical,
            _ => blockOrientation,
        };
    }

    private static OcrZoneTextStyle CreateTextStyle()
    {
        return new OcrZoneTextStyle
        {
            FontFamily = "Segoe UI",
            FontSize = 20,
            IsBold = true,
            LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter,
        };
    }

    private static string CreateTranslation(string sourceText)
    {
        if (TextContainsAllFragments(sourceText, new[] { "\u30bb", "\u30fc", "\u30d6", "\u3057" }))
        {
            return "Save";
        }

        if (TextContainsAllFragments(sourceText, new[] { "OPTIONS", "MENU" }))
        {
            return "Settings";
        }

        if (TextContainsAllFragments(sourceText, new[] { "\u0e1e\u0e23\u0e49\u0e2d\u0e21", "\u0e41\u0e25\u0e49\u0e27", "\u0e44\u0e1b\u0e01\u0e31\u0e19" }))
        {
            return "Ready, let's go";
        }

        if (TextContainsAllFragments(sourceText, new[] { "\u4f60", "\u597d" }))
        {
            return "Hello";
        }

        if (TextContainsAllFragments(sourceText, new[] { "\uc800\uc7a5", "\uc644\ub8cc" }))
        {
            return "Saved";
        }

        if (TextContainsAllFragments(sourceText, new[] { "THE OLD CITY", "WAS QUIET" }))
        {
            return "The old city was quiet";
        }

        return sourceText;
    }

    private static BoundingBox ToBoundingBox(Bounds bounds)
    {
        return new BoundingBox(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        };
    }

    private static readonly (double X, double Y)[] OutlineOffsets =
    {
        (-1, -1),
        (0, -1),
        (1, -1),
        (-1, 0),
        (1, 0),
        (-1, 1),
        (0, 1),
        (1, 1),
    };

    private sealed record FullScreenFixture(
        string Id,
        string CaseType,
        Bounds FullScreenOcrZone,
        IReadOnlyList<SourceBlock> Blocks,
        IReadOnlyList<ExpectedGroup> ExpectedGroups);

    private sealed record SourceBlock(
        string Id,
        string Text,
        IReadOnlyList<string> TextElements,
        string Language,
        OcrOrientationMode Orientation,
        Bounds Bounds,
        double FontSize,
        string FontFamily);

    private sealed record ExpectedGroup(
        string Id,
        string Language,
        OcrOrientationMode Orientation,
        Bounds Bounds,
        IReadOnlyList<string> RequiredFragments);

    private sealed record CandidateSettings(
        string CandidateId,
        string OcrLanguage,
        OcrOrientationMode OrientationMode,
        TranslationGroupingMode GroupingMode,
        double MergeDistancePercent);

    private sealed record CalibrationCandidateResult(
        string CandidateId,
        string OcrLanguage,
        string OrientationMode,
        string GroupingMode,
        double MergeDistancePercent,
        int SourceBlockCount,
        int GroupedTextCount,
        double GroupingScore,
        double OrientationScore,
        double TotalScore,
        IReadOnlyList<string> GroupedTexts,
        IReadOnlyList<Bounds> OverlayBounds);

    private sealed record SelectedPipelineResult(
        OcrResult GroupedResult,
        OverlaySnapshot Snapshot);

    private readonly record struct Bounds(int X, int Y, int Width, int Height);
}
