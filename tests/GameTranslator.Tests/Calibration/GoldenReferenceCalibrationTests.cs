using System.IO;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Pipeline;
using GameTranslator.Tests;
using GameTranslator.Domain.Profiles;
using GameTranslator.Infrastructure.Ocr;

namespace GameTranslator.Tests.Calibration;

public sealed class GoldenReferenceCalibrationTests
{
    private static readonly DateTimeOffset FixtureTime = new(2026, 6, 30, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FixtureManifest_WhenBuilt_StoresGroundTruthTextSemanticGeometryAndForbiddenRegions()
    {
        var fixtures = CreateGoldenFixtures();

        Assert.Equal(
            new[] { "manga_vertical_cjk", "manga_vertical_japanese", "book_page_horizontal", "plain_ui_horizontal" },
            fixtures.Select(fixture => fixture.CaseType));
        Assert.Equal(fixtures.Count, fixtures.Select(fixture => fixture.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(fixtures, fixture =>
        {
            Assert.NotEmpty(fixture.OriginalText);
            Assert.NotEmpty(fixture.ApprovedReadingOrder);
            Assert.NotEmpty(fixture.SemanticGroups);
            Assert.NotEqual(default, fixture.BubbleBounds);
            Assert.NotEqual(default, fixture.ApprovedOverlayBounds);
            Assert.NotEmpty(fixture.RawSourceBounds);
            Assert.NotEmpty(fixture.ForbiddenRegions);
        });

        var vertical = fixtures.Single(fixture => fixture.Id == "vertical-cjk-basic-bubble");
        Assert.Equal(new[] { "\u4f60", "\u597d" }, vertical.ApprovedReadingOrder);
        var group = Assert.Single(vertical.SemanticGroups);
        Assert.Equal("\u4f60 \u597d", group.ApprovedSourceText);
        Assert.Equal(new[] { 0, 1 }, group.RawSourceIndexes);
        Assert.Equal(new[] { 0, 1 }, group.MaskSourceIndexes);
        Assert.Equal(2, vertical.RawSourceBounds.Count);

        var japanese = fixtures.Single(fixture => fixture.Id == "vertical-japanese-save-prompt");
        Assert.Equal("ja-JP", japanese.SourceLanguage);
        Assert.Equal(OcrOrientationMode.Vertical, japanese.SourceOrientation);
        Assert.Equal(
            new[] { "\u30bb", "\u30fc", "\u30d6", "\u3057", "\u307e", "\u3059", "\u304b" },
            japanese.ApprovedReadingOrder);
        var japaneseGroup = Assert.Single(japanese.SemanticGroups);
        Assert.Equal("\u30bb \u30fc \u30d6 \u3057 \u307e \u3059 \u304b", japaneseGroup.ApprovedSourceText);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, japaneseGroup.RawSourceIndexes);
        Assert.Equal(7, japanese.RawSourceBounds.Count);
    }

    [Fact]
    public void OcrFidelity_WhenGeneratedTextNeedsPresetRetry_SelectsFirstPresetThatMatchesGroundTruth()
    {
        var rendered = RenderTextFixture("HELLO START", width: 180, height: 64);
        var candidates = new[]
        {
            new OcrPresetCandidate(
                "default",
                OcrPreprocessingSettings.Default,
                _ => CreateOcrResult(rendered.Frame, "HE L0 START", new BoundingBox(8, 20, 118, 16), OcrOrientationMode.Horizontal)),
            new OcrPresetCandidate(
                "threshold-scale",
                new OcrPreprocessingSettings
                {
                    IsEnabled = true,
                    ThresholdingEnabled = true,
                    Threshold = 160,
                    Scale = 1.5,
                },
                _ => CreateOcrResult(rendered.Frame, "HELLO START", rendered.TextBounds, OcrOrientationMode.Horizontal)),
        };

        var result = SelectSuccessfulOcrPreset(
            rendered.ExpectedText,
            rendered.Frame,
            candidates,
            maxCharacterErrorRate: 0.05);

        Assert.NotNull(result);
        Assert.Equal("threshold-scale", result!.Preset.Name);
        Assert.Equal("HELLO START", Normalize(result.Result.Text));
        Assert.True(result.CharacterErrorRate <= 0.05);
        Assert.Equal(rendered.TextBounds, Assert.Single(result.Result.TextBlocks).Bounds);
    }

    [Fact]
    public void TranslationRequest_WhenRecognizedBlocksAreFragmented_UsesApprovedSemanticOrder()
    {
        const string approvedText = "\u042f \u0435\u043b \u044f\u0431\u043b\u043e\u043a\u043e";
        var frame = CreateSolidFrame(new CaptureRegion(0, 0, 220, 80), 245);
        var sourceResult = new OcrResult(
            new OcrRequest(frame, "ru", "zone-a", orientationMode: OcrOrientationMode.Horizontal),
            new[]
            {
                new OcrTextBlock("\u044f\u0431\u043b\u043e\u043a\u043e", new BoundingBox(88, 10, 76, 18)),
                new OcrTextBlock("\u042f \u0435\u043b", new BoundingBox(10, 12, 54, 18)),
            },
            FixtureTime);
        var zone = CreateZone(TranslationGroupingMode.NearbyBlocks, mergeDistancePercent: 30);

        var grouping = TranslationTextGroupingService.CreateTextGroupingResult(sourceResult, zone);
        var requestTexts = grouping.TranslationSourceResult.TextBlocks
            .Select(block => Normalize(block.Text))
            .ToArray();

        Assert.Equal(new[] { Normalize(approvedText) }, requestTexts);
        Assert.Equal(
            new BoundingBox(10, 10, 154, 20),
            Assert.Single(grouping.TranslationSourceResult.TextBlocks).Bounds);
        Assert.Equal(
            new[] { "\u044f\u0431\u043b\u043e\u043a\u043e", "\u042f \u0435\u043b" },
            grouping.MaskSourceResult.TextBlocks.Select(block => block.Text));
    }

    [Fact]
    public void TranslationMeaningChecker_WhenTranslationLosesPartsOrOrder_FlagsTheCandidate()
    {
        var checklist = new TranslationMeaningChecklist(
            RequiredFragments: new[] { "I", "ate", "apple" },
            OrderedFragments: new[] { "I", "ate", "apple" });
        ITranslationMeaningChecker checker = new DeterministicTranslationMeaningChecker();

        Assert.True(checker.IsAcceptable("I ate an apple.", checklist));
        Assert.False(checker.IsAcceptable("apple", checklist));
        Assert.False(checker.IsAcceptable("apple I ate", checklist));
    }

    [Fact]
    public void OverlayGeometry_WhenSemanticGroupHasReferenceBounds_CentersTextInBubbleAndAvoidsForbiddenRegions()
    {
        var fixture = CreateVerticalCjkFixture();
        var frame = CreateSolidFrame(new CaptureRegion(0, 0, 240, 240), 245);
        var sourceResult = new OcrResult(
            new OcrRequest(frame, "zh-TW", "zone-a", orientationMode: OcrOrientationMode.Vertical),
            new[]
            {
                new OcrTextBlock(fixture.ApprovedTranslation, fixture.SemanticGroups[0].SourceBounds.ToBoundingBox()),
            },
            FixtureTime);
        var snapshot = new OverlayPositioningService().CreateSnapshot(sourceResult, FixtureTime.AddSeconds(1));

        var text = Assert.Single(snapshot.TextItems);
        var mask = Assert.Single(snapshot.MaskItems);
        var textBounds = GeometryBounds.FromOverlayText(text);
        var maskBounds = GeometryBounds.FromOverlayMask(mask);

        Assert.True(fixture.BubbleBounds.Contains(textBounds), $"Text bounds {textBounds} should stay inside bubble {fixture.BubbleBounds}.");
        Assert.True(textBounds.CenterDistanceTo(fixture.ApprovedOverlayBounds) <= 8d);
        Assert.True(maskBounds.CenterDistanceTo(fixture.SemanticGroups[0].SourceBounds) <= 2d);
        Assert.All(fixture.ForbiddenRegions, forbidden =>
        {
            Assert.False(textBounds.Intersects(forbidden), $"Text bounds {textBounds} intersect forbidden region {forbidden}.");
            Assert.False(maskBounds.Intersects(forbidden), $"Mask bounds {maskBounds} intersect forbidden region {forbidden}.");
        });
    }

    [Fact]
    public void DiagnosticsContract_WhenSerializedFromGoldenReference_IncludesCalibrationFieldsWithoutSecrets()
    {
        var fixture = CreateVerticalCjkFixture();
        var diagnostics = new GoldenDiagnosticsPackage(
            SchemaVersion: 1,
            FixtureId: fixture.Id,
            CaseType: fixture.CaseType,
            SourceOcr: fixture.ApprovedReadingOrder,
            TranslationSourceOcr: fixture.SemanticGroups.Select(group => group.ApprovedSourceText).ToArray(),
            MaskSourceOcr: fixture.SemanticGroups.SelectMany(group => group.MaskSourceIndexes).ToArray(),
            OverlayGeometry: new
            {
                semanticGroups = fixture.SemanticGroups.Select(group => new
                {
                    group.GroupId,
                    group.ApprovedSourceText,
                    group.SourceBounds,
                    group.RawSourceIndexes,
                    group.MaskSourceIndexes,
                    fixture.ApprovedOverlayBounds,
                }),
                forbiddenRegions = fixture.ForbiddenRegions,
            },
            SelectedPreset: "threshold-scale",
            ProviderDiagnostic: "offline calibration; credentials redacted",
            RedactedCredential: "<redacted>");

        var json = JsonSerializer.Serialize(diagnostics);

        Assert.Contains("\"SchemaVersion\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"SourceOcr\"", json, StringComparison.Ordinal);
        Assert.Contains("\"TranslationSourceOcr\"", json, StringComparison.Ordinal);
        Assert.Contains("\"MaskSourceOcr\"", json, StringComparison.Ordinal);
        Assert.Contains("\"OverlayGeometry\"", json, StringComparison.Ordinal);
        Assert.Contains("\"SelectedPreset\":\"threshold-scale\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CalibrationScorecard_WhenCandidatesAreEvaluated_SelectsBestCandidateAndWritesArtifact()
    {
        var fixtures = CreateGoldenFixtures();
        var scorecard = BuildCalibrationScorecard(fixtures, CreateCalibrationCandidateMatrix());
        var selectedCandidate = Assert.Single(scorecard.Candidates, candidate => candidate.IsSelected);

        Assert.Equal("threshold-scale_merge-30_mask-raw-4_overlay-centered", scorecard.BestCandidateId);
        Assert.Equal("threshold-scale_merge-30_mask-raw-4_overlay-centered", selectedCandidate.CandidateId);
        Assert.True(scorecard.Candidates.Count >= 24);
        Assert.True(selectedCandidate.TotalScore >= 0.95d);
        Assert.All(selectedCandidate.FixtureScores, fixtureScore =>
        {
            Assert.True(fixtureScore.OcrScore >= 0.99d);
            Assert.True(fixtureScore.GroupingScore >= 0.99d);
            Assert.True(fixtureScore.OverlayScore >= 0.99d);
            Assert.True(fixtureScore.MaskScore >= 0.99d);
            Assert.Empty(fixtureScore.Violations);
        });

        var rejectedCandidates = scorecard.Candidates
            .Where(candidate => !candidate.IsSelected)
            .ToArray();
        Assert.NotEmpty(rejectedCandidates);
        Assert.All(rejectedCandidates, candidate =>
        {
            Assert.True(
                candidate.TotalScore < selectedCandidate.TotalScore,
                $"{candidate.CandidateId} should not outscore {selectedCandidate.CandidateId}.");
            Assert.NotEmpty(candidate.FixtureScores.SelectMany(fixtureScore => fixtureScore.Violations));
        });

        var artifactDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration");
        Directory.CreateDirectory(artifactDirectory);
        var scorecardPath = Path.Combine(artifactDirectory, "scorecard.json");
        var visualEvidencePath = Path.Combine(artifactDirectory, "candidate-evidence.png");
        SaveCalibrationScorecardEvidencePng(fixtures, scorecard, visualEvidencePath);

        var artifact = new
        {
            schemaVersion = 1,
            generatedAtUtc = FixtureTime,
            purpose = "test-only calibration parameter sweep; not production behavior",
            scorecard.BestCandidateId,
            visualEvidence = new[] { Path.GetFileName(visualEvidencePath) },
            candidates = scorecard.Candidates,
        };
        var artifactJson = JsonSerializer.Serialize(
            artifact,
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });

        File.WriteAllText(scorecardPath, artifactJson);

        Assert.True(File.Exists(scorecardPath), scorecardPath);
        Assert.True(File.Exists(visualEvidencePath), visualEvidencePath);
        Assert.True(new FileInfo(visualEvidencePath).Length > 0);
        Assert.Contains("\"bestCandidateId\": \"threshold-scale_merge-30_mask-raw-4_overlay-centered\"", artifactJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", artifactJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", artifactJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RealOcrSweep_WhenTessdataIsLocal_WritesEvidenceWithoutBreakingCi()
    {
        var fixtures = CreateGoldenFixtures();
        var artifactDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration");
        Directory.CreateDirectory(artifactDirectory);
        var sweep = await BuildRealOcrSweepAsync(fixtures, artifactDirectory);
        var sweepPath = Path.Combine(artifactDirectory, "real-ocr-sweep.json");
        var sweepJson = JsonSerializer.Serialize(
            sweep,
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });

        File.WriteAllText(sweepPath, sweepJson);

        Assert.True(File.Exists(sweepPath), sweepPath);
        Assert.Contains("\"schemaVersion\": 1", sweepJson, StringComparison.Ordinal);
        Assert.Contains("\"testOnly\": true", sweepJson, StringComparison.Ordinal);
        Assert.Contains("jpn_vert.traineddata", sweepJson, StringComparison.Ordinal);
        Assert.Contains("\"sourceFrames\"", sweepJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", sweepJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", sweepJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContactSheet_WhenGenerated_WritesManualVerificationPngAndManifest()
    {
        foreach (var fixture in CreateGoldenFixtures())
        {
            var artifactDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration", fixture.Id);
            Directory.CreateDirectory(artifactDirectory);

            var contactSheetPath = Path.Combine(artifactDirectory, "contact-sheet.png");
            var manifestPath = Path.Combine(artifactDirectory, "manifest.json");
            SaveContactSheetPng(fixture, contactSheetPath);
            var manifest = new
            {
                fixture.Id,
                fixture.CaseType,
                fixture.OriginalText,
                fixture.SourceLanguage,
                fixture.SourceOrientation,
                fixture.ApprovedReadingOrder,
                fixture.SemanticGroups,
                fixture.RawSourceBounds,
                fixture.ApprovedTranslation,
                fixture.BubbleBounds,
                fixture.ApprovedOverlayBounds,
                fixture.RequiredGroupingMergeDistancePercent,
                fixture.ForbiddenRegions,
                generatedArtifacts = new[]
                {
                    Path.GetFileName(contactSheetPath),
                },
            };
            var manifestJson = JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = true,
                });

            File.WriteAllText(manifestPath, manifestJson);

            Assert.True(File.Exists(contactSheetPath), contactSheetPath);
            Assert.True(new FileInfo(contactSheetPath).Length > 0);
            Assert.True(File.Exists(manifestPath), manifestPath);
            Assert.Contains(fixture.OriginalText, manifestJson, StringComparison.Ordinal);
            Assert.Contains(fixture.ApprovedTranslation, manifestJson, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET", manifestJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TOKEN", manifestJson, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IReadOnlyList<GoldenReferenceFixture> CreateGoldenFixtures()
    {
        return new[]
        {
            CreateVerticalCjkFixture(),
            CreateVerticalJapaneseFixture(),
            CreateBookPageFixture(),
            CreatePlainTextFixture(),
        };
    }

    private static GoldenReferenceFixture CreateVerticalCjkFixture()
    {
        return new GoldenReferenceFixture(
            Id: "vertical-cjk-basic-bubble",
            CaseType: "manga_vertical_cjk",
            OriginalText: "\u4f60\u597d",
            SourceLanguage: "zh-CN",
            SourceOrientation: OcrOrientationMode.Vertical,
            ApprovedReadingOrder: new[] { "\u4f60", "\u597d" },
            RawSourceBounds: new[]
            {
                new GeometryBounds(104, 48, 16, 44),
                new GeometryBounds(104, 112, 16, 44),
            },
            SemanticGroups: new[]
            {
                new GoldenSemanticGroup(
                    GroupId: 0,
                    ApprovedSourceText: "\u4f60 \u597d",
                    SourceBounds: new GeometryBounds(96, 40, 32, 140),
                    RawSourceIndexes: new[] { 0, 1 },
                    MaskSourceIndexes: new[] { 0, 1 }),
            },
            ApprovedTranslation: "Approved translation",
            BubbleBounds: new GeometryBounds(40, 20, 160, 180),
            ApprovedOverlayBounds: new GeometryBounds(58, 64, 110, 92),
            RequiredGroupingMergeDistancePercent: 24,
            ForbiddenRegions: new[]
            {
                new GeometryBounds(0, 0, 36, 36),
                new GeometryBounds(204, 70, 30, 90),
            });
    }

    private static GoldenReferenceFixture CreateVerticalJapaneseFixture()
    {
        return new GoldenReferenceFixture(
            Id: "vertical-japanese-save-prompt",
            CaseType: "manga_vertical_japanese",
            OriginalText: "\u30bb\u30fc\u30d6\u3057\u307e\u3059\u304b",
            SourceLanguage: "ja-JP",
            SourceOrientation: OcrOrientationMode.Vertical,
            ApprovedReadingOrder: new[] { "\u30bb", "\u30fc", "\u30d6", "\u3057", "\u307e", "\u3059", "\u304b" },
            RawSourceBounds: new[]
            {
                new GeometryBounds(108, 32, 20, 22),
                new GeometryBounds(108, 57, 20, 22),
                new GeometryBounds(108, 82, 20, 22),
                new GeometryBounds(108, 107, 20, 22),
                new GeometryBounds(108, 132, 20, 22),
                new GeometryBounds(108, 157, 20, 22),
                new GeometryBounds(108, 182, 20, 22),
            },
            SemanticGroups: new[]
            {
                new GoldenSemanticGroup(
                    GroupId: 0,
                    ApprovedSourceText: "\u30bb \u30fc \u30d6 \u3057 \u307e \u3059 \u304b",
                    SourceBounds: new GeometryBounds(102, 26, 34, 184),
                    RawSourceIndexes: new[] { 0, 1, 2, 3, 4, 5, 6 },
                    MaskSourceIndexes: new[] { 0, 1, 2, 3, 4, 5, 6 }),
            },
            ApprovedTranslation: "\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c?",
            BubbleBounds: new GeometryBounds(54, 14, 132, 214),
            ApprovedOverlayBounds: new GeometryBounds(72, 72, 96, 80),
            RequiredGroupingMergeDistancePercent: 26,
            ForbiddenRegions: new[]
            {
                new GeometryBounds(0, 0, 38, 40),
                new GeometryBounds(188, 66, 32, 110),
            });
    }

    private static GoldenReferenceFixture CreateBookPageFixture()
    {
        return new GoldenReferenceFixture(
            Id: "book-page-horizontal-lines",
            CaseType: "book_page_horizontal",
            OriginalText: "THE OLD CITY WAS QUIET",
            SourceLanguage: "en",
            SourceOrientation: OcrOrientationMode.Horizontal,
            ApprovedReadingOrder: new[] { "THE OLD CITY", "WAS QUIET" },
            RawSourceBounds: new[]
            {
                new GeometryBounds(48, 56, 128, 20),
                new GeometryBounds(58, 92, 108, 20),
            },
            SemanticGroups: new[]
            {
                new GoldenSemanticGroup(
                    GroupId: 0,
                    ApprovedSourceText: "THE OLD CITY WAS QUIET",
                    SourceBounds: new GeometryBounds(44, 50, 136, 68),
                    RawSourceIndexes: new[] { 0, 1 },
                    MaskSourceIndexes: new[] { 0, 1 }),
            },
            ApprovedTranslation: "\u0421\u0442\u0430\u0440\u044b\u0439 \u0433\u043e\u0440\u043e\u0434 \u0431\u044b\u043b \u0442\u0438\u0445\u0438\u043c",
            BubbleBounds: new GeometryBounds(28, 24, 184, 172),
            ApprovedOverlayBounds: new GeometryBounds(48, 64, 144, 64),
            RequiredGroupingMergeDistancePercent: 18,
            ForbiddenRegions: new[]
            {
                new GeometryBounds(10, 12, 34, 24),
                new GeometryBounds(196, 172, 32, 36),
            });
    }

    private static GoldenReferenceFixture CreatePlainTextFixture()
    {
        return new GoldenReferenceFixture(
            Id: "plain-ui-save-game",
            CaseType: "plain_ui_horizontal",
            OriginalText: "SAVE GAME",
            SourceLanguage: "en",
            SourceOrientation: OcrOrientationMode.Horizontal,
            ApprovedReadingOrder: new[] { "SAVE", "GAME" },
            RawSourceBounds: new[]
            {
                new GeometryBounds(62, 88, 48, 18),
                new GeometryBounds(120, 88, 52, 18),
            },
            SemanticGroups: new[]
            {
                new GoldenSemanticGroup(
                    GroupId: 0,
                    ApprovedSourceText: "SAVE GAME",
                    SourceBounds: new GeometryBounds(58, 82, 118, 30),
                    RawSourceIndexes: new[] { 0, 1 },
                    MaskSourceIndexes: new[] { 0, 1 }),
            },
            ApprovedTranslation: "\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c",
            BubbleBounds: new GeometryBounds(42, 58, 156, 84),
            ApprovedOverlayBounds: new GeometryBounds(58, 78, 124, 42),
            RequiredGroupingMergeDistancePercent: 12,
            ForbiddenRegions: new[]
            {
                new GeometryBounds(14, 14, 42, 30),
                new GeometryBounds(196, 86, 30, 58),
            });
    }

    private static void SaveContactSheetPng(GoldenReferenceFixture fixture, string path)
    {
        const int width = 960;
        const int height = 340;
        var stride = checked(width * 4);
        var pixels = CreateCanvas(width, height, background: 238);

        DrawFixturePanel(pixels, stride, 40, 54, fixture, PanelLayer.Source);
        DrawFixturePanel(pixels, stride, 360, 54, fixture, PanelLayer.Overlay);
        DrawFixturePanel(pixels, stride, 680, 54, fixture, PanelLayer.Forbidden);

        SaveAnnotatedPng(pixels, width, height, stride, path, fixture);
    }

    private static void SaveCalibrationScorecardEvidencePng(
        IReadOnlyList<GoldenReferenceFixture> fixtures,
        CalibrationScorecard scorecard,
        string path)
    {
        const int width = 1360;
        const int left = 40;
        const int top = 360;
        const int columnWidth = 320;
        const int rowHeight = 300;
        var height = top + rowHeight * fixtures.Count + 80;
        var evidenceCandidates = SelectVisualEvidenceCandidates(scorecard);
        var stride = checked(width * 4);
        var pixels = CreateCanvas(width, height, background: 238);

        for (var row = 0; row < fixtures.Count; row++)
        {
            var fixture = fixtures[row];
            DrawSourceReferenceFixturePanel(
                pixels,
                stride,
                left,
                top + row * rowHeight,
                fixture);

            for (var column = 0; column < evidenceCandidates.Count; column++)
            {
                var candidate = evidenceCandidates[column];
                var fixtureScore = candidate.FixtureScores.Single(score => score.FixtureId == fixture.Id);
                DrawCandidateFixturePanel(
                    pixels,
                    stride,
                    left + (column + 1) * columnWidth,
                    top + row * rowHeight,
                    fixture,
                    fixtureScore);
            }
        }

        SaveCalibrationEvidencePng(
            pixels,
            width,
            height,
            stride,
            path,
            fixtures,
            evidenceCandidates,
            left,
            top,
            columnWidth,
            rowHeight);
    }

    private static IReadOnlyList<CalibrationCandidateScore> SelectVisualEvidenceCandidates(CalibrationScorecard scorecard)
    {
        var selected = scorecard.Candidates.Single(candidate => candidate.IsSelected);
        var overlayFailure = scorecard.Candidates
            .Where(candidate => !candidate.IsSelected)
            .OrderBy(candidate => HasReadingOrGroupingViolation(candidate) ? 1 : 0)
            .ThenBy(candidate => candidate.TotalScore)
            .First(candidate => candidate.FixtureScores.Any(score =>
                score.Violations.Contains("overlay-intersects-forbidden-region", StringComparer.Ordinal)
                || score.Violations.Contains("overlay-outside-reference-region", StringComparer.Ordinal)));
        var groupingFailure = scorecard.Candidates
            .Where(candidate => !candidate.IsSelected)
            .OrderBy(candidate => HasOverlayViolation(candidate) ? 1 : 0)
            .ThenBy(candidate => candidate.TotalScore)
            .First(candidate => candidate.FixtureScores.Any(score =>
                score.Violations.Contains("reading-order-mismatch", StringComparer.Ordinal)
                || score.Violations.Contains("translation-request-mismatch", StringComparer.Ordinal)));

        var candidates = new List<CalibrationCandidateScore>();
        AddVisualEvidenceCandidate(candidates, selected);
        AddVisualEvidenceCandidate(candidates, overlayFailure);
        AddVisualEvidenceCandidate(candidates, groupingFailure);
        foreach (var fallback in scorecard.Candidates.Where(candidate => !candidate.IsSelected).OrderBy(candidate => candidate.TotalScore))
        {
            if (candidates.Count >= 3)
            {
                break;
            }

            AddVisualEvidenceCandidate(candidates, fallback);
        }

        return candidates;
    }

    private static bool HasOverlayViolation(CalibrationCandidateScore candidate)
    {
        return candidate.FixtureScores.Any(score =>
            score.Violations.Contains("overlay-intersects-forbidden-region", StringComparer.Ordinal)
            || score.Violations.Contains("overlay-outside-reference-region", StringComparer.Ordinal));
    }

    private static bool HasReadingOrGroupingViolation(CalibrationCandidateScore candidate)
    {
        return candidate.FixtureScores.Any(score =>
            score.Violations.Contains("reading-order-mismatch", StringComparer.Ordinal)
            || score.Violations.Contains("translation-request-mismatch", StringComparer.Ordinal));
    }

    private static void AddVisualEvidenceCandidate(
        List<CalibrationCandidateScore> candidates,
        CalibrationCandidateScore candidate)
    {
        if (candidates.All(existing => !string.Equals(existing.CandidateId, candidate.CandidateId, StringComparison.Ordinal)))
        {
            candidates.Add(candidate);
        }
    }

    private static byte[] CreateCanvas(int width, int height, byte background)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = background;
            pixels[index + 1] = background;
            pixels[index + 2] = background;
            pixels[index + 3] = byte.MaxValue;
        }

        return pixels;
    }

    private static void DrawFixturePanel(
        byte[] pixels,
        int stride,
        int originX,
        int originY,
        GoldenReferenceFixture fixture,
        PanelLayer layer)
    {
        var frame = new GeometryBounds(originX, originY, 240, 240);
        FillRectangle(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 210, 210, 210);
        FillOffsetRectangle(pixels, stride, originX, originY, fixture.BubbleBounds, 255, 255, 255);
        DrawOffsetOutline(pixels, stride, originX, originY, fixture.BubbleBounds, 80, 80, 80, thickness: 2);

        foreach (var raw in fixture.RawSourceBounds)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, raw, 20, 20, 20);
        }

        foreach (var group in fixture.SemanticGroups)
        {
            DrawOffsetOutline(pixels, stride, originX, originY, group.SourceBounds, 0, 150, 60, thickness: 3);
        }

        if (layer is PanelLayer.Overlay)
        {
            foreach (var raw in fixture.RawSourceBounds)
            {
                var mask = new GeometryBounds(raw.X - 4, raw.Y - 4, raw.Width + 8, raw.Height + 8);
                FillOffsetRectangle(pixels, stride, originX, originY, mask, 70, 70, 70);
                DrawOffsetOutline(pixels, stride, originX, originY, mask, 0, 0, 0, thickness: 2);
            }

            FillOffsetRectangle(pixels, stride, originX, originY, fixture.ApprovedOverlayBounds, 185, 220, 255);
            DrawOffsetOutline(pixels, stride, originX, originY, fixture.ApprovedOverlayBounds, 20, 100, 230, thickness: 3);
        }

        if (layer is PanelLayer.Forbidden)
        {
            foreach (var forbidden in fixture.ForbiddenRegions)
            {
                FillOffsetRectangle(pixels, stride, originX, originY, forbidden, 255, 205, 205);
                DrawOffsetOutline(pixels, stride, originX, originY, forbidden, 220, 0, 0, thickness: 3);
            }
        }

        DrawRectangleOutline(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 40, 40, 40, thickness: 1);
    }

    private static void DrawCandidateFixturePanel(
        byte[] pixels,
        int stride,
        int originX,
        int originY,
        GoldenReferenceFixture fixture,
        CalibrationFixtureScore fixtureScore)
    {
        var frame = new GeometryBounds(originX, originY, 240, 240);
        FillRectangle(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 210, 210, 210);
        FillOffsetRectangle(pixels, stride, originX, originY, fixture.BubbleBounds, 255, 255, 255);
        DrawOffsetOutline(pixels, stride, originX, originY, fixture.BubbleBounds, 80, 80, 80, thickness: 2);

        foreach (var forbidden in fixture.ForbiddenRegions)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, forbidden, 255, 225, 225);
            DrawOffsetOutline(pixels, stride, originX, originY, forbidden, 220, 0, 0, thickness: 3);
        }

        foreach (var raw in fixture.RawSourceBounds)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, raw, 20, 20, 20);
        }

        foreach (var mask in fixtureScore.MaskBounds)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, mask, 70, 70, 70);
            DrawOffsetOutline(pixels, stride, originX, originY, mask, 0, 0, 0, thickness: 2);
        }

        foreach (var group in fixture.SemanticGroups)
        {
            DrawOffsetOutline(pixels, stride, originX, originY, group.SourceBounds, 0, 150, 60, thickness: 3);
        }

        FillOffsetRectangle(pixels, stride, originX, originY, fixtureScore.OverlayBounds, 185, 220, 255);
        DrawOffsetOutline(pixels, stride, originX, originY, fixtureScore.OverlayBounds, 20, 100, 230, thickness: 3);

        foreach (var forbidden in fixture.ForbiddenRegions)
        {
            DrawOffsetOutline(pixels, stride, originX, originY, forbidden, 220, 0, 0, thickness: 3);
        }

        DrawRectangleOutline(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 40, 40, 40, thickness: 1);
    }

    private static void DrawSourceReferenceFixturePanel(
        byte[] pixels,
        int stride,
        int originX,
        int originY,
        GoldenReferenceFixture fixture)
    {
        var frame = new GeometryBounds(originX, originY, 240, 240);
        FillRectangle(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 210, 210, 210);
        FillOffsetRectangle(pixels, stride, originX, originY, fixture.BubbleBounds, 255, 255, 255);
        DrawOffsetOutline(pixels, stride, originX, originY, fixture.BubbleBounds, 80, 80, 80, thickness: 2);

        foreach (var forbidden in fixture.ForbiddenRegions)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, forbidden, 255, 245, 245);
            DrawOffsetOutline(pixels, stride, originX, originY, forbidden, 220, 0, 0, thickness: 2);
        }

        foreach (var raw in fixture.RawSourceBounds)
        {
            DrawOffsetOutline(pixels, stride, originX, originY, raw, 20, 20, 20, thickness: 1);
        }

        foreach (var group in fixture.SemanticGroups)
        {
            DrawOffsetOutline(pixels, stride, originX, originY, group.SourceBounds, 0, 150, 60, thickness: 2);
        }

        DrawRectangleOutline(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 40, 40, 40, thickness: 1);
    }

    private static void SaveAnnotatedPng(
        byte[] pixels,
        int width,
        int height,
        int stride,
        string path,
        GoldenReferenceFixture fixture)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SaveAnnotatedPngOnStaThread(pixels, width, height, stride, path, fixture);
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
            throw new InvalidOperationException("Failed to write annotated calibration contact sheet.", failure);
        }
    }

    private static void SaveAnnotatedPngOnStaThread(
        byte[] pixels,
        int width,
        int height,
        int stride,
        string path,
        GoldenReferenceFixture fixture)
    {
        var image = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        image.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(image, new Rect(0, 0, width, height));
            DrawContactSheetText(context, "Source OCR", 40, 24, 16, Brushes.Black, semiBold: true);
            DrawContactSheetText(context, "Mask + overlay", 360, 24, 16, Brushes.Black, semiBold: true);
            DrawContactSheetText(context, "Forbidden regions", 680, 24, 16, Brushes.Black, semiBold: true);

            DrawSourceGlyphs(context, fixture, originX: 40, originY: 54);
            DrawContactSheetText(context, fixture.ApprovedTranslation, 424, 145, 13, Brushes.Black, semiBold: true, maxTextWidth: 92);
            var firstForbiddenRegion = fixture.ForbiddenRegions[0];
            DrawContactSheetText(
                context,
                "Do not cover",
                680 + firstForbiddenRegion.Right + 8,
                54 + firstForbiddenRegion.Y + 2,
                11,
                Brushes.DarkRed,
                semiBold: true,
                maxTextWidth: 100);

            DrawContactSheetText(context, $"Original: {fixture.OriginalText}", 40, 300, 13, Brushes.Black, maxTextWidth: 260);
            DrawContactSheetText(context, $"Approved: {fixture.ApprovedTranslation}", 360, 300, 13, Brushes.Black, maxTextWidth: 260);
            DrawContactSheetText(context, $"Fixture: {fixture.Id}", 680, 300, 13, Brushes.Black, maxTextWidth: 260);
            DrawContactSheetText(
                context,
                "Legend: black=raw OCR, green=semantic group, gray=mask, blue=approved overlay, red=forbidden",
                40,
                322,
                12,
                Brushes.DimGray,
                maxTextWidth: 880);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        SavePng(bitmap, path);
    }

    private static void SaveCalibrationEvidencePng(
        byte[] pixels,
        int width,
        int height,
        int stride,
        string path,
        IReadOnlyList<GoldenReferenceFixture> fixtures,
        IReadOnlyList<CalibrationCandidateScore> candidates,
        int left,
        int top,
        int columnWidth,
        int rowHeight)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SaveCalibrationEvidencePngOnStaThread(
                    pixels,
                    width,
                    height,
                    stride,
                    path,
                    fixtures,
                    candidates,
                    left,
                    top,
                    columnWidth,
                    rowHeight);
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
            throw new InvalidOperationException("Failed to write calibration scorecard visual evidence.", failure);
        }
    }

    private static void SaveCalibrationEvidencePngOnStaThread(
        byte[] pixels,
        int width,
        int height,
        int stride,
        string path,
        IReadOnlyList<GoldenReferenceFixture> fixtures,
        IReadOnlyList<CalibrationCandidateScore> candidates,
        int left,
        int top,
        int columnWidth,
        int rowHeight)
    {
        var image = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        image.Freeze();

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(image, new Rect(0, 0, width, height));
            DrawContactSheetText(context, "Calibration candidate evidence", 40, 18, 18, Brushes.Black, semiBold: true, maxTextWidth: 520);
            DrawCalibrationEvidenceLegend(context, 40, 48);
            DrawCalibrationReviewNotes(context, 40, 128);

            DrawContactSheetText(context, "Source reference", left, 280, 11, Brushes.Black, semiBold: true, maxTextWidth: 285);
            DrawContactSheetText(context, "original text; no mask/overlay", left, 308, 11, Brushes.Black, maxTextWidth: 285);

            for (var column = 0; column < candidates.Count; column++)
            {
                var candidate = candidates[column];
                var x = left + (column + 1) * columnWidth;
                DrawContactSheetText(context, candidate.CandidateId, x, 280, 10, Brushes.Black, semiBold: true, maxTextWidth: 285);
                DrawContactSheetText(
                    context,
                    FormattableString.Invariant($"score={candidate.TotalScore:0.####} selected={candidate.IsSelected}"),
                    x,
                    308,
                    11,
                    Brushes.Black,
                    maxTextWidth: 285);
            }

            for (var row = 0; row < fixtures.Count; row++)
            {
                var fixture = fixtures[row];
                var y = top + row * rowHeight;
                DrawContactSheetText(context, fixture.Id, left, y - 18, 11, Brushes.Black, semiBold: true, maxTextWidth: 285);
                DrawSourceGlyphs(context, fixture, originX: left, originY: y, textBrush: Brushes.Black);
                DrawContactSheetText(
                    context,
                    $"source: {fixture.OriginalText}",
                    left,
                    y + 244,
                    10,
                    Brushes.Black,
                    maxTextWidth: 285);
                DrawContactSheetText(
                    context,
                    "compare text coverage to candidate panels",
                    left,
                    y + 258,
                    10,
                    Brushes.DimGray,
                    maxTextWidth: 285);

                for (var column = 0; column < candidates.Count; column++)
                {
                    var candidate = candidates[column];
                    var x = left + (column + 1) * columnWidth;
                    var score = candidate.FixtureScores.Single(item => item.FixtureId == fixture.Id);
                    var violations = score.Violations.Count == 0
                        ? "OK"
                        : string.Join(", ", score.Violations.Take(2));
                    DrawContactSheetText(
                        context,
                        FormattableString.Invariant(
                            $"fixture={score.TotalScore:0.####} ocr={score.OcrScore:0.##} grp={score.GroupingScore:0.##} mask={score.MaskScore:0.##} ovl={score.OverlayScore:0.##}"),
                        x,
                        y + 244,
                        10,
                        Brushes.Black,
                        maxTextWidth: 285);
                    DrawContactSheetText(context, violations, x, y + 258, 10, score.Violations.Count == 0 ? Brushes.DarkGreen : Brushes.DarkRed, maxTextWidth: 285);
                }
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        SavePng(bitmap, path);
    }

    private static void DrawCalibrationEvidenceLegend(DrawingContext context, double x, double y)
    {
        DrawContactSheetText(context, "Легенда областей", x, y, 13, Brushes.Black, semiBold: true, maxTextWidth: 220);
        DrawLegendItem(context, x, y + 26, Brushes.Black, null, "сырой OCR-блок");
        DrawLegendItem(context, x + 170, y + 26, Brushes.Transparent, new Pen(Brushes.ForestGreen, 3), "semantic group");
        DrawLegendItem(context, x + 340, y + 26, Brushes.DimGray, new Pen(Brushes.Black, 2), "маска");
        DrawLegendItem(context, x + 510, y + 26, new SolidColorBrush(Color.FromRgb(185, 220, 255)), new Pen(Brushes.DodgerBlue, 3), "overlay перевод");
        DrawLegendItem(context, x + 680, y + 26, new SolidColorBrush(Color.FromRgb(255, 225, 225)), new Pen(Brushes.Red, 3), "нельзя закрывать");
        DrawLegendItem(context, x, y + 58, Brushes.White, new Pen(Brushes.DimGray, 2), "bubble/reference");
        DrawContactSheetText(
            context,
                "В колонках: source reference без маски/overlay, лучший кандидат, пример провала overlay, пример провала OCR/order/grouping.",
                x + 170,
                y + 58,
                12,
            Brushes.DimGray,
                maxTextWidth: 740);
    }

    private static void DrawCalibrationReviewNotes(DrawingContext context, double x, double y)
    {
        DrawContactSheetText(context, "Правила визуальной проверки", x, y, 13, Brushes.Black, semiBold: true, maxTextWidth: 320);
        DrawReviewRule(
            context,
            x,
            y + 24,
            Brushes.Black,
            null,
            "OCR-блок: сравни с Source reference; на candidate panel каждый фрагмент текста должен иметь черный OCR-блок. Если текст есть в reference, а блока нет — сообщи.");
        DrawReviewRule(
            context,
            x,
            y + 42,
            Brushes.Transparent,
            new Pen(Brushes.ForestGreen, 3),
            "Semantic group: должен объединять только OCR-блоки одной переводимой фразы; если объединяет лишнее или разрывает фразу — сообщи.");
        DrawReviewRule(
            context,
            x,
            y + 60,
            Brushes.DimGray,
            new Pen(Brushes.Black, 2),
            "Маска: должна закрывать исходный текст из принятых OCR-блоков; если закрывает лицо/UI/фон или пропускает текст — сообщи.");
        DrawReviewRule(
            context,
            x,
            y + 78,
            new SolidColorBrush(Color.FromRgb(185, 220, 255)),
            new Pen(Brushes.DodgerBlue, 3),
            "Overlay: перевод должен оставаться внутри bubble/reference и не залезать на красные зоны; если сдвинут, обрезан или пересекает запрет — сообщи.");
        DrawReviewRule(
            context,
            x,
            y + 96,
            new SolidColorBrush(Color.FromRgb(255, 225, 225)),
            new Pen(Brushes.Red, 3),
            "Forbidden: красные зоны нельзя закрывать ни маской, ни overlay; любое пересечение — баг кандидата.");
    }

    private static void DrawReviewRule(
        DrawingContext context,
        double x,
        double y,
        Brush fill,
        Pen? outline,
        string text)
    {
        context.DrawRectangle(fill, outline, new Rect(x, y + 2, 14, 12));
        DrawContactSheetText(context, text, x + 22, y, 11, Brushes.Black, maxTextWidth: 930);
    }

    private static void DrawLegendItem(
        DrawingContext context,
        double x,
        double y,
        Brush fill,
        Pen? outline,
        string label)
    {
        context.DrawRectangle(fill, outline, new Rect(x, y, 22, 16));
        DrawContactSheetText(context, label, x + 30, y - 1, 12, Brushes.Black, maxTextWidth: 135);
    }

    private static void DrawSourceGlyphs(
        DrawingContext context,
        GoldenReferenceFixture fixture,
        int originX,
        int originY,
        Brush? textBrush = null)
    {
        var glyphBrush = textBrush ?? Brushes.White;
        var glyphCount = Math.Min(fixture.ApprovedReadingOrder.Count, fixture.RawSourceBounds.Count);
        for (var index = 0; index < glyphCount; index++)
        {
            var bounds = fixture.RawSourceBounds[index];
            var isHorizontal = bounds.Width >= bounds.Height;
            var fontSize = isHorizontal
                ? Math.Clamp(bounds.Height - 4d, 9d, 14d)
                : Math.Clamp(bounds.Width + 2d, 12d, 18d);
            var textX = originX + bounds.X + (isHorizontal ? 3d : -2d);
            var textY = originY + bounds.Y + (isHorizontal ? 1d : Math.Max(2d, (bounds.Height - fontSize) / 2d));
            var maxTextWidth = Math.Max(28d, bounds.Width + 8d);
            DrawContactSheetText(
                context,
                fixture.ApprovedReadingOrder[index],
                textX,
                textY,
                fontSize,
                glyphBrush,
                semiBold: true,
                fontFamily: "Microsoft YaHei UI",
                maxTextWidth: maxTextWidth);
        }
    }

    private static void DrawContactSheetText(
        DrawingContext context,
        string text,
        double x,
        double y,
        double fontSize,
        Brush brush,
        bool semiBold = false,
        string fontFamily = "Segoe UI",
        double maxTextWidth = 280)
    {
        var formattedText = new FormattedText(
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
            pixelsPerDip: 1d)
        {
            MaxTextWidth = maxTextWidth,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        context.DrawText(formattedText, new Point(x, y));
    }

    private static void FillOffsetRectangle(
        byte[] pixels,
        int stride,
        int originX,
        int originY,
        GeometryBounds bounds,
        byte red,
        byte green,
        byte blue)
    {
        FillRectangle(pixels, stride, originX + bounds.X, originY + bounds.Y, bounds.Width, bounds.Height, red, green, blue);
    }

    private static void DrawOffsetOutline(
        byte[] pixels,
        int stride,
        int originX,
        int originY,
        GeometryBounds bounds,
        byte red,
        byte green,
        byte blue,
        int thickness)
    {
        DrawRectangleOutline(
            pixels,
            stride,
            originX + bounds.X,
            originY + bounds.Y,
            bounds.Width,
            bounds.Height,
            red,
            green,
            blue,
            thickness);
    }

    private static RenderedTextFixture RenderTextFixture(string text, int width, int height)
    {
        var region = new CaptureRegion(0, 0, width, height);
        var frame = CreateSolidFrame(region, 255);
        var pixels = frame.PixelData.ToArray();
        var stride = frame.Stride;
        var left = 8;
        var top = height / 2 - 8;
        var glyphWidth = 7;
        var glyphGap = 3;

        for (var characterIndex = 0; characterIndex < text.Length; characterIndex++)
        {
            if (char.IsWhiteSpace(text[characterIndex]))
            {
                continue;
            }

            var glyphX = left + characterIndex * (glyphWidth + glyphGap);
            FillRectangle(pixels, stride, glyphX, top, glyphWidth, 16, 0);
        }

        return new RenderedTextFixture(
            text,
            new CapturedFrame(region, width, height, stride, "Bgra32", pixels, FixtureTime),
            new BoundingBox(left, top, text.Length * (glyphWidth + glyphGap) - glyphGap, 16));
    }

    private static OcrCalibrationResult? SelectSuccessfulOcrPreset(
        string expectedText,
        CapturedFrame frame,
        IReadOnlyList<OcrPresetCandidate> candidates,
        double maxCharacterErrorRate)
    {
        foreach (var candidate in candidates)
        {
            var result = candidate.Recognize(frame);
            var errorRate = CalculateCharacterErrorRate(expectedText, result.Text);
            if (errorRate <= maxCharacterErrorRate)
            {
                return new OcrCalibrationResult(candidate, result, errorRate);
            }
        }

        return null;
    }

    private static OcrResult CreateOcrResult(
        CapturedFrame frame,
        string text,
        BoundingBox bounds,
        OcrOrientationMode orientationMode)
    {
        return new OcrResult(
            new OcrRequest(frame, "en", "fixture-zone", orientationMode: orientationMode),
            new[] { new OcrTextBlock(text, bounds) },
            FixtureTime);
    }

    private static IReadOnlyList<CalibrationCandidate> CreateCalibrationCandidateMatrix()
    {
        var ocrPresets = new[]
        {
            new CalibrationOcrPreset("threshold-scale", ExpectedCharacterErrorRate: 0d, ReverseReadingOrder: false, DropLastReadingUnit: false),
            new CalibrationOcrPreset("default-noisy", ExpectedCharacterErrorRate: 0.12d, ReverseReadingOrder: false, DropLastReadingUnit: false),
            new CalibrationOcrPreset("order-lost", ExpectedCharacterErrorRate: 0.35d, ReverseReadingOrder: true, DropLastReadingUnit: true),
        };
        var groupingMergeDistances = new[] { 8d, 18d, 30d };
        var maskOptions = new[]
        {
            new CalibrationMaskOption("raw-4", UseRawMaskSource: true, Padding: 4),
            new CalibrationMaskOption("raw-12", UseRawMaskSource: true, Padding: 12),
            new CalibrationMaskOption("group-4", UseRawMaskSource: false, Padding: 4),
        };
        var overlayOptions = new[]
        {
            new CalibrationOverlayOption("centered", OffsetX: 0, OffsetY: 0, Inflation: 0),
            new CalibrationOverlayOption("loose-right", OffsetX: 80, OffsetY: 0, Inflation: 20),
        };

        return (
            from ocrPreset in ocrPresets
            from groupingMergeDistance in groupingMergeDistances
            from maskOption in maskOptions
            from overlayOption in overlayOptions
            select new CalibrationCandidate(
                CandidateId: FormattableString.Invariant(
                    $"{ocrPreset.Name}_merge-{groupingMergeDistance:0}_mask-{maskOption.Name}_overlay-{overlayOption.Name}"),
                OcrPresetName: ocrPreset.Name,
                ExpectedOcrCharacterErrorRate: ocrPreset.ExpectedCharacterErrorRate,
                ReverseReadingOrder: ocrPreset.ReverseReadingOrder,
                DropLastReadingUnit: ocrPreset.DropLastReadingUnit,
                GroupingMergeDistancePercent: groupingMergeDistance,
                UseRawMaskSource: maskOption.UseRawMaskSource,
                MaskPadding: maskOption.Padding,
                OverlayOffsetX: overlayOption.OffsetX,
                OverlayOffsetY: overlayOption.OffsetY,
                OverlayInflation: overlayOption.Inflation))
            .ToArray();
    }

    private static CalibrationScorecard BuildCalibrationScorecard(
        IReadOnlyList<GoldenReferenceFixture> fixtures,
        IReadOnlyList<CalibrationCandidate> candidates)
    {
        var candidateScores = candidates
            .Select(candidate => ScoreCalibrationCandidate(candidate, fixtures))
            .ToArray();
        var bestCandidate = candidateScores
            .OrderByDescending(candidate => candidate.TotalScore)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .First();

        return new CalibrationScorecard(
            bestCandidate.CandidateId,
            candidateScores
                .Select(candidate => candidate with
                {
                    IsSelected = string.Equals(
                        candidate.CandidateId,
                        bestCandidate.CandidateId,
                        StringComparison.Ordinal),
                })
                .ToArray());
    }

    private static CalibrationCandidateScore ScoreCalibrationCandidate(
        CalibrationCandidate candidate,
        IReadOnlyList<GoldenReferenceFixture> fixtures)
    {
        var fixtureScores = fixtures
            .Select(fixture => ScoreCalibrationFixture(candidate, fixture))
            .ToArray();
        var totalScore = fixtureScores.Average(score => score.TotalScore);

        return new CalibrationCandidateScore(
            candidate.CandidateId,
            candidate,
            Math.Round(totalScore, 4),
            IsSelected: false,
            fixtureScores);
    }

    private static CalibrationFixtureScore ScoreCalibrationFixture(
        CalibrationCandidate candidate,
        GoldenReferenceFixture fixture)
    {
        var recognizedUnits = CreateCandidateRecognizedUnits(candidate, fixture);
        var expectedOcrText = string.Join(" ", fixture.ApprovedReadingOrder);
        var recognizedText = string.Join(" ", recognizedUnits);
        var observedCharacterErrorRate = Math.Max(
            CalculateCharacterErrorRate(expectedOcrText, recognizedText),
            candidate.ExpectedOcrCharacterErrorRate);
        var ocrScore = ClampScore(1d - observedCharacterErrorRate);
        var readingOrderScore = IsSameSequence(recognizedUnits, fixture.ApprovedReadingOrder) ? 1d : 0d;

        var requestTexts = CreateCandidateTranslationRequests(candidate, fixture, recognizedUnits);
        var expectedRequests = fixture.SemanticGroups
            .Select(group => group.ApprovedSourceText)
            .ToArray();
        var groupingScore = IsSameSequence(requestTexts, expectedRequests) ? 1d : 0d;

        var candidateOverlayBounds = OffsetAndInflate(
            fixture.ApprovedOverlayBounds,
            candidate.OverlayOffsetX,
            candidate.OverlayOffsetY,
            candidate.OverlayInflation);
        var overlayCenterDistance = candidateOverlayBounds.CenterDistanceTo(fixture.ApprovedOverlayBounds);
        var overlayInsideBubble = fixture.BubbleBounds.Contains(candidateOverlayBounds);
        var overlayForbiddenIntersections = fixture.ForbiddenRegions
            .Count(region => candidateOverlayBounds.Intersects(region));
        var overlayScore = ClampScore(1d - overlayCenterDistance / 80d);
        if (!overlayInsideBubble)
        {
            overlayScore *= 0.5d;
        }

        if (overlayForbiddenIntersections > 0)
        {
            overlayScore = 0d;
        }

        var maskBounds = CreateCandidateMaskBounds(candidate, fixture);
        var maskForbiddenIntersections = maskBounds
            .Count(mask => fixture.ForbiddenRegions.Any(mask.Intersects));
        var maskScore = ScoreCandidateMask(candidate, fixture, maskBounds, maskForbiddenIntersections);

        var violations = new List<string>();
        if (ocrScore < 0.99d)
        {
            violations.Add("ocr-text-mismatch");
        }

        if (readingOrderScore < 0.99d)
        {
            violations.Add("reading-order-mismatch");
        }

        if (groupingScore < 0.99d)
        {
            violations.Add("translation-request-mismatch");
        }

        if (!overlayInsideBubble)
        {
            violations.Add("overlay-outside-reference-region");
        }

        if (overlayForbiddenIntersections > 0)
        {
            violations.Add("overlay-intersects-forbidden-region");
        }

        if (maskScore < 0.99d)
        {
            violations.Add(candidate.UseRawMaskSource
                ? "mask-padding-away-from-reference"
                : "mask-source-not-raw-ocr-blocks");
        }

        if (maskForbiddenIntersections > 0)
        {
            violations.Add("mask-intersects-forbidden-region");
        }

        var totalScore = 0.25d * ocrScore
            + 0.15d * readingOrderScore
            + 0.2d * groupingScore
            + 0.25d * overlayScore
            + 0.15d * maskScore;

        return new CalibrationFixtureScore(
            fixture.Id,
            Math.Round(ocrScore, 4),
            Math.Round(readingOrderScore, 4),
            Math.Round(groupingScore, 4),
            Math.Round(overlayScore, 4),
            Math.Round(maskScore, 4),
            Math.Round(totalScore, 4),
            Math.Round(overlayCenterDistance, 2),
            overlayForbiddenIntersections,
            maskForbiddenIntersections,
            candidateOverlayBounds,
            maskBounds,
            requestTexts,
            violations);
    }

    private static IReadOnlyList<string> CreateCandidateRecognizedUnits(
        CalibrationCandidate candidate,
        GoldenReferenceFixture fixture)
    {
        var units = candidate.ReverseReadingOrder
            ? fixture.ApprovedReadingOrder.Reverse().ToArray()
            : fixture.ApprovedReadingOrder.ToArray();

        if (candidate.DropLastReadingUnit && units.Length > 0)
        {
            units = units.Take(units.Length - 1).ToArray();
        }

        return units;
    }

    private static IReadOnlyList<string> CreateCandidateTranslationRequests(
        CalibrationCandidate candidate,
        GoldenReferenceFixture fixture,
        IReadOnlyList<string> recognizedUnits)
    {
        if (candidate.GroupingMergeDistancePercent < fixture.RequiredGroupingMergeDistancePercent)
        {
            return recognizedUnits;
        }

        return fixture.SemanticGroups
            .Select(_ => string.Join(" ", recognizedUnits))
            .ToArray();
    }

    private static IReadOnlyList<GeometryBounds> CreateCandidateMaskBounds(
        CalibrationCandidate candidate,
        GoldenReferenceFixture fixture)
    {
        if (candidate.UseRawMaskSource)
        {
            return fixture.RawSourceBounds
                .Select(bounds => OffsetAndInflate(bounds, offsetX: 0, offsetY: 0, inflation: candidate.MaskPadding))
                .ToArray();
        }

        return fixture.SemanticGroups
            .Select(group => OffsetAndInflate(group.SourceBounds, offsetX: 0, offsetY: 0, inflation: candidate.MaskPadding))
            .ToArray();
    }

    private static double ScoreCandidateMask(
        CalibrationCandidate candidate,
        GoldenReferenceFixture fixture,
        IReadOnlyList<GeometryBounds> maskBounds,
        int maskForbiddenIntersections)
    {
        if (!candidate.UseRawMaskSource || maskBounds.Count != fixture.RawSourceBounds.Count)
        {
            return 0.25d;
        }

        if (maskForbiddenIntersections > 0)
        {
            return 0d;
        }

        var paddingPenalty = Math.Min(Math.Abs(candidate.MaskPadding - 4) / 16d, 0.75d);
        return ClampScore(1d - paddingPenalty);
    }

    private static async Task<RealOcrSweepPackage> BuildRealOcrSweepAsync(
        IReadOnlyList<GoldenReferenceFixture> fixtures,
        string artifactDirectory)
    {
        var sourceFrames = SaveRealOcrSourceFrames(fixtures, artifactDirectory);
        var requiredTrainedData = fixtures
            .Select(GetRequiredTrainedDataFileName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var tessdataPath = TryFindTessdataPath(requiredTrainedData, out var unavailableReason);
        if (tessdataPath is null)
        {
            return new RealOcrSweepPackage(
                SchemaVersion: 1,
                TestOnly: true,
                GeneratedAtUtc: FixtureTime,
                Status: "unavailable",
                TessdataLocation: null,
                RequiredTrainedData: requiredTrainedData,
                CandidateTessdataLocations: CreateTessdataCandidatePaths().Select(FormatTessdataLocation).ToArray(),
                SetupInstructions: CreateRealOcrSetupInstructions(requiredTrainedData),
                SourceFrames: sourceFrames,
                UnavailableReason: unavailableReason,
                Fixtures: Array.Empty<RealOcrFixtureSweep>());
        }

        var engine = new TesseractOcrEngine(tessdataPath);
        var preprocessor = new OcrPreprocessor();
        var presetCandidates = CreateRealOcrPresetCandidates();
        var fixtureSweeps = new List<RealOcrFixtureSweep>();

        foreach (var fixture in fixtures)
        {
            var sourceFrame = RenderSourceReferenceFrame(fixture);
            var candidateResults = new List<RealOcrCandidateSweep>();
            foreach (var candidate in presetCandidates)
            {
                candidateResults.Add(await RunRealOcrCandidateAsync(engine, preprocessor, fixture, sourceFrame, candidate));
            }

            fixtureSweeps.Add(
                new RealOcrFixtureSweep(
                    fixture.Id,
                    fixture.SourceLanguage,
                    fixture.SourceOrientation.ToString(),
                    Normalize(string.Join(" ", fixture.ApprovedReadingOrder)),
                    candidateResults));
        }

        var status = fixtureSweeps.SelectMany(fixture => fixture.Candidates).Any(candidate => candidate.Status == "success")
            ? "ran"
            : "failed";

        return new RealOcrSweepPackage(
            SchemaVersion: 1,
            TestOnly: true,
            GeneratedAtUtc: FixtureTime,
            Status: status,
            TessdataLocation: FormatTessdataLocation(tessdataPath),
            RequiredTrainedData: requiredTrainedData,
            CandidateTessdataLocations: CreateTessdataCandidatePaths().Select(FormatTessdataLocation).ToArray(),
            SetupInstructions: CreateRealOcrSetupInstructions(requiredTrainedData),
            SourceFrames: sourceFrames,
            UnavailableReason: null,
            Fixtures: fixtureSweeps);
    }

    private static IReadOnlyList<RealOcrSourceFrameEvidence> SaveRealOcrSourceFrames(
        IReadOnlyList<GoldenReferenceFixture> fixtures,
        string artifactDirectory)
    {
        var sourceDirectory = Path.Combine(artifactDirectory, "real-ocr");
        Directory.CreateDirectory(sourceDirectory);
        var sourceFrames = new List<RealOcrSourceFrameEvidence>();

        foreach (var fixture in fixtures)
        {
            var frame = RenderSourceReferenceFrame(fixture);
            var relativePath = Path.Combine("real-ocr", $"{fixture.Id}-source.png");
            var sourcePath = Path.Combine(artifactDirectory, relativePath);
            SavePng(frame.PixelData.ToArray(), frame.Width, frame.Height, frame.Stride, sourcePath);
            sourceFrames.Add(
                new RealOcrSourceFrameEvidence(
                    fixture.Id,
                    relativePath.Replace('\\', '/'),
                    frame.Width,
                    frame.Height,
                    fixture.SourceLanguage,
                    fixture.SourceOrientation.ToString(),
                    Normalize(string.Join(" ", fixture.ApprovedReadingOrder))));
        }

        return sourceFrames;
    }

    private static IReadOnlyList<string> CreateRealOcrSetupInstructions(IReadOnlyList<string> requiredTrainedData)
    {
        return new[]
        {
            $"Place {string.Join(", ", requiredTrainedData)} in repository tessdata/ or set TESSDATA_PREFIX to a folder containing them.",
            "Re-run dotnet test GameTranslator.sln -c Release --no-build to refresh artifacts/calibration/real-ocr-sweep.json.",
            "Keep traineddata binaries local unless the project owner explicitly approves committing them.",
        };
    }

    private static IReadOnlyList<RealOcrPresetCandidate> CreateRealOcrPresetCandidates()
    {
        return new[]
        {
            new RealOcrPresetCandidate("default", OcrPreprocessingSettings.Default),
            new RealOcrPresetCandidate(
                "threshold-scale",
                new OcrPreprocessingSettings
                {
                    IsEnabled = true,
                    ThresholdingEnabled = true,
                    Threshold = 160,
                    Scale = 1.5,
                }),
        };
    }

    private static async Task<RealOcrCandidateSweep> RunRealOcrCandidateAsync(
        TesseractOcrEngine engine,
        OcrPreprocessor preprocessor,
        GoldenReferenceFixture fixture,
        CapturedFrame sourceFrame,
        RealOcrPresetCandidate candidate)
    {
        try
        {
            var processedFrame = preprocessor.Apply(sourceFrame, candidate.Settings);
            var request = new OcrRequest(
                processedFrame,
                fixture.SourceLanguage,
                fixture.Id,
                candidate.Settings,
                OcrSettings.TesseractEngineId,
                fixture.SourceOrientation);
            var result = await engine.RecognizeAsync(request);
            var normalizedExpected = Normalize(string.Join(" ", fixture.ApprovedReadingOrder));
            var normalizedActual = Normalize(result.Text);

            return new RealOcrCandidateSweep(
                candidate.Name,
                "success",
                processedFrame.Width,
                processedFrame.Height,
                result.Text,
                normalizedActual,
                Math.Round(CalculateCharacterErrorRate(normalizedExpected, normalizedActual), 4),
                result.TextBlocks.Select(RealOcrBlockEvidence.FromBlock).ToArray(),
                Error: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RealOcrCandidateSweep(
                candidate.Name,
                "failed",
                sourceFrame.Width,
                sourceFrame.Height,
                RecognizedText: string.Empty,
                NormalizedText: string.Empty,
                CharacterErrorRate: 1d,
                Blocks: Array.Empty<RealOcrBlockEvidence>(),
                Error: $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static CapturedFrame RenderSourceReferenceFrame(GoldenReferenceFixture fixture)
    {
        Exception? failure = null;
        CapturedFrame? frame = null;
        var thread = new Thread(() =>
        {
            try
            {
                frame = RenderSourceReferenceFrameOnStaThread(fixture);
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
            throw new InvalidOperationException("Failed to render generated OCR calibration source frame.", failure);
        }

        return frame ?? throw new InvalidOperationException("Generated OCR calibration source frame was not rendered.");
    }

    private static CapturedFrame RenderSourceReferenceFrameOnStaThread(GoldenReferenceFixture fixture)
    {
        const int sourceWidth = 240;
        const int sourceHeight = 240;
        const int scale = 2;
        const int width = sourceWidth * scale;
        const int height = sourceHeight * scale;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(scale, scale));
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(210, 210, 210)), null, new Rect(0, 0, sourceWidth, sourceHeight));
            context.DrawRectangle(
                Brushes.White,
                new Pen(Brushes.DimGray, 2),
                new Rect(fixture.BubbleBounds.X, fixture.BubbleBounds.Y, fixture.BubbleBounds.Width, fixture.BubbleBounds.Height));
            DrawSourceGlyphs(context, fixture, originX: 0, originY: 0, textBrush: Brushes.Black);
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        bitmap.CopyPixels(pixels, stride, 0);

        return new CapturedFrame(
            new CaptureRegion(0, 0, width, height),
            width,
            height,
            stride,
            "Bgra32",
            pixels,
            FixtureTime);
    }

    private static string? TryFindTessdataPath(
        IReadOnlyList<string> requiredTrainedData,
        out string unavailableReason)
    {
        var missingByCandidate = new List<string>();
        foreach (var candidatePath in CreateTessdataCandidatePaths())
        {
            if (!Directory.Exists(candidatePath))
            {
                missingByCandidate.Add($"{FormatTessdataLocation(candidatePath)}: directory not found");
                continue;
            }

            var missingFiles = requiredTrainedData
                .Where(fileName => !File.Exists(Path.Combine(candidatePath, fileName)))
                .ToArray();
            if (missingFiles.Length == 0)
            {
                unavailableReason = string.Empty;
                return candidatePath;
            }

            missingByCandidate.Add(
                $"{FormatTessdataLocation(candidatePath)}: missing {string.Join(", ", missingFiles)}");
        }

        unavailableReason = string.Join("; ", missingByCandidate);
        return null;
    }

    private static IReadOnlyList<string> CreateTessdataCandidatePaths()
    {
        var paths = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "tessdata"),
            Path.Combine(RepositoryRoot.Find(), "tessdata"),
        };
        var environmentPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            paths.Add(environmentPath.Trim());
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatTessdataLocation(string path)
    {
        var repositoryRoot = RepositoryRoot.Find();
        var relative = Path.GetRelativePath(repositoryRoot, path);
        if (!relative.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative))
        {
            return relative.Replace('\\', '/');
        }

        return "<external-tessdata>";
    }

    private static string GetRequiredTrainedDataFileName(GoldenReferenceFixture fixture)
    {
        if (fixture.SourceOrientation is OcrOrientationMode.Vertical
            && fixture.SourceLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "chi_sim_vert.traineddata";
        }

        if (fixture.SourceOrientation is OcrOrientationMode.Vertical
            && fixture.SourceLanguage.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return "jpn_vert.traineddata";
        }

        return "eng.traineddata";
    }

    private static GeometryBounds OffsetAndInflate(
        GeometryBounds bounds,
        int offsetX,
        int offsetY,
        int inflation)
    {
        return new GeometryBounds(
            bounds.X + offsetX - inflation,
            bounds.Y + offsetY - inflation,
            bounds.Width + inflation * 2,
            bounds.Height + inflation * 2);
    }

    private static bool IsSameSequence(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        return actual
            .Select(Normalize)
            .SequenceEqual(expected.Select(Normalize), StringComparer.Ordinal);
    }

    private static double ClampScore(double score)
    {
        return Math.Clamp(score, 0d, 1d);
    }

    private static OcrZone CreateZone(TranslationGroupingMode groupingMode, double mergeDistancePercent)
    {
        return new OcrZone
        {
            Id = "zone-a",
            Name = "Golden reference zone",
            AbsoluteBounds = new AbsoluteRectangle(0, 0, 220, 80),
            RelativeBounds = new RelativeRectangle(0, 0, 1, 1),
            TranslationGroupingMode = groupingMode,
            TextGrouping = new OcrZoneTextGroupingSettings
            {
                MergeDistancePercent = mergeDistancePercent,
            },
        };
    }

    private static CapturedFrame CreateSolidFrame(CaptureRegion region, byte value)
    {
        var stride = checked(region.Width * 4);
        var pixels = new byte[checked(stride * region.Height)];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = value;
            pixels[index + 1] = value;
            pixels[index + 2] = value;
            pixels[index + 3] = byte.MaxValue;
        }

        return new CapturedFrame(region, region.Width, region.Height, stride, "Bgra32", pixels, FixtureTime);
    }

    private static void FillRectangle(
        byte[] pixels,
        int stride,
        int x,
        int y,
        int width,
        int height,
        byte value)
    {
        FillRectangle(pixels, stride, x, y, width, height, value, value, value);
    }

    private static void FillRectangle(
        byte[] pixels,
        int stride,
        int x,
        int y,
        int width,
        int height,
        byte red,
        byte green,
        byte blue)
    {
        for (var row = y; row < y + height; row++)
        {
            var rowOffset = row * stride;
            for (var column = x; column < x + width; column++)
            {
                var offset = rowOffset + column * 4;
                pixels[offset] = blue;
                pixels[offset + 1] = green;
                pixels[offset + 2] = red;
            }
        }
    }

    private static void DrawRectangleOutline(
        byte[] pixels,
        int stride,
        int x,
        int y,
        int width,
        int height,
        byte red,
        byte green,
        byte blue,
        int thickness)
    {
        FillRectangle(pixels, stride, x, y, width, thickness, red, green, blue);
        FillRectangle(pixels, stride, x, y + height - thickness, width, thickness, red, green, blue);
        FillRectangle(pixels, stride, x, y, thickness, height, red, green, blue);
        FillRectangle(pixels, stride, x + width - thickness, y, thickness, height, red, green, blue);
    }

    private static void SavePng(byte[] pixels, int width, int height, int stride, string path)
    {
        var image = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        SavePng(image, path);
    }

    private static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static double CalculateCharacterErrorRate(string expected, string actual)
    {
        var normalizedExpected = Normalize(expected);
        var normalizedActual = Normalize(actual);
        if (normalizedExpected.Length == 0)
        {
            return normalizedActual.Length == 0 ? 0d : 1d;
        }

        return CalculateLevenshteinDistance(normalizedExpected, normalizedActual) / (double)normalizedExpected.Length;
    }

    private static int CalculateLevenshteinDistance(string left, string right)
    {
        var distances = new int[left.Length + 1, right.Length + 1];
        for (var index = 0; index <= left.Length; index++)
        {
            distances[index, 0] = index;
        }

        for (var index = 0; index <= right.Length; index++)
        {
            distances[0, index] = index;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                distances[leftIndex, rightIndex] = Math.Min(
                    Math.Min(
                        distances[leftIndex - 1, rightIndex] + 1,
                        distances[leftIndex, rightIndex - 1] + 1),
                    distances[leftIndex - 1, rightIndex - 1] + substitutionCost);
            }
        }

        return distances[left.Length, right.Length];
    }

    private static string Normalize(string value)
    {
        return OcrTextNormalizer.NormalizeForComparison(value).ToUpperInvariant();
    }

    private sealed record GoldenReferenceFixture(
        string Id,
        string CaseType,
        string OriginalText,
        string SourceLanguage,
        OcrOrientationMode SourceOrientation,
        IReadOnlyList<string> ApprovedReadingOrder,
        IReadOnlyList<GeometryBounds> RawSourceBounds,
        IReadOnlyList<GoldenSemanticGroup> SemanticGroups,
        string ApprovedTranslation,
        GeometryBounds BubbleBounds,
        GeometryBounds ApprovedOverlayBounds,
        double RequiredGroupingMergeDistancePercent,
        IReadOnlyList<GeometryBounds> ForbiddenRegions);

    private sealed record GoldenSemanticGroup(
        int GroupId,
        string ApprovedSourceText,
        GeometryBounds SourceBounds,
        IReadOnlyList<int> RawSourceIndexes,
        IReadOnlyList<int> MaskSourceIndexes);

    private sealed record RenderedTextFixture(string ExpectedText, CapturedFrame Frame, BoundingBox TextBounds);

    private sealed record OcrPresetCandidate(
        string Name,
        OcrPreprocessingSettings Settings,
        Func<CapturedFrame, OcrResult> Recognize);

    private sealed record OcrCalibrationResult(
        OcrPresetCandidate Preset,
        OcrResult Result,
        double CharacterErrorRate);

    private sealed record RealOcrPresetCandidate(
        string Name,
        OcrPreprocessingSettings Settings);

    private sealed record RealOcrSweepPackage(
        int SchemaVersion,
        bool TestOnly,
        DateTimeOffset GeneratedAtUtc,
        string Status,
        string? TessdataLocation,
        IReadOnlyList<string> RequiredTrainedData,
        IReadOnlyList<string> CandidateTessdataLocations,
        IReadOnlyList<string> SetupInstructions,
        IReadOnlyList<RealOcrSourceFrameEvidence> SourceFrames,
        string? UnavailableReason,
        IReadOnlyList<RealOcrFixtureSweep> Fixtures);

    private sealed record RealOcrSourceFrameEvidence(
        string FixtureId,
        string Path,
        int Width,
        int Height,
        string SourceLanguage,
        string SourceOrientation,
        string ExpectedText);

    private sealed record RealOcrFixtureSweep(
        string FixtureId,
        string SourceLanguage,
        string SourceOrientation,
        string ExpectedText,
        IReadOnlyList<RealOcrCandidateSweep> Candidates);

    private sealed record RealOcrCandidateSweep(
        string PresetName,
        string Status,
        int InputWidth,
        int InputHeight,
        string RecognizedText,
        string NormalizedText,
        double CharacterErrorRate,
        IReadOnlyList<RealOcrBlockEvidence> Blocks,
        string? Error);

    private sealed record RealOcrBlockEvidence(
        string Text,
        GeometryBounds Bounds)
    {
        public static RealOcrBlockEvidence FromBlock(OcrTextBlock block)
        {
            return new RealOcrBlockEvidence(
                block.Text,
                new GeometryBounds(
                    block.Bounds.X,
                    block.Bounds.Y,
                    block.Bounds.Width,
                    block.Bounds.Height));
        }
    }

    private sealed record CalibrationCandidate(
        string CandidateId,
        string OcrPresetName,
        double ExpectedOcrCharacterErrorRate,
        bool ReverseReadingOrder,
        bool DropLastReadingUnit,
        double GroupingMergeDistancePercent,
        bool UseRawMaskSource,
        int MaskPadding,
        int OverlayOffsetX,
        int OverlayOffsetY,
        int OverlayInflation);

    private sealed record CalibrationOcrPreset(
        string Name,
        double ExpectedCharacterErrorRate,
        bool ReverseReadingOrder,
        bool DropLastReadingUnit);

    private sealed record CalibrationMaskOption(
        string Name,
        bool UseRawMaskSource,
        int Padding);

    private sealed record CalibrationOverlayOption(
        string Name,
        int OffsetX,
        int OffsetY,
        int Inflation);

    private sealed record CalibrationScorecard(
        string BestCandidateId,
        IReadOnlyList<CalibrationCandidateScore> Candidates);

    private sealed record CalibrationCandidateScore(
        string CandidateId,
        CalibrationCandidate Parameters,
        double TotalScore,
        bool IsSelected,
        IReadOnlyList<CalibrationFixtureScore> FixtureScores);

    private sealed record CalibrationFixtureScore(
        string FixtureId,
        double OcrScore,
        double ReadingOrderScore,
        double GroupingScore,
        double OverlayScore,
        double MaskScore,
        double TotalScore,
        double OverlayCenterDistance,
        int OverlayForbiddenIntersections,
        int MaskForbiddenIntersections,
        GeometryBounds OverlayBounds,
        IReadOnlyList<GeometryBounds> MaskBounds,
        IReadOnlyList<string> TranslationRequestTexts,
        IReadOnlyList<string> Violations);

    private sealed record TranslationMeaningChecklist(
        IReadOnlyList<string> RequiredFragments,
        IReadOnlyList<string> OrderedFragments);

    private interface ITranslationMeaningChecker
    {
        bool IsAcceptable(string candidateTranslation, TranslationMeaningChecklist checklist);
    }

    private sealed class DeterministicTranslationMeaningChecker : ITranslationMeaningChecker
    {
        public bool IsAcceptable(string candidateTranslation, TranslationMeaningChecklist checklist)
        {
            var normalizedCandidate = Normalize(candidateTranslation);
            if (checklist.RequiredFragments.Any(fragment => !normalizedCandidate.Contains(Normalize(fragment), StringComparison.Ordinal)))
            {
                return false;
            }

            var searchStart = 0;
            foreach (var fragment in checklist.OrderedFragments)
            {
                var normalizedFragment = Normalize(fragment);
                var index = normalizedCandidate.IndexOf(normalizedFragment, searchStart, StringComparison.Ordinal);
                if (index < 0)
                {
                    return false;
                }

                searchStart = index + normalizedFragment.Length;
            }

            return true;
        }
    }

    private readonly record struct GeometryBounds(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;

        public int Bottom => Y + Height;

        public double CenterX => X + Width / 2d;

        public double CenterY => Y + Height / 2d;

        public static GeometryBounds FromOverlayText(OverlayTextItem item)
        {
            return new GeometryBounds(item.X, item.Y, item.Width, item.Height);
        }

        public static GeometryBounds FromOverlayMask(OverlayMaskItem item)
        {
            return new GeometryBounds(item.X, item.Y, item.Width, item.Height);
        }

        public BoundingBox ToBoundingBox()
        {
            return new BoundingBox(X, Y, Width, Height);
        }

        public bool Contains(GeometryBounds other)
        {
            return other.X >= X
                && other.Y >= Y
                && other.Right <= Right
                && other.Bottom <= Bottom;
        }

        public bool Intersects(GeometryBounds other)
        {
            return X < other.Right
                && Right > other.X
                && Y < other.Bottom
                && Bottom > other.Y;
        }

        public double CenterDistanceTo(GeometryBounds other)
        {
            var dx = CenterX - other.CenterX;
            var dy = CenterY - other.CenterY;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    private sealed record GoldenDiagnosticsPackage(
        int SchemaVersion,
        string FixtureId,
        string CaseType,
        IReadOnlyList<string> SourceOcr,
        IReadOnlyList<string> TranslationSourceOcr,
        IReadOnlyList<int> MaskSourceOcr,
        object OverlayGeometry,
        string SelectedPreset,
        string ProviderDiagnostic,
        string RedactedCredential);

    private enum PanelLayer
    {
        Source,
        Overlay,
        Forbidden,
    }
}
