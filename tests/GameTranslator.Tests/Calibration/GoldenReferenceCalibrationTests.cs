using System.IO;
using System.Globalization;
using System.Reflection;
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
using TesseractOCR.Enums;
using TesseractEngine = TesseractOCR.Engine;
using TesseractPage = TesseractOCR.Page;
using TesseractRect = TesseractOCR.Rect;
using PixImage = TesseractOCR.Pix.Image;

namespace GameTranslator.Tests.Calibration;

public sealed class GoldenReferenceCalibrationTests
{
    private static readonly DateTimeOffset FixtureTime = new(2026, 6, 30, 9, 0, 0, TimeSpan.Zero);
    private const int VerticalRightOverflowDampeningMinSemanticWidth = 100;
    private const string BookPageHorizontalFixtureId = "book-page-horizontal-lines";
    private const string MixedOrientationFixtureId = "mixed-orientation-frame";
    private const string MixedWorstCaseFixtureId = "mixed-worst-case-frame";
    private const string VerticalLongTranslationFitFixtureId = "vertical-long-translation-fit-frame";
    private const double VerticalLongTranslationBaseFontSizePt = 14d;
    private const double VerticalLongTranslationMinFontSizePt = 10d;
    private const double VerticalLongTranslationMaxOverlayAreaRatio = 1.10d;
    private const byte EvidenceOverlayFillRed = 24;
    private const byte EvidenceOverlayFillGreen = 94;
    private const byte EvidenceOverlayFillBlue = 165;
    private const byte EvidenceOverlayOutlineRed = 219;
    private const byte EvidenceOverlayOutlineGreen = 234;
    private const byte EvidenceOverlayOutlineBlue = 254;

    [Fact]
    public void FixtureManifest_WhenBuilt_StoresGroundTruthTextSemanticGeometryAndForbiddenRegions()
    {
        var fixtures = CreateGoldenFixtures();

        Assert.Equal(
            new[]
            {
                "manga_vertical_cjk",
                "manga_vertical_japanese",
                "manga_vertical_japanese_two_column",
                "manga_vertical_japanese_three_column",
                "book_page_horizontal",
                "thai_horizontal_dialogue",
                "thai_multiline_dialogue",
                "thai_three_line_dialogue",
                "plain_ui_horizontal",
            },
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

        var japaneseTwoColumn = fixtures.Single(fixture => fixture.Id == "vertical-japanese-two-column-prompt");
        Assert.Equal("ja-JP", japaneseTwoColumn.SourceLanguage);
        Assert.Equal(OcrOrientationMode.Vertical, japaneseTwoColumn.SourceOrientation);
        Assert.Equal(
            new[] { "\u30bb", "\u30fc", "\u30d6", "\u3057", "\u307e", "\u3059", "\u304b" },
            japaneseTwoColumn.ApprovedReadingOrder);
        var japaneseTwoColumnGroup = Assert.Single(japaneseTwoColumn.SemanticGroups);
        Assert.Equal("\u30bb \u30fc \u30d6 \u3057 \u307e \u3059 \u304b", japaneseTwoColumnGroup.ApprovedSourceText);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, japaneseTwoColumnGroup.RawSourceIndexes);
        Assert.Equal(7, japaneseTwoColumn.RawSourceBounds.Count);

        var japaneseThreeColumn = fixtures.Single(fixture => fixture.Id == "vertical-japanese-three-column-prompt");
        Assert.Equal("ja-JP", japaneseThreeColumn.SourceLanguage);
        Assert.Equal(OcrOrientationMode.Vertical, japaneseThreeColumn.SourceOrientation);
        Assert.Equal(
            new[] { "\u30bb", "\u30fc", "\u30d6", "\u3057", "\u307e", "\u3059", "\u304b", "\u306f", "\u3044" },
            japaneseThreeColumn.ApprovedReadingOrder);
        var japaneseThreeColumnGroup = Assert.Single(japaneseThreeColumn.SemanticGroups);
        Assert.Equal("\u30bb \u30fc \u30d6 \u3057 \u307e \u3059 \u304b \u306f \u3044", japaneseThreeColumnGroup.ApprovedSourceText);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, japaneseThreeColumnGroup.RawSourceIndexes);
        Assert.Equal(9, japaneseThreeColumn.RawSourceBounds.Count);

        var thai = fixtures.Single(fixture => fixture.Id == "thai-horizontal-hello");
        Assert.Equal("tha", thai.SourceLanguage);
        Assert.Equal(OcrOrientationMode.Horizontal, thai.SourceOrientation);
        Assert.Equal(new[] { "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35" }, thai.ApprovedReadingOrder);
        var thaiGroup = Assert.Single(thai.SemanticGroups);
        Assert.Equal("\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35", thaiGroup.ApprovedSourceText);
        Assert.Equal(new[] { 0 }, thaiGroup.RawSourceIndexes);

        var thaiMultiline = fixtures.Single(fixture => fixture.Id == "thai-multiline-dialogue");
        Assert.Equal("tha", thaiMultiline.SourceLanguage);
        Assert.Equal(OcrOrientationMode.Horizontal, thaiMultiline.SourceOrientation);
        Assert.Equal(
            new[] { "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35", "\u0e22\u0e34\u0e19\u0e14\u0e35\u0e15\u0e49\u0e2d\u0e19\u0e23\u0e31\u0e1a" },
            thaiMultiline.ApprovedReadingOrder);
        var thaiMultilineGroup = Assert.Single(thaiMultiline.SemanticGroups);
        Assert.Equal("\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35 \u0e22\u0e34\u0e19\u0e14\u0e35\u0e15\u0e49\u0e2d\u0e19\u0e23\u0e31\u0e1a", thaiMultilineGroup.ApprovedSourceText);
        Assert.Equal(new[] { 0, 1 }, thaiMultilineGroup.RawSourceIndexes);

        var thaiThreeLine = fixtures.Single(fixture => fixture.Id == "thai-three-line-dialogue");
        Assert.Equal("tha", thaiThreeLine.SourceLanguage);
        Assert.Equal(OcrOrientationMode.Horizontal, thaiThreeLine.SourceOrientation);
        Assert.Equal(
            new[] { "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35", "\u0e22\u0e34\u0e19\u0e14\u0e35\u0e15\u0e49\u0e2d\u0e19\u0e23\u0e31\u0e1a", "\u0e1e\u0e1a\u0e01\u0e31\u0e19\u0e43\u0e2b\u0e21\u0e48" },
            thaiThreeLine.ApprovedReadingOrder);
        var thaiThreeLineGroup = Assert.Single(thaiThreeLine.SemanticGroups);
        Assert.Equal("\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35 \u0e22\u0e34\u0e19\u0e14\u0e35\u0e15\u0e49\u0e2d\u0e19\u0e23\u0e31\u0e1a \u0e1e\u0e1a\u0e01\u0e31\u0e19\u0e43\u0e2b\u0e21\u0e48", thaiThreeLineGroup.ApprovedSourceText);
        Assert.Equal(new[] { 0, 1, 2 }, thaiThreeLineGroup.RawSourceIndexes);
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

        var translationSourceResult = TranslationTextGroupingService.CreateTranslationSourceResult(sourceResult, zone);
        var requestTexts = translationSourceResult.TextBlocks
            .Select(block => Normalize(block.Text))
            .ToArray();

        Assert.Equal(new[] { Normalize(approvedText) }, requestTexts);
        Assert.Equal(
            new BoundingBox(10, 10, 154, 20),
            Assert.Single(translationSourceResult.TextBlocks).Bounds);
        Assert.Equal(
            new[] { "\u044f\u0431\u043b\u043e\u043a\u043e", "\u042f \u0435\u043b" },
            sourceResult.TextBlocks.Select(block => block.Text));
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
            if (!IsOverlayHighCandidate(candidate))
            {
                Assert.NotEmpty(candidate.FixtureScores.SelectMany(fixtureScore => fixtureScore.Violations));
            }
        });

        var overlayHigh8 = scorecard.Candidates.Single(candidate =>
            candidate.CandidateId == "threshold-scale_merge-30_mask-raw-4_overlay-high-8");
        var overlayLineCount8 = scorecard.Candidates.Single(candidate =>
            candidate.CandidateId == "threshold-scale_merge-30_mask-raw-4_overlay-line-count-step-8");
        var overlayLineCount8XDamped = scorecard.Candidates.Single(candidate =>
            candidate.CandidateId == "threshold-scale_merge-30_mask-raw-4_overlay-line-count-step-8-x-half-right-overflow");
        var overlayHigh12 = scorecard.Candidates.Single(candidate =>
            candidate.CandidateId == "threshold-scale_merge-30_mask-raw-4_overlay-high-12");
        Assert.Equal(-8, overlayHigh8.Parameters.OverlayOffsetY);
        Assert.Equal(-8, overlayLineCount8.Parameters.OverlayOffsetY);
        Assert.True(overlayLineCount8.Parameters.UseLineCountOffset);
        Assert.Equal(-8, overlayLineCount8.Parameters.LineCountOffsetStepY);
        Assert.True(overlayLineCount8XDamped.Parameters.UseSemanticRightOverflowDampening);
        Assert.Equal(0.5d, overlayLineCount8XDamped.Parameters.SemanticRightOverflowDampeningRatio);
        Assert.Equal(
            VerticalRightOverflowDampeningMinSemanticWidth,
            overlayLineCount8XDamped.Parameters.SemanticRightOverflowDampeningMinVerticalGroupWidth);
        Assert.Equal(-12, overlayHigh12.Parameters.OverlayOffsetY);
        Assert.All(new[] { "vertical-cjk-basic-bubble", "vertical-japanese-save-prompt", "vertical-japanese-two-column-prompt", "vertical-japanese-three-column-prompt" }, fixtureId =>
        {
            var high8Fixture = overlayHigh8.FixtureScores.Single(score => score.FixtureId == fixtureId);
            var lineCountFixture = overlayLineCount8.FixtureScores.Single(score => score.FixtureId == fixtureId);
            var high12Fixture = overlayHigh12.FixtureScores.Single(score => score.FixtureId == fixtureId);
            Assert.Equal(8d, high8Fixture.OverlayCenterDistance);
            Assert.Equal(8d, lineCountFixture.OverlayCenterDistance);
            Assert.Equal(12d, high12Fixture.OverlayCenterDistance);
            Assert.DoesNotContain("overlay-intersects-forbidden-region", high8Fixture.Violations);
            Assert.DoesNotContain("overlay-intersects-forbidden-region", lineCountFixture.Violations);
            Assert.DoesNotContain("overlay-intersects-forbidden-region", high12Fixture.Violations);
        });
        Assert.Equal(
            0d,
            overlayLineCount8.FixtureScores
                .Single(score => score.FixtureId == "thai-horizontal-hello")
                .OverlayCenterDistance);
        Assert.Equal(
            0d,
            overlayLineCount8.FixtureScores
                .Single(score => score.FixtureId == "plain-ui-save-game")
                .OverlayCenterDistance);
        Assert.Equal(
            8d,
            overlayLineCount8.FixtureScores
                .Single(score => score.FixtureId == "thai-multiline-dialogue")
                .OverlayCenterDistance);
        Assert.Equal(
            16d,
            overlayLineCount8.FixtureScores
                .Single(score => score.FixtureId == "thai-three-line-dialogue")
                .OverlayCenterDistance);

        Assert.All(new[] { "vertical-cjk-basic-bubble", "vertical-japanese-save-prompt", "vertical-japanese-two-column-prompt" }, fixtureId =>
        {
            var lineCountFixture = overlayLineCount8.FixtureScores.Single(score => score.FixtureId == fixtureId);
            var dampedFixture = overlayLineCount8XDamped.FixtureScores.Single(score => score.FixtureId == fixtureId);
            Assert.Equal(lineCountFixture.OverlayBounds, dampedFixture.OverlayBounds);
            Assert.Equal(lineCountFixture.OverlayCenterDistance, dampedFixture.OverlayCenterDistance);
        });

        var japaneseThreeColumn = fixtures.Single(fixture => fixture.Id == "vertical-japanese-three-column-prompt");
        var japaneseThreeColumnSemanticBounds = GeometryBounds.Union(japaneseThreeColumn.SemanticGroups.Select(group => group.SourceBounds).ToArray());
        var japaneseThreeColumnLineCount = overlayLineCount8.FixtureScores.Single(score => score.FixtureId == japaneseThreeColumn.Id);
        var japaneseThreeColumnDamped = overlayLineCount8XDamped.FixtureScores.Single(score => score.FixtureId == japaneseThreeColumn.Id);
        Assert.True(japaneseThreeColumnSemanticBounds.Width >= VerticalRightOverflowDampeningMinSemanticWidth);
        Assert.Equal(10, japaneseThreeColumnLineCount.OverlayBounds.Right - japaneseThreeColumnSemanticBounds.Right);
        Assert.Equal(5, japaneseThreeColumnLineCount.OverlayBounds.X - japaneseThreeColumnDamped.OverlayBounds.X);
        Assert.Equal(5, japaneseThreeColumnDamped.OverlayBounds.Right - japaneseThreeColumnSemanticBounds.Right);
        Assert.DoesNotContain("overlay-intersects-forbidden-region", japaneseThreeColumnDamped.Violations);

        var bookPage = fixtures.Single(fixture => fixture.Id == BookPageHorizontalFixtureId);
        var bookPageSemanticBounds = GeometryBounds.Union(bookPage.SemanticGroups.Select(group => group.SourceBounds).ToArray());
        var bookPageDamped = overlayLineCount8XDamped.FixtureScores.Single(score => score.FixtureId == bookPage.Id);
        Assert.Equal(bookPageSemanticBounds.Y, bookPageDamped.OverlayBounds.Y);
        Assert.All(new[]
        {
            "vertical-japanese-three-column-prompt",
            BookPageHorizontalFixtureId,
            "thai-horizontal-hello",
            "thai-multiline-dialogue",
            "thai-three-line-dialogue",
            "plain-ui-save-game",
        }, fixtureId =>
        {
            var dampedFixture = overlayLineCount8XDamped.FixtureScores.Single(score => score.FixtureId == fixtureId);
            Assert.DoesNotContain("overlay-intersects-forbidden-region", dampedFixture.Violations);
            Assert.DoesNotContain("overlay-outside-reference-region", dampedFixture.Violations);
        });

        var artifactDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration");
        Directory.CreateDirectory(artifactDirectory);
        var scorecardPath = Path.Combine(artifactDirectory, "scorecard.json");
        var visualEvidencePath = Path.Combine(artifactDirectory, "candidate-evidence.png");
        var visualEvidenceCandidates = SelectVisualEvidenceCandidates(scorecard);
        SaveCalibrationScorecardEvidencePng(fixtures, scorecard, visualEvidencePath);

        var artifact = new
        {
            schemaVersion = 1,
            generatedAtUtc = FixtureTime,
            purpose = "test-only calibration parameter sweep; not production behavior",
            scorecard.BestCandidateId,
            visualEvidence = new[] { Path.GetFileName(visualEvidencePath) },
            visualEvidenceCellMap = CreateVisualEvidenceCellMap(fixtures, visualEvidenceCandidates),
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
        Assert.Contains("\"visualEvidenceCellMap\":", artifactJson, StringComparison.Ordinal);
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
            Assert.All(
                fixture.OriginalText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                line => Assert.Contains(line, manifestJson, StringComparison.Ordinal));
            Assert.Contains(fixture.ApprovedTranslation, manifestJson, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET", manifestJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TOKEN", manifestJson, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void MixedOrientationFixture_WhenBuilt_WritesIndependentGroupManifestAndContactSheet()
    {
        var fixture = CreateMixedOrientationFixture();
        var artifactDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration", fixture.Id);
        Directory.CreateDirectory(artifactDirectory);

        var contactSheetPath = Path.Combine(artifactDirectory, "contact-sheet.png");
        var manifestPath = Path.Combine(artifactDirectory, "manifest.json");
        SaveMixedOrientationContactSheetPng(fixture, contactSheetPath);
        var manifest = new
        {
            fixture.Id,
            fixture.CaseType,
            fixture.OriginalText,
            fixture.RawSourceBounds,
            fixture.SemanticGroups,
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

        Assert.Equal(MixedOrientationFixtureId, fixture.Id);
        Assert.Equal("mixed_orientation_frame", fixture.CaseType);
        Assert.Equal(3, fixture.SemanticGroups.Count);
        Assert.Equal(
            new[] { OcrOrientationMode.Vertical, OcrOrientationMode.Horizontal, OcrOrientationMode.Horizontal },
            fixture.SemanticGroups.Select(group => group.SourceOrientation));
        Assert.Equal(
            fixture.SemanticGroups.SelectMany(group => group.RawSourceIndexes).Count(),
            fixture.SemanticGroups.SelectMany(group => group.RawSourceIndexes).Distinct().Count());
        Assert.All(
            fixture.SemanticGroups.SelectMany(group => group.RawSourceIndexes),
            index => Assert.InRange(index, 0, fixture.RawSourceBounds.Count - 1));

        var compactVertical = fixture.SemanticGroups.Single(group => group.GroupId == 0);
        Assert.True(compactVertical.SourceBounds.Width < VerticalRightOverflowDampeningMinSemanticWidth);

        var multilineHorizontal = fixture.SemanticGroups.Single(group => group.GroupId == 2);
        Assert.Equal(
            2,
            CountHorizontalTextLines(multilineHorizontal.RawSourceIndexes.Select(index => fixture.RawSourceBounds[index]).ToArray()));
        Assert.Equal(multilineHorizontal.SourceBounds.Y, multilineHorizontal.ApprovedOverlayBounds.Y);
        Assert.All(fixture.SemanticGroups, group =>
        {
            Assert.True(group.BubbleBounds.Contains(group.ApprovedOverlayBounds));
            Assert.DoesNotContain(fixture.ForbiddenRegions, region => group.ApprovedOverlayBounds.Intersects(region));
        });

        Assert.True(File.Exists(contactSheetPath), contactSheetPath);
        Assert.True(new FileInfo(contactSheetPath).Length > 0);
        Assert.True(File.Exists(manifestPath), manifestPath);
        Assert.Contains(MixedOrientationFixtureId, manifestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", manifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", manifestJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MixedOrientationPlacementRules_WhenAppliedPerSemanticGroup_KeepsAdjustmentsIndependent()
    {
        var fixture = CreateMixedOrientationFixture();
        var candidate = CreateCalibrationCandidateMatrix().Single(item =>
            item.CandidateId == "threshold-scale_merge-30_mask-raw-4_overlay-line-count-step-8-x-half-right-overflow");
        var placements = fixture.SemanticGroups
            .Select(group => CreateMixedGroupPlacement(candidate, fixture, group))
            .ToArray();

        var compactVertical = placements.Single(placement => placement.GroupId == 0);
        Assert.Equal(OcrOrientationMode.Vertical, compactVertical.SourceOrientation);
        Assert.True(compactVertical.SemanticWidth < VerticalRightOverflowDampeningMinSemanticWidth);
        Assert.True(compactVertical.RightOverflowBeforeDampening > 0);
        Assert.Equal(0, compactVertical.SemanticRightOverflowOffsetX);
        Assert.Equal(fixture.SemanticGroups[0].ApprovedOverlayBounds, compactVertical.OverlayBounds);

        var singleLineHorizontal = placements.Single(placement => placement.GroupId == 1);
        Assert.Equal(1, singleLineHorizontal.LineCount);
        Assert.Equal(0, singleLineHorizontal.LineCountOffsetY);
        Assert.Equal(0, singleLineHorizontal.SemanticRightOverflowOffsetX);
        Assert.Equal(fixture.SemanticGroups[1].ApprovedOverlayBounds, singleLineHorizontal.OverlayBounds);

        var twoLineHorizontal = placements.Single(placement => placement.GroupId == 2);
        Assert.Equal(2, twoLineHorizontal.LineCount);
        Assert.Equal(-8, twoLineHorizontal.LineCountOffsetY);
        Assert.Equal(-6, twoLineHorizontal.SemanticTopAlignmentOffsetY);
        Assert.Equal(-6, twoLineHorizontal.SemanticRightOverflowOffsetX);
        Assert.True(twoLineHorizontal.AppliedSemanticTopAlignment);
        Assert.Equal(fixture.SemanticGroups[2].ApprovedOverlayBounds, twoLineHorizontal.OverlayBounds);
        Assert.All(placements, placement =>
        {
            var group = fixture.SemanticGroups.Single(item => item.GroupId == placement.GroupId);
            Assert.True(group.BubbleBounds.Contains(placement.OverlayBounds));
            Assert.DoesNotContain(fixture.ForbiddenRegions, region => placement.OverlayBounds.Intersects(region));
        });

        var artifactDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration", fixture.Id);
        Directory.CreateDirectory(artifactDirectory);
        var placementEvidencePath = Path.Combine(artifactDirectory, "group-placement-rules.json");
        var placementEvidenceJson = JsonSerializer.Serialize(
            new
            {
                fixture.Id,
                candidate.CandidateId,
                placements,
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
        File.WriteAllText(placementEvidencePath, placementEvidenceJson);

        Assert.True(File.Exists(placementEvidencePath), placementEvidencePath);
        Assert.DoesNotContain("SECRET", placementEvidenceJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", placementEvidenceJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MixedOrientationVisualEvidence_WhenGenerated_WritesNumberedPlacementPanels()
    {
        var fixture = CreateMixedOrientationFixture();
        var candidate = CreateCalibrationCandidateMatrix().Single(item =>
            item.CandidateId == "threshold-scale_merge-30_mask-raw-4_overlay-line-count-step-8-x-half-right-overflow");
        var placements = fixture.SemanticGroups
            .Select(group => CreateMixedGroupPlacement(candidate, fixture, group))
            .ToArray();

        var artifactDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration", fixture.Id);
        Directory.CreateDirectory(artifactDirectory);

        var evidencePath = Path.Combine(artifactDirectory, "placement-evidence.png");
        var cellMapPath = Path.Combine(artifactDirectory, "placement-evidence-map.json");
        SaveMixedPlacementEvidencePng(fixture, placements, evidencePath);
        var cellMap = CreateMixedPlacementEvidenceCellMap(fixture, placements);
        var cellMapJson = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                testOnly = true,
                fixtureId = fixture.Id,
                candidateId = candidate.CandidateId,
                evidenceImage = Path.GetFileName(evidencePath),
                cells = cellMap,
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
        File.WriteAllText(cellMapPath, cellMapJson);

        Assert.Equal(12, cellMap.Count);
        Assert.Equal(
            new[] { 4, 8, 12 },
            cellMap
                .Where(cell => string.Equals(cell.ColumnRole, "final-group-rules", StringComparison.Ordinal))
                .Select(cell => cell.Position));
        Assert.Contains(
            cellMap,
            cell => cell.Position == 12
                && cell.OverlayBounds.GetValueOrDefault() == fixture.SemanticGroups[2].ApprovedOverlayBounds);
        Assert.True(File.Exists(evidencePath), evidencePath);
        Assert.True(new FileInfo(evidencePath).Length > 0);
        Assert.True(File.Exists(cellMapPath), cellMapPath);
        Assert.Contains("final-group-rules", cellMapJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", cellMapJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", cellMapJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MixedWorstCaseVisualEvidence_WhenGenerated_WritesNumberedPlacementPanels()
    {
        var fixture = CreateMixedWorstCaseFixture();
        var candidate = CreateCalibrationCandidateMatrix().Single(item =>
            item.CandidateId == "threshold-scale_merge-30_mask-raw-4_overlay-line-count-step-8-x-half-right-overflow");
        var placements = fixture.SemanticGroups
            .Select(group => CreateMixedGroupPlacement(candidate, fixture, group))
            .ToArray();

        var artifactDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration", fixture.Id);
        Directory.CreateDirectory(artifactDirectory);

        var contactSheetPath = Path.Combine(artifactDirectory, "contact-sheet.png");
        var manifestPath = Path.Combine(artifactDirectory, "manifest.json");
        var groupRulesPath = Path.Combine(artifactDirectory, "group-placement-rules.json");
        var evidencePath = Path.Combine(artifactDirectory, "placement-evidence.png");
        var cellMapPath = Path.Combine(artifactDirectory, "placement-evidence-map.json");
        SaveMixedOrientationContactSheetPng(fixture, contactSheetPath);
        SaveMixedPlacementEvidencePng(fixture, placements, evidencePath);

        var manifestJson = JsonSerializer.Serialize(
            new
            {
                fixture.Id,
                fixture.CaseType,
                fixture.OriginalText,
                fixture.RawSourceBounds,
                fixture.SemanticGroups,
                fixture.ForbiddenRegions,
                generatedArtifacts = new[]
                {
                    Path.GetFileName(contactSheetPath),
                    Path.GetFileName(groupRulesPath),
                    Path.GetFileName(evidencePath),
                    Path.GetFileName(cellMapPath),
                },
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
        File.WriteAllText(manifestPath, manifestJson);

        var groupRulesJson = JsonSerializer.Serialize(
            new
            {
                fixture.Id,
                candidate.CandidateId,
                placements,
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
        File.WriteAllText(groupRulesPath, groupRulesJson);

        var cellMap = CreateMixedPlacementEvidenceCellMap(fixture, placements);
        var cellMapJson = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                testOnly = true,
                fixtureId = fixture.Id,
                candidateId = candidate.CandidateId,
                evidenceImage = Path.GetFileName(evidencePath),
                cells = cellMap,
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
        File.WriteAllText(cellMapPath, cellMapJson);

        Assert.Equal(MixedWorstCaseFixtureId, fixture.Id);
        Assert.Equal("mixed_worst_case_frame", fixture.CaseType);
        Assert.Equal(5, fixture.SemanticGroups.Count);
        Assert.Equal(
            new[] { OcrOrientationMode.Vertical, OcrOrientationMode.Vertical, OcrOrientationMode.Horizontal, OcrOrientationMode.Horizontal, OcrOrientationMode.Horizontal },
            fixture.SemanticGroups.Select(group => group.SourceOrientation));
        Assert.Equal(
            new[] { "ja-JP", "ja-JP", "tha", "en", "en" },
            fixture.SemanticGroups.Select(group => group.SourceLanguage));
        Assert.Equal(
            fixture.SemanticGroups.SelectMany(group => group.RawSourceIndexes).Count(),
            fixture.SemanticGroups.SelectMany(group => group.RawSourceIndexes).Distinct().Count());

        var compactVertical = placements.Single(placement => placement.GroupId == 0);
        Assert.True(compactVertical.SemanticWidth < VerticalRightOverflowDampeningMinSemanticWidth);
        Assert.True(compactVertical.RightOverflowBeforeDampening > 0);
        Assert.Equal(0, compactVertical.SemanticRightOverflowOffsetX);

        var wideVertical = placements.Single(placement => placement.GroupId == 1);
        Assert.True(wideVertical.SemanticWidth >= VerticalRightOverflowDampeningMinSemanticWidth);
        Assert.Equal(-3, wideVertical.SemanticRightOverflowOffsetX);

        var thaiDense = placements.Single(placement => placement.GroupId == 2);
        Assert.Equal(3, thaiDense.LineCount);
        Assert.Equal(-16, thaiDense.LineCountOffsetY);
        Assert.Equal(-8, thaiDense.SemanticRightOverflowOffsetX);
        Assert.Equal(-2, thaiDense.SemanticTopAlignmentOffsetY);
        Assert.True(thaiDense.AppliedSemanticTopAlignment);

        var latinUi = placements.Single(placement => placement.GroupId == 3);
        Assert.Equal(1, latinUi.LineCount);
        Assert.Equal(0, latinUi.LineCountOffsetY);
        Assert.Equal(-4, latinUi.SemanticRightOverflowOffsetX);

        var bookLines = placements.Single(placement => placement.GroupId == 4);
        Assert.Equal(2, bookLines.LineCount);
        Assert.Equal(-8, bookLines.LineCountOffsetY);
        Assert.Equal(-5, bookLines.SemanticRightOverflowOffsetX);
        Assert.Equal(-10, bookLines.SemanticTopAlignmentOffsetY);
        Assert.True(bookLines.AppliedSemanticTopAlignment);

        Assert.All(placements, placement =>
        {
            var group = fixture.SemanticGroups.Single(item => item.GroupId == placement.GroupId);
            Assert.Equal(group.ApprovedOverlayBounds, placement.OverlayBounds);
            Assert.True(group.BubbleBounds.Contains(placement.OverlayBounds));
            Assert.DoesNotContain(fixture.ForbiddenRegions, region => placement.OverlayBounds.Intersects(region));
        });
        Assert.Equal(20, cellMap.Count);
        Assert.Equal(
            new[] { 4, 8, 12, 16, 20 },
            cellMap
                .Where(cell => string.Equals(cell.ColumnRole, "final-group-rules", StringComparison.Ordinal))
                .Select(cell => cell.Position));
        Assert.All(new[] { contactSheetPath, manifestPath, groupRulesPath, evidencePath, cellMapPath }, path =>
        {
            Assert.True(File.Exists(path), path);
            Assert.True(new FileInfo(path).Length > 0);
        });
        Assert.DoesNotContain("SECRET", manifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", manifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", groupRulesJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", groupRulesJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", cellMapJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", cellMapJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerticalLongTranslationFitEvidence_WhenGenerated_WritesSimultaneousFinalOverlayPanel()
    {
        var fixture = CreateVerticalLongTranslationFitFixture();
        var fitEvidence = fixture.Cases
            .Select(CreateVerticalLongTranslationFitEvidence)
            .ToArray();

        var artifactDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration", fixture.Id);
        Directory.CreateDirectory(artifactDirectory);

        var manifestPath = Path.Combine(artifactDirectory, "manifest.json");
        var fitRulesPath = Path.Combine(artifactDirectory, "fit-rules.json");
        var evidencePath = Path.Combine(artifactDirectory, "placement-evidence.png");
        var cellMapPath = Path.Combine(artifactDirectory, "placement-evidence-map.json");
        SaveVerticalLongTranslationFitEvidencePng(fixture, fitEvidence, evidencePath);

        var manifestJson = JsonSerializer.Serialize(
            new
            {
                fixture.Id,
                fixture.CaseType,
                fixture.OriginalText,
                fixture.RawSourceBounds,
                fixture.Cases,
                fixture.ForbiddenRegions,
                generatedArtifacts = new[]
                {
                    Path.GetFileName(fitRulesPath),
                    Path.GetFileName(evidencePath),
                    Path.GetFileName(cellMapPath),
                },
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
        File.WriteAllText(manifestPath, manifestJson);

        var fitRulesJson = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                testOnly = true,
                fixture.Id,
                rule = "vertical-long-translation-fit",
                ruleSummary = "For vertical source groups, keep overlay area within 110% of semantic-group area, expand upward within semantic top/bottom first, keep 14pt when text fits, and shrink toward 10pt only when it still does not fit.",
                fitEvidence,
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
        File.WriteAllText(fitRulesPath, fitRulesJson);

        var cellMap = CreateVerticalLongTranslationFitEvidenceCellMap(fixture, fitEvidence);
        var cellMapJson = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                testOnly = true,
                fixtureId = fixture.Id,
                evidenceImage = Path.GetFileName(evidencePath),
                cells = cellMap,
            },
            new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
            });
        File.WriteAllText(cellMapPath, cellMapJson);

        Assert.Equal(VerticalLongTranslationFitFixtureId, fixture.Id);
        Assert.Equal("vertical_long_translation_fit_frame", fixture.CaseType);
        Assert.Equal(3, fixture.Cases.Count);
        Assert.All(fixture.Cases, item => Assert.Equal(OcrOrientationMode.Vertical, item.SourceOrientation));
        Assert.Equal(
            fixture.Cases.SelectMany(item => item.RawSourceIndexes).Count(),
            fixture.Cases.SelectMany(item => item.RawSourceIndexes).Distinct().Count());

        var shortFit = fitEvidence.Single(item => item.GroupId == 0);
        Assert.False(shortFit.ExpandedUpward);
        Assert.False(shortFit.WasShrunk);
        Assert.Equal(shortFit.InitialOverlayBounds, shortFit.FinalOverlayBounds);
        Assert.Equal(VerticalLongTranslationBaseFontSizePt, shortFit.FittedFontSizePt);

        var mediumFit = fitEvidence.Single(item => item.GroupId == 1);
        Assert.True(mediumFit.ExpandedUpward);
        Assert.False(mediumFit.WasShrunk);
        Assert.True(mediumFit.FinalOverlayBounds.Y < mediumFit.InitialOverlayBounds.Y);
        Assert.Equal(mediumFit.InitialOverlayBounds.Bottom, mediumFit.FinalOverlayBounds.Bottom);
        Assert.Equal(VerticalLongTranslationBaseFontSizePt, mediumFit.FittedFontSizePt);

        var longFit = fitEvidence.Single(item => item.GroupId == 2);
        Assert.True(longFit.ExpandedUpward);
        Assert.True(longFit.ReachedSemanticTop);
        Assert.True(longFit.ReachedSemanticBottom);
        Assert.False(longFit.WasShrunk);
        Assert.Equal(1d, longFit.FontScale);
        Assert.Equal(VerticalLongTranslationBaseFontSizePt, longFit.FittedFontSizePt);
        Assert.True(longFit.TextFitsAtBaseFontAfterExpansion);

        Assert.All(fitEvidence, item =>
        {
            Assert.True(item.FinalOverlayBounds.Y >= item.SemanticBounds.Y);
            Assert.True(item.FinalOverlayBounds.Bottom <= item.SemanticBounds.Bottom);
            Assert.Equal(item.InitialOverlayBounds.Width, item.FinalOverlayBounds.Width);
            Assert.Equal(0, item.WidthExpansionPixels);
            Assert.True(item.WidthExpansionLocked);
            Assert.Equal(VerticalLongTranslationBaseFontSizePt, item.BaseFontSizePt);
            Assert.Equal(VerticalLongTranslationMinFontSizePt, item.MinFontSizePt);
            Assert.InRange(item.FittedFontSizePt, item.MinFontSizePt, item.BaseFontSizePt);
            Assert.True(item.TextFitsAfterFontReduction);
            Assert.True(item.FinalOverlayArea <= item.MaxOverlayArea);
            Assert.Equal(VerticalLongTranslationMaxOverlayAreaRatio, item.MaxOverlayAreaRatio);
            Assert.True(fixture.Cases.Single(@case => @case.GroupId == item.GroupId).BubbleBounds.Contains(item.FinalOverlayBounds));
            Assert.DoesNotContain(fixture.ForbiddenRegions, region => item.FinalOverlayBounds.Intersects(region));
        });
        Assert.Equal(4, cellMap.Count);
        Assert.Equal(
            new[] { 4 },
            cellMap
                .Where(cell => string.Equals(cell.ColumnRole, "final-simultaneous-fit", StringComparison.Ordinal))
                .Select(cell => cell.Position));
        Assert.All(new[] { manifestPath, fitRulesPath, evidencePath, cellMapPath }, path =>
        {
            Assert.True(File.Exists(path), path);
            Assert.True(new FileInfo(path).Length > 0);
        });
        Assert.DoesNotContain("SECRET", manifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", manifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", fitRulesJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", fitRulesJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET", cellMapJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOKEN", cellMapJson, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<GoldenReferenceFixture> CreateGoldenFixtures()
    {
        return new[]
        {
            CreateVerticalCjkFixture(),
            CreateVerticalJapaneseFixture(),
            CreateVerticalJapaneseTwoColumnFixture(),
            CreateVerticalJapaneseThreeColumnFixture(),
            CreateBookPageFixture(),
            CreateThaiHorizontalFixture(),
            CreateThaiMultilineFixture(),
            CreateThaiThreeLineFixture(),
            CreatePlainTextFixture(),
        };
    }

    private static MixedOrientationFixture CreateMixedOrientationFixture()
    {
        return new MixedOrientationFixture(
            Id: MixedOrientationFixtureId,
            CaseType: "mixed_orientation_frame",
            OriginalText: "\u30bb\u30fc\u30d6\u3057\u307e\nSAVE GAME\nTHE OLD CITY\nWAS QUIET",
            RawSourceBounds: new[]
            {
                new GeometryBounds(54, 36, 20, 22),
                new GeometryBounds(54, 61, 20, 22),
                new GeometryBounds(54, 86, 20, 22),
                new GeometryBounds(54, 111, 20, 22),
                new GeometryBounds(54, 128, 20, 22),
                new GeometryBounds(130, 42, 48, 18),
                new GeometryBounds(184, 42, 46, 18),
                new GeometryBounds(50, 174, 122, 18),
                new GeometryBounds(50, 202, 106, 18),
            },
            SemanticGroups: new[]
            {
                new MixedSemanticGroup(
                    GroupId: 0,
                    Name: "compact-vertical-japanese",
                    SourceLanguage: "ja-JP",
                    SourceOrientation: OcrOrientationMode.Vertical,
                    ApprovedReadingOrder: new[] { "\u30bb", "\u30fc", "\u30d6", "\u3057", "\u307e" },
                    ApprovedSourceText: "\u30bb \u30fc \u30d6 \u3057 \u307e",
                    ApprovedTranslation: "\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c",
                    SourceBounds: new GeometryBounds(48, 30, 34, 126),
                    CenteredOverlayBounds: new GeometryBounds(34, 60, 60, 72),
                    ApprovedOverlayBounds: new GeometryBounds(34, 60, 60, 72),
                    BubbleBounds: new GeometryBounds(28, 20, 72, 142),
                    RawSourceIndexes: new[] { 0, 1, 2, 3, 4 },
                    MaskSourceIndexes: new[] { 0, 1, 2, 3, 4 }),
                new MixedSemanticGroup(
                    GroupId: 1,
                    Name: "single-line-horizontal-ui",
                    SourceLanguage: "en",
                    SourceOrientation: OcrOrientationMode.Horizontal,
                    ApprovedReadingOrder: new[] { "SAVE", "GAME" },
                    ApprovedSourceText: "SAVE GAME",
                    ApprovedTranslation: "\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c",
                    SourceBounds: new GeometryBounds(126, 36, 108, 30),
                    CenteredOverlayBounds: new GeometryBounds(126, 38, 104, 38),
                    ApprovedOverlayBounds: new GeometryBounds(126, 38, 104, 38),
                    BubbleBounds: new GeometryBounds(116, 24, 118, 58),
                    RawSourceIndexes: new[] { 5, 6 },
                    MaskSourceIndexes: new[] { 5, 6 }),
                new MixedSemanticGroup(
                    GroupId: 2,
                    Name: "two-line-horizontal-book",
                    SourceLanguage: "en",
                    SourceOrientation: OcrOrientationMode.Horizontal,
                    ApprovedReadingOrder: new[] { "THE OLD CITY", "WAS QUIET" },
                    ApprovedSourceText: "THE OLD CITY WAS QUIET",
                    ApprovedTranslation: "\u0421\u0442\u0430\u0440\u044b\u0439 \u0433\u043e\u0440\u043e\u0434 \u0431\u044b\u043b \u0442\u0438\u0445\u0438\u043c",
                    SourceBounds: new GeometryBounds(44, 168, 136, 58),
                    CenteredOverlayBounds: new GeometryBounds(48, 182, 144, 58),
                    ApprovedOverlayBounds: new GeometryBounds(42, 168, 144, 58),
                    BubbleBounds: new GeometryBounds(32, 156, 180, 78),
                    RawSourceIndexes: new[] { 7, 8 },
                    MaskSourceIndexes: new[] { 7, 8 }),
            },
            ForbiddenRegions: new[]
            {
                new GeometryBounds(0, 0, 24, 24),
                new GeometryBounds(214, 112, 20, 84),
                new GeometryBounds(198, 222, 34, 18),
            });
    }

    private static MixedOrientationFixture CreateMixedWorstCaseFixture()
    {
        return new MixedOrientationFixture(
            Id: MixedWorstCaseFixtureId,
            CaseType: "mixed_worst_case_frame",
            OriginalText: "\u30bb\u30fc\u30d6\u3057\n\u306f\u3044\n\u0e1e\u0e23\u0e49\u0e2d\u0e21\u0e41\u0e25\u0e49\u0e27\nOPTIONS MENU\nTHE OLD CITY\nWAS QUIET",
            RawSourceBounds: new[]
            {
                new GeometryBounds(38, 26, 18, 22),
                new GeometryBounds(38, 51, 18, 22),
                new GeometryBounds(38, 76, 18, 22),
                new GeometryBounds(38, 101, 18, 22),
                new GeometryBounds(170, 24, 18, 20),
                new GeometryBounds(170, 48, 18, 20),
                new GeometryBounds(170, 72, 18, 20),
                new GeometryBounds(132, 24, 18, 20),
                new GeometryBounds(132, 48, 18, 20),
                new GeometryBounds(132, 72, 18, 20),
                new GeometryBounds(94, 24, 18, 20),
                new GeometryBounds(94, 48, 18, 20),
                new GeometryBounds(24, 158, 82, 16),
                new GeometryBounds(24, 180, 104, 16),
                new GeometryBounds(24, 202, 88, 16),
                new GeometryBounds(150, 120, 38, 16),
                new GeometryBounds(192, 120, 34, 16),
                new GeometryBounds(152, 164, 70, 16),
                new GeometryBounds(152, 188, 62, 16),
            },
            SemanticGroups: new[]
            {
                new MixedSemanticGroup(
                    GroupId: 0,
                    Name: "compact-vertical-japanese",
                    SourceLanguage: "ja-JP",
                    SourceOrientation: OcrOrientationMode.Vertical,
                    ApprovedReadingOrder: new[] { "\u30bb", "\u30fc", "\u30d6", "\u3057" },
                    ApprovedSourceText: "\u30bb \u30fc \u30d6 \u3057",
                    ApprovedTranslation: "\u0421\u043e\u0445\u0440.",
                    SourceBounds: new GeometryBounds(32, 20, 30, 112),
                    CenteredOverlayBounds: new GeometryBounds(20, 58, 58, 66),
                    ApprovedOverlayBounds: new GeometryBounds(20, 58, 58, 66),
                    BubbleBounds: new GeometryBounds(12, 12, 72, 128),
                    RawSourceIndexes: new[] { 0, 1, 2, 3 },
                    MaskSourceIndexes: new[] { 0, 1, 2, 3 }),
                new MixedSemanticGroup(
                    GroupId: 1,
                    Name: "wide-vertical-japanese-multicolumn",
                    SourceLanguage: "ja-JP",
                    SourceOrientation: OcrOrientationMode.Vertical,
                    ApprovedReadingOrder: new[] { "\u306f", "\u3044", "\u30bb", "\u30fc", "\u30d6", "\u3057", "\u307e", "\u3059" },
                    ApprovedSourceText: "\u306f \u3044 \u30bb \u30fc \u30d6 \u3057 \u307e \u3059",
                    ApprovedTranslation: "\u0414\u0430, \u0441\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c",
                    SourceBounds: new GeometryBounds(88, 18, 108, 82),
                    CenteredOverlayBounds: new GeometryBounds(89, 36, 112, 48),
                    ApprovedOverlayBounds: new GeometryBounds(86, 36, 112, 48),
                    BubbleBounds: new GeometryBounds(76, 10, 146, 104),
                    RawSourceIndexes: new[] { 4, 5, 6, 7, 8, 9, 10, 11 },
                    MaskSourceIndexes: new[] { 4, 5, 6, 7, 8, 9, 10, 11 }),
                new MixedSemanticGroup(
                    GroupId: 2,
                    Name: "dense-thai-three-line-dialogue",
                    SourceLanguage: "tha",
                    SourceOrientation: OcrOrientationMode.Horizontal,
                    ApprovedReadingOrder: new[] { "\u0e1e\u0e23\u0e49\u0e2d\u0e21", "\u0e41\u0e25\u0e49\u0e27", "\u0e44\u0e1b\u0e01\u0e31\u0e19" },
                    ApprovedSourceText: "\u0e1e\u0e23\u0e49\u0e2d\u0e21 \u0e41\u0e25\u0e49\u0e27 \u0e44\u0e1b\u0e01\u0e31\u0e19",
                    ApprovedTranslation: "\u0413\u043e\u0442\u043e\u0432\u043e, \u0438\u0434\u0435\u043c",
                    SourceBounds: new GeometryBounds(16, 152, 116, 72),
                    CenteredOverlayBounds: new GeometryBounds(20, 170, 128, 66),
                    ApprovedOverlayBounds: new GeometryBounds(12, 152, 128, 66),
                    BubbleBounds: new GeometryBounds(8, 146, 136, 86),
                    RawSourceIndexes: new[] { 12, 13, 14 },
                    MaskSourceIndexes: new[] { 12, 13, 14 }),
                new MixedSemanticGroup(
                    GroupId: 3,
                    Name: "latin-ui-single-line",
                    SourceLanguage: "en",
                    SourceOrientation: OcrOrientationMode.Horizontal,
                    ApprovedReadingOrder: new[] { "OPTIONS", "MENU" },
                    ApprovedSourceText: "OPTIONS MENU",
                    ApprovedTranslation: "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438",
                    SourceBounds: new GeometryBounds(146, 116, 82, 26),
                    CenteredOverlayBounds: new GeometryBounds(142, 119, 94, 34),
                    ApprovedOverlayBounds: new GeometryBounds(138, 119, 94, 34),
                    BubbleBounds: new GeometryBounds(136, 110, 100, 48),
                    RawSourceIndexes: new[] { 15, 16 },
                    MaskSourceIndexes: new[] { 15, 16 }),
                new MixedSemanticGroup(
                    GroupId: 4,
                    Name: "book-two-line-horizontal",
                    SourceLanguage: "en",
                    SourceOrientation: OcrOrientationMode.Horizontal,
                    ApprovedReadingOrder: new[] { "THE OLD CITY", "WAS QUIET" },
                    ApprovedSourceText: "THE OLD CITY WAS QUIET",
                    ApprovedTranslation: "\u0413\u043e\u0440\u043e\u0434 \u0431\u044b\u043b \u0442\u0438\u0445",
                    SourceBounds: new GeometryBounds(146, 158, 80, 54),
                    CenteredOverlayBounds: new GeometryBounds(144, 176, 92, 54),
                    ApprovedOverlayBounds: new GeometryBounds(139, 158, 92, 54),
                    BubbleBounds: new GeometryBounds(136, 150, 100, 72),
                    RawSourceIndexes: new[] { 17, 18 },
                    MaskSourceIndexes: new[] { 17, 18 }),
            },
            ForbiddenRegions: new[]
            {
                new GeometryBounds(0, 0, 14, 14),
                new GeometryBounds(224, 66, 10, 40),
                new GeometryBounds(234, 120, 6, 28),
                new GeometryBounds(232, 212, 8, 20),
            });
    }

    private static VerticalLongTranslationFitFixture CreateVerticalLongTranslationFitFixture()
    {
        return new VerticalLongTranslationFitFixture(
            Id: VerticalLongTranslationFitFixtureId,
            CaseType: "vertical_long_translation_fit_frame",
            OriginalText: "\u306f\u3044\n\u30ed\u30b0\u3092\u958b\u304f\n\u8a73\u7d30\u3092\u78ba\u8a8d\u3057\u307e\u3059",
            RawSourceBounds: new[]
            {
                new GeometryBounds(40, 34, 18, 20),
                new GeometryBounds(40, 58, 18, 20),
                new GeometryBounds(40, 82, 18, 20),
                new GeometryBounds(190, 32, 16, 20),
                new GeometryBounds(190, 56, 16, 20),
                new GeometryBounds(190, 80, 16, 20),
                new GeometryBounds(160, 32, 16, 20),
                new GeometryBounds(160, 56, 16, 20),
                new GeometryBounds(160, 80, 16, 20),
                new GeometryBounds(160, 104, 16, 20),
                new GeometryBounds(102, 118, 16, 20),
                new GeometryBounds(102, 142, 16, 20),
                new GeometryBounds(102, 166, 16, 20),
                new GeometryBounds(102, 190, 16, 20),
                new GeometryBounds(76, 118, 16, 20),
                new GeometryBounds(76, 142, 16, 20),
                new GeometryBounds(76, 166, 16, 20),
                new GeometryBounds(76, 190, 16, 20),
            },
            Cases: new[]
            {
                new VerticalLongTranslationFitCase(
                    GroupId: 0,
                    Name: "short-vertical-translation",
                    TranslationLengthClass: "short",
                    SourceLanguage: "ja-JP",
                    SourceOrientation: OcrOrientationMode.Vertical,
                    ApprovedReadingOrder: new[] { "\u306f", "\u3044", "\u3002" },
                    ApprovedSourceText: "\u306f \u3044 \u3002",
                    ApprovedTranslation: "\u041e\u041a",
                    SourceBounds: new GeometryBounds(34, 28, 30, 84),
                    InitialOverlayBounds: new GeometryBounds(18, 62, 64, 32),
                    DesiredTranslationHeight: 32,
                    BubbleBounds: new GeometryBounds(12, 16, 76, 112),
                    RawSourceIndexes: new[] { 0, 1, 2 },
                    MaskSourceIndexes: new[] { 0, 1, 2 }),
                new VerticalLongTranslationFitCase(
                    GroupId: 1,
                    Name: "medium-vertical-translation",
                    TranslationLengthClass: "medium",
                    SourceLanguage: "ja-JP",
                    SourceOrientation: OcrOrientationMode.Vertical,
                    ApprovedReadingOrder: new[] { "\u30ed", "\u30b0", "\u3092", "\u958b", "\u304f", "\u3002", "\u306f", "\u3044" },
                    ApprovedSourceText: "\u30ed \u30b0 \u3092 \u958b \u304f \u3002 \u306f \u3044",
                    ApprovedTranslation: "\u041e\u0442\u043a\u0440\u044b\u0442\u044c \u0436\u0443\u0440\u043d\u0430\u043b \u0437\u0430\u0434\u0430\u0447",
                    SourceBounds: new GeometryBounds(154, 28, 58, 120),
                    InitialOverlayBounds: new GeometryBounds(152, 82, 72, 36),
                    DesiredTranslationHeight: 78,
                    BubbleBounds: new GeometryBounds(142, 16, 88, 146),
                    RawSourceIndexes: new[] { 3, 4, 5, 6, 7, 8, 9 },
                    MaskSourceIndexes: new[] { 3, 4, 5, 6, 7, 8, 9 }),
                new VerticalLongTranslationFitCase(
                    GroupId: 2,
                    Name: "long-vertical-translation",
                    TranslationLengthClass: "long",
                    SourceLanguage: "ja-JP",
                    SourceOrientation: OcrOrientationMode.Vertical,
                    ApprovedReadingOrder: new[] { "\u8a73", "\u7d30", "\u3092", "\u78ba", "\u8a8d", "\u3057", "\u307e", "\u3059" },
                    ApprovedSourceText: "\u8a73 \u7d30 \u3092 \u78ba \u8a8d \u3057 \u307e \u3059",
                    ApprovedTranslation: "\u0414\u043b\u0438\u043d\u043d\u044b\u0439 \u043f\u0435\u0440\u0435\u0432\u043e\u0434 \u0437\u0430\u043d\u0438\u043c\u0430\u0435\u0442 \u043c\u043d\u043e\u0433\u043e \u0441\u0442\u0440\u043e\u043a \u0438 \u0434\u043e\u043b\u0436\u0435\u043d \u0443\u043c\u0435\u0441\u0442\u0438\u0442\u044c\u0441\u044f \u0431\u0435\u0437 \u0441\u0436\u0430\u0442\u0438\u044f",
                    SourceBounds: new GeometryBounds(70, 108, 58, 116),
                    InitialOverlayBounds: new GeometryBounds(68, 176, 63, 34),
                    DesiredTranslationHeight: 116,
                    BubbleBounds: new GeometryBounds(40, 96, 118, 134),
                    RawSourceIndexes: new[] { 10, 11, 12, 13, 14, 15, 16, 17 },
                    MaskSourceIndexes: new[] { 10, 11, 12, 13, 14, 15, 16, 17 }),
            },
            ForbiddenRegions: new[]
            {
                new GeometryBounds(0, 0, 14, 14),
                new GeometryBounds(228, 0, 12, 46),
                new GeometryBounds(224, 206, 16, 28),
            });
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

    private static GoldenReferenceFixture CreateVerticalJapaneseTwoColumnFixture()
    {
        return new GoldenReferenceFixture(
            Id: "vertical-japanese-two-column-prompt",
            CaseType: "manga_vertical_japanese_two_column",
            OriginalText: "\u30bb\u30fc\u30d6\n\u3057\u307e\u3059\u304b",
            SourceLanguage: "ja-JP",
            SourceOrientation: OcrOrientationMode.Vertical,
            ApprovedReadingOrder: new[] { "\u30bb", "\u30fc", "\u30d6", "\u3057", "\u307e", "\u3059", "\u304b" },
            RawSourceBounds: new[]
            {
                new GeometryBounds(132, 42, 20, 22),
                new GeometryBounds(132, 67, 20, 22),
                new GeometryBounds(132, 92, 20, 22),
                new GeometryBounds(88, 42, 20, 22),
                new GeometryBounds(88, 67, 20, 22),
                new GeometryBounds(88, 92, 20, 22),
                new GeometryBounds(88, 117, 20, 22),
            },
            SemanticGroups: new[]
            {
                new GoldenSemanticGroup(
                    GroupId: 0,
                    ApprovedSourceText: "\u30bb \u30fc \u30d6 \u3057 \u307e \u3059 \u304b",
                    SourceBounds: new GeometryBounds(82, 36, 76, 108),
                    RawSourceIndexes: new[] { 0, 1, 2, 3, 4, 5, 6 },
                    MaskSourceIndexes: new[] { 0, 1, 2, 3, 4, 5, 6 }),
            },
            ApprovedTranslation: "\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c?",
            BubbleBounds: new GeometryBounds(44, 20, 156, 170),
            ApprovedOverlayBounds: new GeometryBounds(68, 62, 104, 72),
            RequiredGroupingMergeDistancePercent: 30,
            ForbiddenRegions: new[]
            {
                new GeometryBounds(0, 0, 38, 40),
                new GeometryBounds(204, 72, 30, 90),
            });
    }

    private static GoldenReferenceFixture CreateVerticalJapaneseThreeColumnFixture()
    {
        return new GoldenReferenceFixture(
            Id: "vertical-japanese-three-column-prompt",
            CaseType: "manga_vertical_japanese_three_column",
            OriginalText: "\u30bb\u30fc\u30d6\n\u3057\u307e\u3059\u304b\n\u306f\u3044",
            SourceLanguage: "ja-JP",
            SourceOrientation: OcrOrientationMode.Vertical,
            ApprovedReadingOrder: new[]
            {
                "\u30bb",
                "\u30fc",
                "\u30d6",
                "\u3057",
                "\u307e",
                "\u3059",
                "\u304b",
                "\u306f",
                "\u3044",
            },
            RawSourceBounds: new[]
            {
                new GeometryBounds(148, 42, 20, 22),
                new GeometryBounds(148, 67, 20, 22),
                new GeometryBounds(148, 92, 20, 22),
                new GeometryBounds(108, 42, 20, 22),
                new GeometryBounds(108, 67, 20, 22),
                new GeometryBounds(108, 92, 20, 22),
                new GeometryBounds(108, 117, 20, 22),
                new GeometryBounds(68, 42, 20, 22),
                new GeometryBounds(68, 67, 20, 22),
            },
            SemanticGroups: new[]
            {
                new GoldenSemanticGroup(
                    GroupId: 0,
                    ApprovedSourceText: "\u30bb \u30fc \u30d6 \u3057 \u307e \u3059 \u304b \u306f \u3044",
                    SourceBounds: new GeometryBounds(62, 36, 112, 108),
                    RawSourceIndexes: new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 },
                    MaskSourceIndexes: new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }),
            },
            ApprovedTranslation: "\u0421\u043e\u0445\u0440\u0430\u043d\u0438\u0442\u044c? \u0414\u0430",
            BubbleBounds: new GeometryBounds(36, 20, 168, 170),
            ApprovedOverlayBounds: new GeometryBounds(60, 62, 124, 78),
            RequiredGroupingMergeDistancePercent: 30,
            ForbiddenRegions: new[]
            {
                new GeometryBounds(0, 0, 38, 40),
                new GeometryBounds(208, 72, 28, 90),
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

    private static GoldenReferenceFixture CreateThaiHorizontalFixture()
    {
        return new GoldenReferenceFixture(
            Id: "thai-horizontal-hello",
            CaseType: "thai_horizontal_dialogue",
            OriginalText: "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35",
            SourceLanguage: "tha",
            SourceOrientation: OcrOrientationMode.Horizontal,
            ApprovedReadingOrder: new[] { "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35" },
            RawSourceBounds: new[]
            {
                new GeometryBounds(60, 82, 116, 30),
            },
            SemanticGroups: new[]
            {
                new GoldenSemanticGroup(
                    GroupId: 0,
                    ApprovedSourceText: "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35",
                    SourceBounds: new GeometryBounds(56, 76, 124, 42),
                    RawSourceIndexes: new[] { 0 },
                    MaskSourceIndexes: new[] { 0 }),
            },
            ApprovedTranslation: "Hello",
            BubbleBounds: new GeometryBounds(36, 54, 172, 108),
            ApprovedOverlayBounds: new GeometryBounds(58, 70, 126, 48),
            RequiredGroupingMergeDistancePercent: 12,
            ForbiddenRegions: new[]
            {
                new GeometryBounds(10, 12, 34, 24),
                new GeometryBounds(196, 136, 30, 54),
            });
    }

    private static GoldenReferenceFixture CreateThaiMultilineFixture()
    {
        return new GoldenReferenceFixture(
            Id: "thai-multiline-dialogue",
            CaseType: "thai_multiline_dialogue",
            OriginalText: "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35\n\u0e22\u0e34\u0e19\u0e14\u0e35\u0e15\u0e49\u0e2d\u0e19\u0e23\u0e31\u0e1a",
            SourceLanguage: "tha",
            SourceOrientation: OcrOrientationMode.Horizontal,
            ApprovedReadingOrder: new[]
            {
                "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35",
                "\u0e22\u0e34\u0e19\u0e14\u0e35\u0e15\u0e49\u0e2d\u0e19\u0e23\u0e31\u0e1a",
            },
            RawSourceBounds: new[]
            {
                new GeometryBounds(52, 66, 86, 28),
                new GeometryBounds(52, 102, 142, 28),
            },
            SemanticGroups: new[]
            {
                new GoldenSemanticGroup(
                    GroupId: 0,
                    ApprovedSourceText: "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35 \u0e22\u0e34\u0e19\u0e14\u0e35\u0e15\u0e49\u0e2d\u0e19\u0e23\u0e31\u0e1a",
                    SourceBounds: new GeometryBounds(48, 60, 150, 76),
                    RawSourceIndexes: new[] { 0, 1 },
                    MaskSourceIndexes: new[] { 0, 1 }),
            },
            ApprovedTranslation: "Hello, welcome",
            BubbleBounds: new GeometryBounds(30, 36, 188, 136),
            ApprovedOverlayBounds: new GeometryBounds(50, 72, 148, 60),
            RequiredGroupingMergeDistancePercent: 18,
            ForbiddenRegions: new[]
            {
                new GeometryBounds(8, 10, 36, 26),
                new GeometryBounds(204, 124, 28, 58),
            });
    }

    private static GoldenReferenceFixture CreateThaiThreeLineFixture()
    {
        return new GoldenReferenceFixture(
            Id: "thai-three-line-dialogue",
            CaseType: "thai_three_line_dialogue",
            OriginalText: "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35\n\u0e22\u0e34\u0e19\u0e14\u0e35\u0e15\u0e49\u0e2d\u0e19\u0e23\u0e31\u0e1a\n\u0e1e\u0e1a\u0e01\u0e31\u0e19\u0e43\u0e2b\u0e21\u0e48",
            SourceLanguage: "tha",
            SourceOrientation: OcrOrientationMode.Horizontal,
            ApprovedReadingOrder: new[]
            {
                "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35",
                "\u0e22\u0e34\u0e19\u0e14\u0e35\u0e15\u0e49\u0e2d\u0e19\u0e23\u0e31\u0e1a",
                "\u0e1e\u0e1a\u0e01\u0e31\u0e19\u0e43\u0e2b\u0e21\u0e48",
            },
            RawSourceBounds: new[]
            {
                new GeometryBounds(52, 48, 86, 24),
                new GeometryBounds(52, 78, 142, 24),
                new GeometryBounds(52, 108, 124, 24),
            },
            SemanticGroups: new[]
            {
                new GoldenSemanticGroup(
                    GroupId: 0,
                    ApprovedSourceText: "\u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35 \u0e22\u0e34\u0e19\u0e14\u0e35\u0e15\u0e49\u0e2d\u0e19\u0e23\u0e31\u0e1a \u0e1e\u0e1a\u0e01\u0e31\u0e19\u0e43\u0e2b\u0e21\u0e48",
                    SourceBounds: new GeometryBounds(48, 42, 150, 94),
                    RawSourceIndexes: new[] { 0, 1, 2 },
                    MaskSourceIndexes: new[] { 0, 1, 2 }),
            },
            ApprovedTranslation: "Hello, welcome, see you",
            BubbleBounds: new GeometryBounds(30, 26, 190, 156),
            ApprovedOverlayBounds: new GeometryBounds(50, 64, 152, 76),
            RequiredGroupingMergeDistancePercent: 18,
            ForbiddenRegions: new[]
            {
                new GeometryBounds(8, 10, 36, 26),
                new GeometryBounds(204, 124, 28, 58),
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

    private static void SaveMixedOrientationContactSheetPng(MixedOrientationFixture fixture, string path)
    {
        const int width = 960;
        const int height = 340;
        var stride = checked(width * 4);
        var pixels = CreateCanvas(width, height, background: 238);

        DrawMixedOrientationPanel(pixels, stride, 40, 54, fixture, PanelLayer.Source);
        DrawMixedOrientationPanel(pixels, stride, 360, 54, fixture, PanelLayer.Overlay);
        DrawMixedOrientationPanel(pixels, stride, 680, 54, fixture, PanelLayer.Forbidden);

        SaveMixedOrientationAnnotatedPng(pixels, width, height, stride, path, fixture);
    }

    private static void SaveMixedPlacementEvidencePng(
        MixedOrientationFixture fixture,
        IReadOnlyList<MixedGroupPlacementEvidence> placements,
        string path)
    {
        const int left = 40;
        const int top = 150;
        const int columnWidth = 300;
        const int rowHeight = 300;
        var columns = CreateMixedPlacementEvidenceColumns();
        var width = left + columnWidth * columns.Count + 80;
        var height = top + rowHeight * fixture.SemanticGroups.Count + 80;
        var stride = checked(width * 4);
        var pixels = CreateCanvas(width, height, background: 238);

        for (var row = 0; row < fixture.SemanticGroups.Count; row++)
        {
            var group = fixture.SemanticGroups[row];
            var placement = placements.Single(item => item.GroupId == group.GroupId);
            for (var column = 0; column < columns.Count; column++)
            {
                DrawMixedPlacementPanel(
                    pixels,
                    stride,
                    left + column * columnWidth,
                    top + row * rowHeight,
                    fixture,
                    group,
                    placement,
                    columns[column].Stage);
            }
        }

        SaveMixedPlacementEvidencePng(
            pixels,
            width,
            height,
            stride,
            path,
            fixture,
            placements,
            left,
            top,
            columnWidth,
            rowHeight);
    }

    private static void SaveVerticalLongTranslationFitEvidencePng(
        VerticalLongTranslationFitFixture fixture,
        IReadOnlyList<VerticalLongTranslationFitEvidence> fitEvidence,
        string path)
    {
        const int left = 40;
        const int top = 150;
        const int columnWidth = 300;
        const int rowHeight = 320;
        var columns = CreateVerticalLongTranslationFitEvidenceColumns();
        var width = left + columnWidth * columns.Count + 80;
        var height = top + rowHeight + 80;
        var stride = checked(width * 4);
        var pixels = CreateCanvas(width, height, background: 238);

        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            DrawVerticalLongTranslationFitPanel(
                pixels,
                stride,
                left + columnIndex * columnWidth,
                top,
                fixture,
                fitEvidence,
                columns[columnIndex].Stage);
        }

        SaveVerticalLongTranslationFitEvidencePng(
            pixels,
            width,
            height,
            stride,
            path,
            fixture,
            fitEvidence,
            left,
            top,
            columnWidth,
            rowHeight);
    }

    private static void SaveCalibrationScorecardEvidencePng(
        IReadOnlyList<GoldenReferenceFixture> fixtures,
        CalibrationScorecard scorecard,
        string path)
    {
        const int left = 40;
        const int top = 360;
        const int columnWidth = 320;
        const int rowHeight = 300;
        var evidenceCandidates = SelectVisualEvidenceCandidates(scorecard);
        var width = left + columnWidth * (evidenceCandidates.Count + 1) + 80;
        var height = top + rowHeight * fixtures.Count + 80;
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
        AddVisualEvidenceCandidateById(
            candidates,
            scorecard,
            selected.CandidateId.Replace("_overlay-centered", "_overlay-line-count-step-8", StringComparison.Ordinal));
        AddVisualEvidenceCandidateById(
            candidates,
            scorecard,
            selected.CandidateId.Replace("_overlay-centered", "_overlay-line-count-step-8-x-half-right-overflow", StringComparison.Ordinal));
        AddVisualEvidenceCandidateById(
            candidates,
            scorecard,
            selected.CandidateId.Replace("_overlay-centered", "_overlay-high-8", StringComparison.Ordinal));
        AddVisualEvidenceCandidateById(
            candidates,
            scorecard,
            selected.CandidateId.Replace("_overlay-centered", "_overlay-high-12", StringComparison.Ordinal));
        AddVisualEvidenceCandidate(candidates, overlayFailure);
        AddVisualEvidenceCandidate(candidates, groupingFailure);
        foreach (var fallback in scorecard.Candidates.Where(candidate => !candidate.IsSelected).OrderBy(candidate => candidate.TotalScore))
        {
            if (candidates.Count >= 6)
            {
                break;
            }

            AddVisualEvidenceCandidate(candidates, fallback);
        }

        return candidates;
    }

    private static IReadOnlyList<VisualEvidenceCellMapItem> CreateVisualEvidenceCellMap(
        IReadOnlyList<GoldenReferenceFixture> fixtures,
        IReadOnlyList<CalibrationCandidateScore> candidates)
    {
        var columnCount = candidates.Count + 1;
        var items = new List<VisualEvidenceCellMapItem>(fixtures.Count * columnCount);
        for (var rowIndex = 0; rowIndex < fixtures.Count; rowIndex++)
        {
            var fixture = fixtures[rowIndex];
            items.Add(new VisualEvidenceCellMapItem(
                Position: rowIndex * columnCount + 1,
                Row: rowIndex + 1,
                Column: 1,
                FixtureId: fixture.Id,
                ColumnRole: "source-reference",
                CandidateId: null));

            for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                items.Add(new VisualEvidenceCellMapItem(
                    Position: rowIndex * columnCount + candidateIndex + 2,
                    Row: rowIndex + 1,
                    Column: candidateIndex + 2,
                    FixtureId: fixture.Id,
                    ColumnRole: "candidate",
                    CandidateId: candidates[candidateIndex].CandidateId));
            }
        }

        return items;
    }

    private static IReadOnlyList<MixedPlacementEvidenceCellMapItem> CreateMixedPlacementEvidenceCellMap(
        MixedOrientationFixture fixture,
        IReadOnlyList<MixedGroupPlacementEvidence> placements)
    {
        var columns = CreateMixedPlacementEvidenceColumns();
        var items = new List<MixedPlacementEvidenceCellMapItem>(fixture.SemanticGroups.Count * columns.Count);
        for (var rowIndex = 0; rowIndex < fixture.SemanticGroups.Count; rowIndex++)
        {
            var group = fixture.SemanticGroups[rowIndex];
            var placement = placements.Single(item => item.GroupId == group.GroupId);
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                var column = columns[columnIndex];
                items.Add(new MixedPlacementEvidenceCellMapItem(
                    Position: rowIndex * columns.Count + columnIndex + 1,
                    Row: rowIndex + 1,
                    Column: columnIndex + 1,
                    GroupId: group.GroupId,
                    GroupName: group.Name,
                    SourceOrientation: group.SourceOrientation.ToString(),
                    ColumnRole: column.ColumnRole,
                    OverlayBounds: GetMixedPlacementStageOverlayBounds(placement, column.Stage)));
            }
        }

        return items;
    }

    private static IReadOnlyList<VerticalLongTranslationFitCellMapItem> CreateVerticalLongTranslationFitEvidenceCellMap(
        VerticalLongTranslationFitFixture fixture,
        IReadOnlyList<VerticalLongTranslationFitEvidence> fitEvidence)
    {
        var columns = CreateVerticalLongTranslationFitEvidenceColumns();
        var items = new List<VerticalLongTranslationFitCellMapItem>(columns.Count);
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var column = columns[columnIndex];
            items.Add(new VerticalLongTranslationFitCellMapItem(
                Position: columnIndex + 1,
                Row: 1,
                Column: columnIndex + 1,
                FixtureId: fixture.Id,
                ColumnRole: column.ColumnRole,
                Overlays: fitEvidence
                    .Select(item => new VerticalLongTranslationFitOverlayMapItem(
                        GroupId: item.GroupId,
                        GroupName: item.GroupName,
                        TranslationLengthClass: item.TranslationLengthClass,
                        OverlayBounds: GetVerticalLongTranslationFitStageOverlayBounds(item, column.Stage),
                        FontScale: column.Stage == VerticalLongTranslationFitStage.FinalSimultaneousFit ? item.FontScale : 1d,
                        BaseFontSizePt: item.BaseFontSizePt,
                        FittedFontSizePt: column.Stage == VerticalLongTranslationFitStage.FinalSimultaneousFit
                            ? item.FittedFontSizePt
                            : item.BaseFontSizePt,
                        WidthExpansionPixels: item.WidthExpansionPixels,
                        WidthExpansionLocked: item.WidthExpansionLocked,
                        TextFitsAtBaseFontAfterExpansion: item.TextFitsAtBaseFontAfterExpansion,
                        TextFitsAfterFontReduction: item.TextFitsAfterFontReduction,
                        SemanticArea: item.SemanticArea,
                        MaxOverlayArea: item.MaxOverlayArea,
                        FinalOverlayArea: item.FinalOverlayArea,
                        ExpandedUpward: item.ExpandedUpward,
                        WasShrunk: item.WasShrunk))
                    .ToArray()));
        }

        return items;
    }

    private static IReadOnlyList<MixedPlacementEvidenceColumn> CreateMixedPlacementEvidenceColumns()
    {
        return new[]
        {
            new MixedPlacementEvidenceColumn(MixedPlacementStage.Source, "source-group", "Source group"),
            new MixedPlacementEvidenceColumn(MixedPlacementStage.CenteredOverlay, "centered-overlay", "Centered/base"),
            new MixedPlacementEvidenceColumn(MixedPlacementStage.LineCountOverlay, "line-count-overlay", "Line-count"),
            new MixedPlacementEvidenceColumn(MixedPlacementStage.FinalGroupRules, "final-group-rules", "Final group rules"),
        };
    }

    private static IReadOnlyList<VerticalLongTranslationFitEvidenceColumn> CreateVerticalLongTranslationFitEvidenceColumns()
    {
        return new[]
        {
            new VerticalLongTranslationFitEvidenceColumn(VerticalLongTranslationFitStage.Source, "source-groups", "Source groups"),
            new VerticalLongTranslationFitEvidenceColumn(VerticalLongTranslationFitStage.InitialFit, "initial-fit", "Initial fit"),
            new VerticalLongTranslationFitEvidenceColumn(VerticalLongTranslationFitStage.ExpandUpward, "expand-upward", "Expand upward"),
            new VerticalLongTranslationFitEvidenceColumn(VerticalLongTranslationFitStage.FinalSimultaneousFit, "final-simultaneous-fit", "Final simultaneous"),
        };
    }

    private static void AddVisualEvidenceCandidateById(
        List<CalibrationCandidateScore> candidates,
        CalibrationScorecard scorecard,
        string candidateId)
    {
        var candidate = scorecard.Candidates.FirstOrDefault(item =>
            string.Equals(item.CandidateId, candidateId, StringComparison.Ordinal));
        if (candidate is not null)
        {
            AddVisualEvidenceCandidate(candidates, candidate);
        }
    }

    private static bool HasOverlayViolation(CalibrationCandidateScore candidate)
    {
        return candidate.FixtureScores.Any(score =>
            score.Violations.Contains("overlay-intersects-forbidden-region", StringComparer.Ordinal)
            || score.Violations.Contains("overlay-outside-reference-region", StringComparer.Ordinal));
    }

    private static bool IsOverlayHighCandidate(CalibrationCandidateScore candidate)
    {
        return candidate.Parameters.UseLineCountOffset
            || (candidate.Parameters.OverlayOffsetX == 0
                && candidate.Parameters.OverlayOffsetY < 0
                && candidate.Parameters.OverlayInflation == 0);
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

    private static void DrawMixedOrientationPanel(
        byte[] pixels,
        int stride,
        int originX,
        int originY,
        MixedOrientationFixture fixture,
        PanelLayer layer)
    {
        var frame = new GeometryBounds(originX, originY, 240, 240);
        FillRectangle(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 210, 210, 210);

        foreach (var group in fixture.SemanticGroups)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, group.BubbleBounds, 255, 255, 255);
            DrawOffsetOutline(pixels, stride, originX, originY, group.BubbleBounds, 80, 80, 80, thickness: 2);
        }

        if (layer is PanelLayer.Forbidden)
        {
            foreach (var forbidden in fixture.ForbiddenRegions)
            {
                FillOffsetRectangle(pixels, stride, originX, originY, forbidden, 255, 205, 205);
            }
        }

        foreach (var raw in fixture.RawSourceBounds)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, raw, 20, 20, 20);
        }

        if (layer is PanelLayer.Overlay)
        {
            foreach (var group in fixture.SemanticGroups)
            {
                foreach (var index in group.MaskSourceIndexes)
                {
                    var raw = fixture.RawSourceBounds[index];
                    var mask = new GeometryBounds(raw.X - 4, raw.Y - 4, raw.Width + 8, raw.Height + 8);
                    FillOffsetRectangle(pixels, stride, originX, originY, mask, 70, 70, 70);
                    DrawOffsetOutline(pixels, stride, originX, originY, mask, 0, 0, 0, thickness: 2);
                }
            }
        }

        foreach (var group in fixture.SemanticGroups)
        {
            DrawOffsetOutline(pixels, stride, originX, originY, group.SourceBounds, 0, 150, 60, thickness: 3);
        }

        if (layer is PanelLayer.Overlay)
        {
            foreach (var group in fixture.SemanticGroups)
            {
                FillOffsetRectangle(pixels, stride, originX, originY, group.ApprovedOverlayBounds, 185, 220, 255);
                DrawOffsetOutline(pixels, stride, originX, originY, group.ApprovedOverlayBounds, 20, 100, 230, thickness: 3);
            }
        }

        if (layer is not PanelLayer.Source)
        {
            foreach (var forbidden in fixture.ForbiddenRegions)
            {
                DrawOffsetOutline(pixels, stride, originX, originY, forbidden, 220, 0, 0, thickness: 3);
            }
        }

        DrawRectangleOutline(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 40, 40, 40, thickness: 1);
    }

    private static void DrawMixedPlacementPanel(
        byte[] pixels,
        int stride,
        int originX,
        int originY,
        MixedOrientationFixture fixture,
        MixedSemanticGroup currentGroup,
        MixedGroupPlacementEvidence placement,
        MixedPlacementStage stage)
    {
        var frame = new GeometryBounds(originX, originY, 240, 240);
        FillRectangle(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 210, 210, 210);

        foreach (var group in fixture.SemanticGroups)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, group.BubbleBounds, 255, 255, 255);
            var isCurrentGroup = group.GroupId == currentGroup.GroupId;
            DrawOffsetOutline(
                pixels,
                stride,
                originX,
                originY,
                group.BubbleBounds,
                isCurrentGroup ? (byte)45 : (byte)130,
                isCurrentGroup ? (byte)45 : (byte)130,
                isCurrentGroup ? (byte)45 : (byte)130,
                thickness: isCurrentGroup ? 2 : 1);
        }

        foreach (var forbidden in fixture.ForbiddenRegions)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, forbidden, 255, 245, 245);
            DrawOffsetOutline(pixels, stride, originX, originY, forbidden, 220, 0, 0, thickness: 2);
        }

        foreach (var group in fixture.SemanticGroups)
        {
            var isCurrentGroup = group.GroupId == currentGroup.GroupId;
            foreach (var index in group.RawSourceIndexes)
            {
                var raw = fixture.RawSourceBounds[index];
                if (isCurrentGroup)
                {
                    FillOffsetRectangle(pixels, stride, originX, originY, raw, 20, 20, 20);
                }
                else
                {
                    DrawOffsetOutline(pixels, stride, originX, originY, raw, 115, 115, 115, thickness: 1);
                }
            }
        }

        if (stage is not MixedPlacementStage.Source)
        {
            foreach (var index in currentGroup.MaskSourceIndexes)
            {
                var raw = fixture.RawSourceBounds[index];
                var mask = new GeometryBounds(raw.X - 4, raw.Y - 4, raw.Width + 8, raw.Height + 8);
                FillOffsetRectangle(pixels, stride, originX, originY, mask, 70, 70, 70);
                DrawOffsetOutline(pixels, stride, originX, originY, mask, 0, 0, 0, thickness: 2);
            }
        }

        foreach (var group in fixture.SemanticGroups)
        {
            var isCurrentGroup = group.GroupId == currentGroup.GroupId;
            DrawOffsetOutline(
                pixels,
                stride,
                originX,
                originY,
                group.SourceBounds,
                isCurrentGroup ? (byte)0 : (byte)125,
                isCurrentGroup ? (byte)150 : (byte)200,
                isCurrentGroup ? (byte)60 : (byte)140,
                thickness: isCurrentGroup ? 3 : 1);
        }

        var overlayBounds = GetMixedPlacementStageOverlayBounds(placement, stage);
        if (overlayBounds is { } overlay)
        {
            FillOffsetRectangle(
                pixels,
                stride,
                originX,
                originY,
                overlay,
                EvidenceOverlayFillRed,
                EvidenceOverlayFillGreen,
                EvidenceOverlayFillBlue);
            DrawOffsetOutline(
                pixels,
                stride,
                originX,
                originY,
                overlay,
                EvidenceOverlayOutlineRed,
                EvidenceOverlayOutlineGreen,
                EvidenceOverlayOutlineBlue,
                thickness: 3);
        }

        DrawRectangleOutline(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 40, 40, 40, thickness: 1);
    }

    private static void DrawVerticalLongTranslationFitPanel(
        byte[] pixels,
        int stride,
        int originX,
        int originY,
        VerticalLongTranslationFitFixture fixture,
        IReadOnlyList<VerticalLongTranslationFitEvidence> fitEvidence,
        VerticalLongTranslationFitStage stage)
    {
        var frame = new GeometryBounds(originX, originY, 240, 240);
        FillRectangle(pixels, stride, frame.X, frame.Y, frame.Width, frame.Height, 210, 210, 210);

        foreach (var item in fixture.Cases)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, item.BubbleBounds, 255, 255, 255);
            DrawOffsetOutline(pixels, stride, originX, originY, item.BubbleBounds, 80, 80, 80, thickness: 2);
        }

        foreach (var forbidden in fixture.ForbiddenRegions)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, forbidden, 255, 245, 245);
            DrawOffsetOutline(pixels, stride, originX, originY, forbidden, 220, 0, 0, thickness: 2);
        }

        foreach (var raw in fixture.RawSourceBounds)
        {
            FillOffsetRectangle(pixels, stride, originX, originY, raw, 20, 20, 20);
        }

        if (stage is not VerticalLongTranslationFitStage.Source)
        {
            foreach (var item in fixture.Cases)
            {
                foreach (var index in item.MaskSourceIndexes)
                {
                    var raw = fixture.RawSourceBounds[index];
                    var mask = new GeometryBounds(raw.X - 4, raw.Y - 4, raw.Width + 8, raw.Height + 8);
                    FillOffsetRectangle(pixels, stride, originX, originY, mask, 70, 70, 70);
                    DrawOffsetOutline(pixels, stride, originX, originY, mask, 0, 0, 0, thickness: 2);
                }
            }
        }

        foreach (var item in fixture.Cases)
        {
            DrawOffsetOutline(pixels, stride, originX, originY, item.SourceBounds, 0, 150, 60, thickness: 3);
        }

        if (stage is not VerticalLongTranslationFitStage.Source)
        {
            foreach (var item in fitEvidence)
            {
                var overlay = GetVerticalLongTranslationFitStageOverlayBounds(item, stage);
                if (overlay is null)
                {
                    continue;
                }

                FillOffsetRectangle(pixels, stride, originX, originY, overlay.Value, 185, 220, 255);
                DrawOffsetOutline(pixels, stride, originX, originY, overlay.Value, 20, 100, 230, thickness: 3);
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

            DrawContactSheetText(context, $"Original: {FormatFixtureDisplayText(fixture.OriginalText)}", 40, 300, 13, Brushes.Black, maxTextWidth: 260);
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

    private static void SaveMixedOrientationAnnotatedPng(
        byte[] pixels,
        int width,
        int height,
        int stride,
        string path,
        MixedOrientationFixture fixture)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SaveMixedOrientationAnnotatedPngOnStaThread(pixels, width, height, stride, path, fixture);
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
            throw new InvalidOperationException("Failed to write mixed-orientation calibration contact sheet.", failure);
        }
    }

    private static void SaveMixedOrientationAnnotatedPngOnStaThread(
        byte[] pixels,
        int width,
        int height,
        int stride,
        string path,
        MixedOrientationFixture fixture)
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
            DrawContactSheetText(context, "Mixed source OCR", 40, 24, 16, Brushes.Black, semiBold: true);
            DrawContactSheetText(context, "Group masks + overlays", 360, 24, 16, Brushes.Black, semiBold: true);
            DrawContactSheetText(context, "Forbidden regions", 680, 24, 16, Brushes.Black, semiBold: true);

            DrawMixedSourceGlyphs(context, fixture, originX: 40, originY: 54);
            DrawMixedOverlayTranslations(context, fixture, originX: 360, originY: 54);
            DrawContactSheetText(context, $"Fixture: {fixture.Id}", 40, 300, 13, Brushes.Black, maxTextWidth: 260);
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

    private static void SaveMixedPlacementEvidencePng(
        byte[] pixels,
        int width,
        int height,
        int stride,
        string path,
        MixedOrientationFixture fixture,
        IReadOnlyList<MixedGroupPlacementEvidence> placements,
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
                SaveMixedPlacementEvidencePngOnStaThread(
                    pixels,
                    width,
                    height,
                    stride,
                    path,
                    fixture,
                    placements,
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
            throw new InvalidOperationException("Failed to write mixed-orientation placement visual evidence.", failure);
        }
    }

    private static void SaveMixedPlacementEvidencePngOnStaThread(
        byte[] pixels,
        int width,
        int height,
        int stride,
        string path,
        MixedOrientationFixture fixture,
        IReadOnlyList<MixedGroupPlacementEvidence> placements,
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

        var columns = CreateMixedPlacementEvidenceColumns();
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(image, new Rect(0, 0, width, height));
            DrawContactSheetText(context, "Mixed orientation placement evidence", 40, 18, 18, Brushes.Black, semiBold: true, maxTextWidth: 520);
            DrawContactSheetText(
                context,
                "Each row is one semantic group from the same frame. Final column applies line-count, right-overflow dampening, and semantic top alignment per group.",
                40,
                48,
                12,
                Brushes.DimGray,
                maxTextWidth: 1040);
            DrawContactSheetText(
                context,
                "Legend: black=current raw OCR, green=current semantic group, gray=current mask, blue=translation overlay, red=forbidden",
                40,
                72,
                12,
                Brushes.DimGray,
                maxTextWidth: 1040);

            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                DrawContactSheetText(
                    context,
                    columns[columnIndex].Title,
                    left + columnIndex * columnWidth,
                    116,
                    12,
                    Brushes.Black,
                    semiBold: true,
                    maxTextWidth: 245);
            }

            for (var rowIndex = 0; rowIndex < fixture.SemanticGroups.Count; rowIndex++)
            {
                var group = fixture.SemanticGroups[rowIndex];
                var placement = placements.Single(item => item.GroupId == group.GroupId);
                var y = top + rowIndex * rowHeight;
                DrawContactSheetText(
                    context,
                    $"{group.GroupId}: {group.Name}",
                    left,
                    y - 20,
                    11,
                    Brushes.Black,
                    semiBold: true,
                    maxTextWidth: 285);

                for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
                {
                    var column = columns[columnIndex];
                    var x = left + columnIndex * columnWidth;
                    if (column.Stage is MixedPlacementStage.Source)
                    {
                        DrawMixedSourceGlyphs(context, fixture, x, y);
                    }
                    else
                    {
                        var overlayBounds = GetMixedPlacementStageOverlayBounds(placement, column.Stage);
                        if (overlayBounds is { } overlay)
                        {
                            DrawOutlinedContactSheetText(
                                context,
                                group.ApprovedTranslation,
                                x + overlay.X + 4,
                                y + overlay.Y + 4,
                                10,
                                Brushes.White,
                                Brushes.Black,
                                semiBold: true,
                                maxTextWidth: Math.Max(40d, overlay.Width - 8d));
                        }
                    }

                    DrawContactSheetText(
                        context,
                        CreateMixedPlacementPanelNote(group, placement, column.Stage),
                        x,
                        y + 244,
                        10,
                        Brushes.Black,
                        maxTextWidth: 285);
                }
            }

            DrawGridCellNumbers(context, fixture.SemanticGroups.Count, columns.Count, left, top, columnWidth, rowHeight);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        SavePng(bitmap, path);
    }

    private static void SaveVerticalLongTranslationFitEvidencePng(
        byte[] pixels,
        int width,
        int height,
        int stride,
        string path,
        VerticalLongTranslationFitFixture fixture,
        IReadOnlyList<VerticalLongTranslationFitEvidence> fitEvidence,
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
                SaveVerticalLongTranslationFitEvidencePngOnStaThread(
                    pixels,
                    width,
                    height,
                    stride,
                    path,
                    fixture,
                    fitEvidence,
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
            throw new InvalidOperationException("Failed to write vertical long-translation fit visual evidence.", failure);
        }
    }

    private static void SaveVerticalLongTranslationFitEvidencePngOnStaThread(
        byte[] pixels,
        int width,
        int height,
        int stride,
        string path,
        VerticalLongTranslationFitFixture fixture,
        IReadOnlyList<VerticalLongTranslationFitEvidence> fitEvidence,
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

        var columns = CreateVerticalLongTranslationFitEvidenceColumns();
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(image, new Rect(0, 0, width, height));
            DrawContactSheetText(context, "Vertical long-translation fit evidence", 40, 18, 18, Brushes.Black, semiBold: true, maxTextWidth: 560);
            DrawContactSheetText(
                context,
                "One frame contains short, medium, and long vertical-source translations. Final column draws all blue translation blocks at the same time.",
                40,
                48,
                12,
                Brushes.DimGray,
                maxTextWidth: 1040);
            DrawContactSheetText(
                context,
                "Legend: black=raw OCR, green=semantic group, gray=mask, blue=translation overlay, red=forbidden",
                40,
                72,
                12,
                Brushes.DimGray,
                maxTextWidth: 1040);

            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                var column = columns[columnIndex];
                var x = left + columnIndex * columnWidth;
                DrawContactSheetText(
                    context,
                    column.Title,
                    x,
                    116,
                    12,
                    Brushes.Black,
                    semiBold: true,
                    maxTextWidth: 245);

                if (column.Stage is VerticalLongTranslationFitStage.Source)
                {
                    DrawVerticalLongTranslationFitSourceGlyphs(context, fixture, x, top);
                }
                else
                {
                    DrawVerticalLongTranslationFitOverlayTranslations(context, fixture, fitEvidence, x, top, column.Stage);
                }

                DrawContactSheetText(
                    context,
                    CreateVerticalLongTranslationFitPanelNote(fitEvidence, column.Stage),
                    x,
                    top + 248,
                    10,
                    Brushes.Black,
                    maxTextWidth: 285);
            }

            DrawGridCellNumbers(context, 1, columns.Count, left, top, columnWidth, rowHeight);
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

            DrawEvidenceCellNumbers(context, fixtures, candidates, left, top, columnWidth, rowHeight);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        SavePng(bitmap, path);
    }

    private static void DrawEvidenceCellNumbers(
        DrawingContext context,
        IReadOnlyList<GoldenReferenceFixture> fixtures,
        IReadOnlyList<CalibrationCandidateScore> candidates,
        int left,
        int top,
        int columnWidth,
        int rowHeight)
    {
        var columnCount = candidates.Count + 1;
        DrawGridCellNumbers(context, fixtures.Count, columnCount, left, top, columnWidth, rowHeight);
    }

    private static void DrawGridCellNumbers(
        DrawingContext context,
        int rowCount,
        int columnCount,
        int left,
        int top,
        int columnWidth,
        int rowHeight)
    {
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var position = rowIndex * columnCount + columnIndex + 1;
                var label = FormattableString.Invariant($"#{position}");
                var labelWidth = Math.Max(34d, 18d + label.Length * 7d);
                var labelBounds = new Rect(
                    left + columnIndex * columnWidth + 7,
                    top + rowIndex * rowHeight + 7,
                    labelWidth,
                    20);

                context.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
                    new Pen(Brushes.Black, 1),
                    labelBounds,
                    3,
                    3);
                DrawContactSheetText(
                    context,
                    label,
                    labelBounds.X + 5,
                    labelBounds.Y + 2,
                    11,
                    Brushes.Black,
                    semiBold: true,
                    maxTextWidth: labelBounds.Width - 8);
            }
        }
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
            "В колонках: source reference, centered, line-count-step-8, line-count-step-8-x-half-right-overflow, high-8, high-12, провалы overlay/OCR. Номера #N идут слева направо, сверху вниз.",
            x + 170,
            y + 58,
            12,
            Brushes.DimGray,
            maxTextWidth: 980);
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

    private static void DrawMixedSourceGlyphs(
        DrawingContext context,
        MixedOrientationFixture fixture,
        int originX,
        int originY)
    {
        foreach (var group in fixture.SemanticGroups)
        {
            var unitCount = Math.Min(group.ApprovedReadingOrder.Count, group.RawSourceIndexes.Count);
            for (var unitIndex = 0; unitIndex < unitCount; unitIndex++)
            {
                var bounds = fixture.RawSourceBounds[group.RawSourceIndexes[unitIndex]];
                var isHorizontal = group.SourceOrientation == OcrOrientationMode.Horizontal;
                var fontSize = isHorizontal
                    ? Math.Clamp(bounds.Height - 4d, 9d, 14d)
                    : Math.Clamp(bounds.Width + 2d, 12d, 18d);
                var textX = originX + bounds.X + (isHorizontal ? 3d : -2d);
                var textY = originY + bounds.Y + (isHorizontal ? 1d : Math.Max(2d, (bounds.Height - fontSize) / 2d));
                DrawContactSheetText(
                    context,
                    group.ApprovedReadingOrder[unitIndex],
                    textX,
                    textY,
                    fontSize,
                    Brushes.White,
                    semiBold: true,
                    fontFamily: GetFixtureFontFamily(group.SourceLanguage),
                    maxTextWidth: Math.Max(28d, bounds.Width + 8d));
            }
        }
    }

    private static void DrawVerticalLongTranslationFitSourceGlyphs(
        DrawingContext context,
        VerticalLongTranslationFitFixture fixture,
        int originX,
        int originY)
    {
        foreach (var item in fixture.Cases)
        {
            var unitCount = Math.Min(item.ApprovedReadingOrder.Count, item.RawSourceIndexes.Count);
            for (var unitIndex = 0; unitIndex < unitCount; unitIndex++)
            {
                var bounds = fixture.RawSourceBounds[item.RawSourceIndexes[unitIndex]];
                var fontSize = Math.Clamp(bounds.Width + 2d, 12d, 18d);
                var textX = originX + bounds.X - 2d;
                var textY = originY + bounds.Y + Math.Max(2d, (bounds.Height - fontSize) / 2d);
                DrawContactSheetText(
                    context,
                    item.ApprovedReadingOrder[unitIndex],
                    textX,
                    textY,
                    fontSize,
                    Brushes.White,
                    semiBold: true,
                    fontFamily: GetFixtureFontFamily(item.SourceLanguage),
                    maxTextWidth: Math.Max(28d, bounds.Width + 8d));
            }
        }
    }

    private static void DrawMixedOverlayTranslations(
        DrawingContext context,
        MixedOrientationFixture fixture,
        int originX,
        int originY)
    {
        foreach (var group in fixture.SemanticGroups)
        {
            DrawContactSheetText(
                context,
                group.ApprovedTranslation,
                originX + group.ApprovedOverlayBounds.X + 4,
                originY + group.ApprovedOverlayBounds.Y + 4,
                10,
                Brushes.Black,
                semiBold: true,
                maxTextWidth: Math.Max(40d, group.ApprovedOverlayBounds.Width - 8d));
        }
    }

    private static void DrawVerticalLongTranslationFitOverlayTranslations(
        DrawingContext context,
        VerticalLongTranslationFitFixture fixture,
        IReadOnlyList<VerticalLongTranslationFitEvidence> fitEvidence,
        int originX,
        int originY,
        VerticalLongTranslationFitStage stage)
    {
        foreach (var item in fitEvidence)
        {
            var bounds = GetVerticalLongTranslationFitStageOverlayBounds(item, stage);
            if (bounds is null)
            {
                continue;
            }

            var testCase = fixture.Cases.Single(@case => @case.GroupId == item.GroupId);
            var fittedFontSizePt = stage is VerticalLongTranslationFitStage.FinalSimultaneousFit
                ? item.FittedFontSizePt
                : item.BaseFontSizePt;
            var fontSize = Math.Clamp(fittedFontSizePt / 1.4d, 7d, 10d);
            var clip = new Rect(
                originX + bounds.Value.X + 3,
                originY + bounds.Value.Y + 3,
                Math.Max(8d, bounds.Value.Width - 6d),
                Math.Max(8d, bounds.Value.Height - 6d));
            context.PushClip(new RectangleGeometry(clip));
            DrawContactSheetText(
                context,
                testCase.ApprovedTranslation,
                originX + bounds.Value.X + 4,
                originY + bounds.Value.Y + 4,
                fontSize,
                Brushes.Black,
                semiBold: true,
                maxTextWidth: Math.Max(36d, bounds.Value.Width - 8d));
            context.Pop();
        }
    }

    private static string CreateMixedPlacementPanelNote(
        MixedSemanticGroup group,
        MixedGroupPlacementEvidence placement,
        MixedPlacementStage stage)
    {
        return stage switch
        {
            MixedPlacementStage.Source => FormattableString.Invariant(
                $"orientation={group.SourceOrientation}; semantic={FormatBounds(group.SourceBounds)}"),
            MixedPlacementStage.CenteredOverlay => FormattableString.Invariant(
                $"base overlay={FormatBounds(placement.CenteredOverlayBounds)}"),
            MixedPlacementStage.LineCountOverlay => FormattableString.Invariant(
                $"line count={placement.LineCount}; y={FormatSigned(placement.LineCountOffsetY)}; overlay={FormatBounds(placement.LineCountOverlayBounds)}"),
            MixedPlacementStage.FinalGroupRules => FormattableString.Invariant(
                $"x={FormatSigned(placement.SemanticRightOverflowOffsetX)}; top={FormatSigned(placement.SemanticTopAlignmentOffsetY)}; overlay={FormatBounds(placement.OverlayBounds)}"),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };
    }

    private static string CreateVerticalLongTranslationFitPanelNote(
        IReadOnlyList<VerticalLongTranslationFitEvidence> fitEvidence,
        VerticalLongTranslationFitStage stage)
    {
        var longFit = fitEvidence.Single(item => item.GroupId == 2);
        return stage switch
        {
            VerticalLongTranslationFitStage.Source => "Three vertical semantic groups share one frame.",
            VerticalLongTranslationFitStage.InitialFit => "Initial blue blocks before text-fit growth.",
            VerticalLongTranslationFitStage.ExpandUpward => "Medium and long text grow upward before any lower expansion.",
            VerticalLongTranslationFitStage.FinalSimultaneousFit => FormattableString.Invariant(
                $"All final overlays shown together; long text {longFit.BaseFontSizePt:0}pt->{longFit.FittedFontSizePt:0}pt; width {FormatSigned(longFit.WidthExpansionPixels)}px; area <= 110%."),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };
    }

    private static GeometryBounds? GetMixedPlacementStageOverlayBounds(
        MixedGroupPlacementEvidence placement,
        MixedPlacementStage stage)
    {
        return stage switch
        {
            MixedPlacementStage.Source => null,
            MixedPlacementStage.CenteredOverlay => placement.CenteredOverlayBounds,
            MixedPlacementStage.LineCountOverlay => placement.LineCountOverlayBounds,
            MixedPlacementStage.FinalGroupRules => placement.OverlayBounds,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };
    }

    private static GeometryBounds? GetVerticalLongTranslationFitStageOverlayBounds(
        VerticalLongTranslationFitEvidence evidence,
        VerticalLongTranslationFitStage stage)
    {
        return stage switch
        {
            VerticalLongTranslationFitStage.Source => null,
            VerticalLongTranslationFitStage.InitialFit => evidence.InitialOverlayBounds,
            VerticalLongTranslationFitStage.ExpandUpward => evidence.UpwardExpandedOverlayBounds,
            VerticalLongTranslationFitStage.FinalSimultaneousFit => evidence.FinalOverlayBounds,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
        };
    }

    private static string FormatBounds(GeometryBounds bounds)
    {
        return FormattableString.Invariant($"x={bounds.X},y={bounds.Y},w={bounds.Width},h={bounds.Height}");
    }

    private static string FormatSigned(int value)
    {
        return value switch
        {
            > 0 => FormattableString.Invariant($"+{value}"),
            < 0 => FormattableString.Invariant($"{value}"),
            _ => "0",
        };
    }

    private static void DrawSourceGlyphs(
        DrawingContext context,
        GoldenReferenceFixture fixture,
        int originX,
        int originY,
        Brush? textBrush = null,
        RealOcrRenderCandidate? renderCandidate = null)
    {
        var glyphBrush = textBrush ?? Brushes.White;
        var sourceBounds = CreateRenderSourceBounds(fixture, renderCandidate);
        var glyphCount = Math.Min(fixture.ApprovedReadingOrder.Count, sourceBounds.Count);
        for (var index = 0; index < glyphCount; index++)
        {
            var bounds = sourceBounds[index];
            var isHorizontal = bounds.Width >= bounds.Height;
            var fontSize = renderCandidate?.VerticalFontSize is not null && !isHorizontal
                ? renderCandidate.VerticalFontSize.Value
                : isHorizontal
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
                fontFamily: GetFixtureFontFamily(fixture),
                maxTextWidth: maxTextWidth);
        }
    }

    private static void DrawSourceReferenceText(
        DrawingContext context,
        GoldenReferenceFixture fixture,
        RealOcrRenderCandidate renderCandidate)
    {
        if (renderCandidate.UseVerticalTextBlock && fixture.SourceOrientation == OcrOrientationMode.Vertical)
        {
            DrawVerticalSourceTextBlocks(context, fixture, renderCandidate, Brushes.Black);
            return;
        }

        DrawSourceGlyphs(context, fixture, originX: 0, originY: 0, textBrush: Brushes.Black, renderCandidate);
    }

    private static void DrawVerticalSourceTextBlocks(
        DrawingContext context,
        GoldenReferenceFixture fixture,
        RealOcrRenderCandidate renderCandidate,
        Brush textBrush)
    {
        var sourceBounds = CreateRenderSourceBounds(fixture, renderCandidate);
        var indexedBounds = sourceBounds
            .Select((bounds, index) => new { Bounds = bounds, Index = index })
            .GroupBy(item => item.Bounds.CenterX)
            .OrderByDescending(group => group.Key);
        var fontSize = renderCandidate.VerticalFontSize ?? 22d;

        foreach (var column in indexedBounds)
        {
            var columnItems = column
                .OrderBy(item => item.Bounds.Y)
                .ToArray();
            var text = string.Join(
                Environment.NewLine,
                columnItems.Select(item => fixture.ApprovedReadingOrder[item.Index]));
            var columnBounds = GeometryBounds.Union(columnItems.Select(item => item.Bounds).ToArray());
            DrawContactSheetText(
                context,
                text,
                columnBounds.X - 2,
                columnBounds.Y,
                fontSize,
                textBrush,
                semiBold: true,
                fontFamily: GetFixtureFontFamily(fixture),
                maxTextWidth: Math.Max(30d, columnBounds.Width + 8d));
        }
    }

    private static IReadOnlyList<GeometryBounds> CreateRenderSourceBounds(
        GoldenReferenceFixture fixture,
        RealOcrRenderCandidate? renderCandidate)
    {
        if (renderCandidate is null
            || renderCandidate.VerticalGlyphWidth is null
            || fixture.SourceOrientation != OcrOrientationMode.Vertical)
        {
            return fixture.RawSourceBounds;
        }

        var width = renderCandidate.VerticalGlyphWidth.Value;
        var height = renderCandidate.VerticalGlyphHeight ?? fixture.RawSourceBounds.Max(bounds => bounds.Height);
        var step = renderCandidate.VerticalGlyphStep ?? height;
        var renderedBounds = new GeometryBounds[fixture.RawSourceBounds.Count];
        var columns = fixture.RawSourceBounds
            .Select((bounds, index) => new { Bounds = bounds, Index = index })
            .GroupBy(item => item.Bounds.CenterX);

        foreach (var column in columns)
        {
            var columnItems = column
                .OrderBy(item => item.Bounds.Y)
                .ToArray();
            var columnCenterX = columnItems.Average(item => item.Bounds.CenterX);
            var startY = columnItems.Min(item => item.Bounds.Y);
            for (var index = 0; index < columnItems.Length; index++)
            {
                var x = (int)Math.Round(columnCenterX - width / 2d);
                var y = startY + index * step;
                renderedBounds[columnItems[index].Index] = new GeometryBounds(x, y, width, height);
            }
        }

        return renderedBounds;
    }

    private static string GetFixtureFontFamily(GoldenReferenceFixture fixture)
    {
        return GetFixtureFontFamily(fixture.SourceLanguage);
    }

    private static string GetFixtureFontFamily(string sourceLanguage)
    {
        if (sourceLanguage.StartsWith("tha", StringComparison.OrdinalIgnoreCase))
        {
            return "Leelawadee UI";
        }

        if (sourceLanguage.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
            || sourceLanguage.StartsWith("jpn", StringComparison.OrdinalIgnoreCase))
        {
            return "Yu Gothic UI";
        }

        return "Microsoft YaHei UI";
    }

    private static string FormatFixtureDisplayText(string text)
    {
        return text
            .Replace("\r\n", " / ", StringComparison.Ordinal)
            .Replace("\n", " / ", StringComparison.Ordinal);
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

    private static void DrawOutlinedContactSheetText(
        DrawingContext context,
        string text,
        double x,
        double y,
        double fontSize,
        Brush fillBrush,
        Brush outlineBrush,
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
            outlineBrush,
            pixelsPerDip: 1d)
        {
            MaxTextWidth = maxTextWidth,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        var outlineOffsets = new[]
        {
            new Vector(-1, -1),
            new Vector(0, -1),
            new Vector(1, -1),
            new Vector(-1, 0),
            new Vector(1, 0),
            new Vector(-1, 1),
            new Vector(0, 1),
            new Vector(1, 1),
        };

        foreach (var offset in outlineOffsets)
        {
            context.DrawText(formattedText, new Point(x + offset.X, y + offset.Y));
        }

        var fillText = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(fontFamily),
                FontStyles.Normal,
                semiBold ? FontWeights.SemiBold : FontWeights.Normal,
                FontStretches.Normal),
            fontSize,
            fillBrush,
            pixelsPerDip: 1d)
        {
            MaxTextWidth = maxTextWidth,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        context.DrawText(fillText, new Point(x, y));
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
            new CalibrationOverlayOption("line-count-step-8", OffsetX: 0, OffsetY: -8, Inflation: 0, UseLineCountOffset: true, LineCountOffsetStepY: -8),
            new CalibrationOverlayOption(
                "line-count-step-8-x-half-right-overflow",
                OffsetX: 0,
                OffsetY: -8,
                Inflation: 0,
                UseLineCountOffset: true,
                LineCountOffsetStepY: -8,
                UseSemanticRightOverflowDampening: true,
                SemanticRightOverflowDampeningMinVerticalGroupWidth: VerticalRightOverflowDampeningMinSemanticWidth,
                SemanticRightOverflowDampeningRatio: 0.5d),
            new CalibrationOverlayOption("high-8", OffsetX: 0, OffsetY: -8, Inflation: 0),
            new CalibrationOverlayOption("high-12", OffsetX: 0, OffsetY: -12, Inflation: 0),
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
                OverlayInflation: overlayOption.Inflation,
                UseLineCountOffset: overlayOption.UseLineCountOffset,
                LineCountOffsetStepY: overlayOption.LineCountOffsetStepY,
                UseSemanticRightOverflowDampening: overlayOption.UseSemanticRightOverflowDampening,
                SemanticRightOverflowDampeningMinVerticalGroupWidth: overlayOption.SemanticRightOverflowDampeningMinVerticalGroupWidth,
                SemanticRightOverflowDampeningRatio: overlayOption.SemanticRightOverflowDampeningRatio))
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

        var overlayOffset = CreateCandidateOverlayOffset(candidate, fixture);
        var candidateOverlayBounds = OffsetAndInflate(
            fixture.ApprovedOverlayBounds,
            overlayOffset.OffsetX,
            overlayOffset.OffsetY,
            overlayOffset.Inflation);
        candidateOverlayBounds = ApplySemanticRightOverflowDampening(candidate, fixture, candidateOverlayBounds);
        candidateOverlayBounds = ApplyManualVisualReviewAdjustment(candidate, fixture, candidateOverlayBounds);
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

    private static CalibrationOverlayOffset CreateCandidateOverlayOffset(
        CalibrationCandidate candidate,
        GoldenReferenceFixture fixture)
    {
        if (!candidate.UseLineCountOffset)
        {
            return new CalibrationOverlayOffset(
                candidate.OverlayOffsetX,
                candidate.OverlayOffsetY,
                candidate.OverlayInflation);
        }

        if (fixture.SourceOrientation == OcrOrientationMode.Vertical)
        {
            return new CalibrationOverlayOffset(
                candidate.OverlayOffsetX,
                candidate.OverlayOffsetY,
                candidate.OverlayInflation);
        }

        var lineCount = CountHorizontalTextLines(fixture.RawSourceBounds);
        var offsetY = lineCount <= 1
            ? 0
            : candidate.LineCountOffsetStepY * (lineCount - 1);

        return new CalibrationOverlayOffset(
            candidate.OverlayOffsetX,
            offsetY,
            candidate.OverlayInflation);
    }

    private static int CountHorizontalTextLines(IReadOnlyList<GeometryBounds> bounds)
    {
        if (bounds.Count == 0)
        {
            return 0;
        }

        var rows = new List<double>();
        foreach (var boundsItem in bounds.OrderBy(item => item.CenterY))
        {
            var tolerance = Math.Max(8d, boundsItem.Height * 0.6d);
            if (rows.All(rowCenter => Math.Abs(rowCenter - boundsItem.CenterY) > tolerance))
            {
                rows.Add(boundsItem.CenterY);
            }
        }

        return rows.Count;
    }

    private static GeometryBounds ApplySemanticRightOverflowDampening(
        CalibrationCandidate candidate,
        GoldenReferenceFixture fixture,
        GeometryBounds overlayBounds)
    {
        if (!candidate.UseSemanticRightOverflowDampening || candidate.SemanticRightOverflowDampeningRatio <= 0d)
        {
            return overlayBounds;
        }

        var semanticBounds = GeometryBounds.Union(fixture.SemanticGroups.Select(group => group.SourceBounds).ToArray());
        if (fixture.SourceOrientation == OcrOrientationMode.Vertical
            && candidate.SemanticRightOverflowDampeningMinVerticalGroupWidth > 0
            && semanticBounds.Width < candidate.SemanticRightOverflowDampeningMinVerticalGroupWidth)
        {
            return overlayBounds;
        }

        var rightOverflow = overlayBounds.Right - semanticBounds.Right;
        if (rightOverflow <= 0)
        {
            return overlayBounds;
        }

        var offsetX = -(int)Math.Round(
            rightOverflow * candidate.SemanticRightOverflowDampeningRatio,
            MidpointRounding.AwayFromZero);
        return OffsetAndInflate(overlayBounds, offsetX, offsetY: 0, inflation: 0);
    }

    private static GeometryBounds ApplyManualVisualReviewAdjustment(
        CalibrationCandidate candidate,
        GoldenReferenceFixture fixture,
        GeometryBounds overlayBounds)
    {
        if (!string.Equals(fixture.Id, BookPageHorizontalFixtureId, StringComparison.Ordinal)
            || !candidate.UseLineCountOffset
            || !candidate.UseSemanticRightOverflowDampening)
        {
            return overlayBounds;
        }

        var semanticBounds = GeometryBounds.Union(fixture.SemanticGroups.Select(group => group.SourceBounds).ToArray());
        if (overlayBounds.Y <= semanticBounds.Y)
        {
            return overlayBounds;
        }

        return OffsetAndInflate(overlayBounds, offsetX: 0, offsetY: semanticBounds.Y - overlayBounds.Y, inflation: 0);
    }

    private static VerticalLongTranslationFitEvidence CreateVerticalLongTranslationFitEvidence(
        VerticalLongTranslationFitCase item)
    {
        var semantic = item.SourceBounds;
        var initial = item.InitialOverlayBounds;
        var initialBottom = Math.Min(initial.Bottom, semantic.Bottom);
        var upwardAvailableHeight = Math.Max(0, initialBottom - semantic.Y);
        var upwardExpandedHeight = Math.Min(item.DesiredTranslationHeight, upwardAvailableHeight);
        var upwardExpandedBounds = new GeometryBounds(
            initial.X,
            initialBottom - upwardExpandedHeight,
            initial.Width,
            upwardExpandedHeight);

        var desiredFitsBeforeSemanticTop = item.DesiredTranslationHeight <= upwardExpandedHeight;
        var finalHeight = desiredFitsBeforeSemanticTop
            ? upwardExpandedHeight
            : Math.Min(item.DesiredTranslationHeight, semantic.Height);
        var finalY = desiredFitsBeforeSemanticTop
            ? upwardExpandedBounds.Y
            : semantic.Y;
        var semanticArea = semantic.Width * semantic.Height;
        var maxOverlayArea = semanticArea * VerticalLongTranslationMaxOverlayAreaRatio;
        var maxWidthForArea = finalHeight <= 0
            ? initial.Width
            : Math.Max(1, (int)Math.Floor(maxOverlayArea / finalHeight));
        var finalWidth = Math.Min(initial.Width, maxWidthForArea);
        var finalX = finalWidth == initial.Width
            ? initial.X
            : (int)Math.Round(semantic.CenterX - finalWidth / 2d, MidpointRounding.AwayFromZero);
        var finalBounds = new GeometryBounds(finalX, finalY, finalWidth, finalHeight);
        var fontScale = item.DesiredTranslationHeight <= finalHeight
            ? 1d
            : Math.Round((double)finalHeight / item.DesiredTranslationHeight, 2, MidpointRounding.AwayFromZero);
        var fittedFontSizePt = fontScale >= 1d
            ? VerticalLongTranslationBaseFontSizePt
            : Math.Max(
                VerticalLongTranslationMinFontSizePt,
                Math.Floor(VerticalLongTranslationBaseFontSizePt * fontScale));
        var textFitsAfterFontReduction = item.DesiredTranslationHeight
            * fittedFontSizePt
            / VerticalLongTranslationBaseFontSizePt
            <= finalBounds.Height;
        var finalOverlayArea = finalBounds.Width * finalBounds.Height;

        return new VerticalLongTranslationFitEvidence(
            GroupId: item.GroupId,
            GroupName: item.Name,
            TranslationLengthClass: item.TranslationLengthClass,
            SemanticBounds: semantic,
            InitialOverlayBounds: initial,
            UpwardExpandedOverlayBounds: upwardExpandedBounds,
            FinalOverlayBounds: finalBounds,
            DesiredTranslationHeight: item.DesiredTranslationHeight,
            UpwardExpansionPixels: Math.Max(0, initial.Y - upwardExpandedBounds.Y),
            ExpandedUpward: upwardExpandedBounds.Y < initial.Y,
            ReachedSemanticTop: finalBounds.Y <= semantic.Y,
            ReachedSemanticBottom: finalBounds.Bottom >= semantic.Bottom,
            WasShrunk: fontScale < 1d,
            FontScale: fontScale,
            BaseFontSizePt: VerticalLongTranslationBaseFontSizePt,
            MinFontSizePt: VerticalLongTranslationMinFontSizePt,
            FittedFontSizePt: fittedFontSizePt,
            WidthExpansionPixels: finalBounds.Width - initial.Width,
            WidthExpansionLocked: finalBounds.Width == initial.Width,
            TextFitsAtBaseFontAfterExpansion: item.DesiredTranslationHeight <= finalBounds.Height,
            TextFitsAfterFontReduction: textFitsAfterFontReduction,
            SemanticArea: semanticArea,
            MaxOverlayAreaRatio: VerticalLongTranslationMaxOverlayAreaRatio,
            MaxOverlayArea: maxOverlayArea,
            FinalOverlayArea: finalOverlayArea);
    }

    private static MixedGroupPlacementEvidence CreateMixedGroupPlacement(
        CalibrationCandidate candidate,
        MixedOrientationFixture fixture,
        MixedSemanticGroup group)
    {
        var groupRawBounds = group.RawSourceIndexes
            .Select(index => fixture.RawSourceBounds[index])
            .ToArray();
        var lineCount = group.SourceOrientation == OcrOrientationMode.Horizontal
            ? CountHorizontalTextLines(groupRawBounds)
            : 0;
        var overlayBounds = group.CenteredOverlayBounds;
        var lineCountOffsetY = 0;
        var semanticRightOverflowOffsetX = 0;
        var semanticTopAlignmentOffsetY = 0;

        if (candidate.UseLineCountOffset && group.SourceOrientation != OcrOrientationMode.Vertical)
        {
            lineCountOffsetY = lineCount <= 1
                ? 0
                : candidate.LineCountOffsetStepY * (lineCount - 1);
            overlayBounds = OffsetAndInflate(overlayBounds, offsetX: 0, offsetY: lineCountOffsetY, inflation: 0);
        }

        var lineCountOverlayBounds = overlayBounds;
        var rightOverflowBeforeDampening = overlayBounds.Right - group.SourceBounds.Right;
        if (candidate.UseSemanticRightOverflowDampening && candidate.SemanticRightOverflowDampeningRatio > 0d)
        {
            var canDampenRightOverflow = group.SourceOrientation != OcrOrientationMode.Vertical
                || candidate.SemanticRightOverflowDampeningMinVerticalGroupWidth <= 0
                || group.SourceBounds.Width >= candidate.SemanticRightOverflowDampeningMinVerticalGroupWidth;
            if (canDampenRightOverflow && rightOverflowBeforeDampening > 0)
            {
                semanticRightOverflowOffsetX = -(int)Math.Round(
                    rightOverflowBeforeDampening * candidate.SemanticRightOverflowDampeningRatio,
                    MidpointRounding.AwayFromZero);
                overlayBounds = OffsetAndInflate(overlayBounds, semanticRightOverflowOffsetX, offsetY: 0, inflation: 0);
            }
        }

        var rightDampenedOverlayBounds = overlayBounds;
        var rightOverflowAfterDampening = overlayBounds.Right - group.SourceBounds.Right;
        var appliedSemanticTopAlignment = false;
        if (candidate.UseLineCountOffset
            && candidate.UseSemanticRightOverflowDampening
            && group.SourceOrientation == OcrOrientationMode.Horizontal
            && lineCount > 1
            && overlayBounds.Y > group.SourceBounds.Y)
        {
            semanticTopAlignmentOffsetY = group.SourceBounds.Y - overlayBounds.Y;
            overlayBounds = OffsetAndInflate(overlayBounds, offsetX: 0, offsetY: semanticTopAlignmentOffsetY, inflation: 0);
            appliedSemanticTopAlignment = true;
        }

        return new MixedGroupPlacementEvidence(
            GroupId: group.GroupId,
            GroupName: group.Name,
            SourceLanguage: group.SourceLanguage,
            SourceOrientation: group.SourceOrientation,
            LineCount: lineCount,
            SemanticWidth: group.SourceBounds.Width,
            CenteredOverlayBounds: group.CenteredOverlayBounds,
            LineCountOverlayBounds: lineCountOverlayBounds,
            RightDampenedOverlayBounds: rightDampenedOverlayBounds,
            OverlayBounds: overlayBounds,
            LineCountOffsetY: lineCountOffsetY,
            SemanticRightOverflowOffsetX: semanticRightOverflowOffsetX,
            SemanticTopAlignmentOffsetY: semanticTopAlignmentOffsetY,
            AppliedSemanticTopAlignment: appliedSemanticTopAlignment,
            RightOverflowBeforeDampening: rightOverflowBeforeDampening,
            RightOverflowAfterDampening: rightOverflowAfterDampening);
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
        var fixtureSweeps = new List<RealOcrFixtureSweep>();

        foreach (var fixture in fixtures)
        {
            var candidateResults = new List<RealOcrCandidateSweep>();
            var presetCandidates = CreateRealOcrPresetCandidates(fixture);
            foreach (var renderCandidate in CreateRealOcrRenderCandidates(fixture))
            {
                var sourceFrame = RenderSourceReferenceFrame(fixture, renderCandidate);
                foreach (var candidate in presetCandidates)
                {
                    candidateResults.Add(await RunRealOcrCandidateAsync(tessdataPath, engine, preprocessor, fixture, sourceFrame, renderCandidate, candidate));
                }
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
            foreach (var renderCandidate in CreateRealOcrRenderCandidates(fixture))
            {
                var frame = RenderSourceReferenceFrame(fixture, renderCandidate);
                var variantSuffix = string.Equals(renderCandidate.Name, "fixture", StringComparison.Ordinal)
                    ? string.Empty
                    : $"-{renderCandidate.Name}";
                var relativePath = Path.Combine("real-ocr", $"{fixture.Id}{variantSuffix}-source.png");
                var sourcePath = Path.Combine(artifactDirectory, relativePath);
                SavePng(frame.PixelData.ToArray(), frame.Width, frame.Height, frame.Stride, sourcePath);
                sourceFrames.Add(
                    new RealOcrSourceFrameEvidence(
                        fixture.Id,
                        renderCandidate.Name,
                        relativePath.Replace('\\', '/'),
                        frame.Width,
                        frame.Height,
                        fixture.SourceLanguage,
                        fixture.SourceOrientation.ToString(),
                        Normalize(string.Join(" ", fixture.ApprovedReadingOrder))));
            }
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

    private static IReadOnlyList<RealOcrPresetCandidate> CreateRealOcrPresetCandidates(GoldenReferenceFixture fixture)
    {
        var candidates = new List<RealOcrPresetCandidate>
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
            new RealOcrPresetCandidate(
                "scale-2",
                new OcrPreprocessingSettings
                {
                    IsEnabled = true,
                    Scale = 2.0,
                }),
            new RealOcrPresetCandidate(
                "threshold-140-scale-2",
                new OcrPreprocessingSettings
                {
                    IsEnabled = true,
                    ThresholdingEnabled = true,
                    Threshold = 140,
                    Scale = 2.0,
                }),
            new RealOcrPresetCandidate(
                "threshold-160-scale-2",
                new OcrPreprocessingSettings
                {
                    IsEnabled = true,
                    ThresholdingEnabled = true,
                    Threshold = 160,
                    Scale = 2.0,
                }),
            new RealOcrPresetCandidate(
                "threshold-180-scale-2",
                new OcrPreprocessingSettings
                {
                    IsEnabled = true,
                    ThresholdingEnabled = true,
                    Threshold = 180,
                    Scale = 2.0,
                }),
        };

        if (IsVerticalJapaneseFixture(fixture))
        {
            AddPageSegModeCandidates(candidates, "default", OcrPreprocessingSettings.Default);
            AddPageSegModeCandidates(
                candidates,
                "threshold-scale",
                new OcrPreprocessingSettings
                {
                    IsEnabled = true,
                    ThresholdingEnabled = true,
                    Threshold = 160,
                    Scale = 1.5,
                });
        }

        return candidates;
    }

    private static IReadOnlyList<RealOcrRenderCandidate> CreateRealOcrRenderCandidates(GoldenReferenceFixture fixture)
    {
        var candidates = new List<RealOcrRenderCandidate>
        {
            new("fixture"),
        };

        if (IsVerticalJapaneseFixture(fixture))
        {
            candidates.Add(new RealOcrRenderCandidate(
                Name: "native-wide-28-step-29",
                VerticalGlyphWidth: 28,
                VerticalGlyphHeight: 26,
                VerticalGlyphStep: 29,
                VerticalFontSize: 24));
            candidates.Add(new RealOcrRenderCandidate(
                Name: "native-block-font-24",
                VerticalGlyphWidth: 30,
                VerticalGlyphHeight: 28,
                VerticalGlyphStep: 30,
                VerticalFontSize: 24,
                UseVerticalTextBlock: true));
        }

        return candidates;
    }

    private static void AddPageSegModeCandidates(
        List<RealOcrPresetCandidate> candidates,
        string presetPrefix,
        OcrPreprocessingSettings settings)
    {
        candidates.Add(new RealOcrPresetCandidate($"{presetPrefix}-psm-single-block-vert", settings, PageSegMode.SingleBlockVertText));
        candidates.Add(new RealOcrPresetCandidate($"{presetPrefix}-psm-single-line", settings, PageSegMode.SingleLine));
        candidates.Add(new RealOcrPresetCandidate($"{presetPrefix}-psm-single-column", settings, PageSegMode.SingleColumn));
        candidates.Add(new RealOcrPresetCandidate($"{presetPrefix}-psm-sparse-text", settings, PageSegMode.SparseText));
        candidates.Add(new RealOcrPresetCandidate($"{presetPrefix}-psm-raw-line", settings, PageSegMode.RawLine));
    }

    private static bool IsVerticalJapaneseFixture(GoldenReferenceFixture fixture)
    {
        return fixture.SourceOrientation == OcrOrientationMode.Vertical
            && fixture.SourceLanguage.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<RealOcrCandidateSweep> RunRealOcrCandidateAsync(
        string tessdataPath,
        TesseractOcrEngine engine,
        OcrPreprocessor preprocessor,
        GoldenReferenceFixture fixture,
        CapturedFrame sourceFrame,
        RealOcrRenderCandidate renderCandidate,
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
            var result = candidate.PageSegMode is null
                ? await engine.RecognizeAsync(request)
                : RunRealOcrPageSegCandidate(tessdataPath, fixture, request, candidate.PageSegMode.Value);
            var normalizedExpected = Normalize(string.Join(" ", fixture.ApprovedReadingOrder));
            var normalizedActual = Normalize(result.Text);

            return new RealOcrCandidateSweep(
                candidate.Name,
                renderCandidate.Name,
                candidate.PageSegMode?.ToString(),
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
                renderCandidate.Name,
                candidate.PageSegMode?.ToString(),
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

    private static OcrResult RunRealOcrPageSegCandidate(
        string tessdataPath,
        GoldenReferenceFixture fixture,
        OcrRequest request,
        PageSegMode pageSegMode)
    {
        var bitmapBytes = InvokeCreateTesseractBitmapBytes(request.Frame);
        using var image = PixImage.LoadFromMemory(bitmapBytes);
        var language = InvokeMapTesseractLanguage(fixture.SourceLanguage, fixture.SourceOrientation);
        using var engine = new TesseractEngine(tessdataPath, language, EngineMode.Default);
        engine.DefaultPageSegMode = pageSegMode;
        using var page = engine.Process(image, pageSegMode);
        var textBlocks = CreateRealOcrTextBlocks(page, request.Frame.Width, request.Frame.Height);

        return new OcrResult(request, textBlocks, FixtureTime);
    }

    private static IReadOnlyList<OcrTextBlock> CreateRealOcrTextBlocks(
        TesseractPage page,
        int frameWidth,
        int frameHeight)
    {
        var blocks = new List<OcrTextBlock>();
        foreach (var layoutBlock in page.Layout)
        {
            foreach (var paragraph in layoutBlock.Paragraphs)
            {
                foreach (var textLine in paragraph.TextLines)
                {
                    if (string.IsNullOrWhiteSpace(textLine.Text) || !textLine.BoundingBox.HasValue)
                    {
                        continue;
                    }

                    var bounds = CreateRealOcrBoundingBox(textLine.BoundingBox.Value, frameWidth, frameHeight);
                    blocks.Add(new OcrTextBlock(textLine.Text, bounds));
                }
            }
        }

        if (blocks.Count == 0 && !string.IsNullOrWhiteSpace(page.Text))
        {
            blocks.Add(new OcrTextBlock(page.Text.Trim(), new BoundingBox(0, 0, frameWidth, frameHeight)));
        }

        return blocks;
    }

    private static BoundingBox CreateRealOcrBoundingBox(
        TesseractRect rect,
        int frameWidth,
        int frameHeight)
    {
        var x = Math.Clamp(rect.X1, 0, frameWidth - 1);
        var y = Math.Clamp(rect.Y1, 0, frameHeight - 1);
        var right = Math.Clamp(rect.X2, x + 1, frameWidth);
        var bottom = Math.Clamp(rect.Y2, y + 1, frameHeight);

        return new BoundingBox(
            x,
            y,
            checked(right - x),
            checked(bottom - y));
    }

    private static byte[] InvokeCreateTesseractBitmapBytes(CapturedFrame frame)
    {
        var method = typeof(TesseractOcrEngine).GetMethod(
            "CreateBitmapBytes",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(CapturedFrame) },
            modifiers: null)
            ?? throw new InvalidOperationException("TesseractOcrEngine.CreateBitmapBytes was not found.");

        return (byte[])(method.Invoke(null, new object[] { frame })
            ?? throw new InvalidOperationException("TesseractOcrEngine.CreateBitmapBytes returned null."));
    }

    private static string InvokeMapTesseractLanguage(
        string languageTag,
        OcrOrientationMode orientationMode)
    {
        var method = typeof(TesseractOcrEngine).GetMethod(
            "MapLanguage",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(string), typeof(OcrOrientationMode) },
            modifiers: null)
            ?? throw new InvalidOperationException("TesseractOcrEngine.MapLanguage overload was not found.");

        return (string)(method.Invoke(null, new object[] { languageTag, orientationMode })
            ?? throw new InvalidOperationException("TesseractOcrEngine.MapLanguage returned null."));
    }

    private static CapturedFrame RenderSourceReferenceFrame(GoldenReferenceFixture fixture)
    {
        return RenderSourceReferenceFrame(fixture, new RealOcrRenderCandidate("fixture"));
    }

    private static CapturedFrame RenderSourceReferenceFrame(
        GoldenReferenceFixture fixture,
        RealOcrRenderCandidate renderCandidate)
    {
        Exception? failure = null;
        CapturedFrame? frame = null;
        var thread = new Thread(() =>
        {
            try
            {
                frame = RenderSourceReferenceFrameOnStaThread(fixture, renderCandidate);
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

    private static CapturedFrame RenderSourceReferenceFrameOnStaThread(
        GoldenReferenceFixture fixture,
        RealOcrRenderCandidate renderCandidate)
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
            DrawSourceReferenceText(context, fixture, renderCandidate);
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

        if (fixture.SourceLanguage.StartsWith("tha", StringComparison.OrdinalIgnoreCase)
            || fixture.SourceLanguage.StartsWith("th", StringComparison.OrdinalIgnoreCase))
        {
            return "tha.traineddata";
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

    private sealed record MixedOrientationFixture(
        string Id,
        string CaseType,
        string OriginalText,
        IReadOnlyList<GeometryBounds> RawSourceBounds,
        IReadOnlyList<MixedSemanticGroup> SemanticGroups,
        IReadOnlyList<GeometryBounds> ForbiddenRegions);

    private sealed record MixedSemanticGroup(
        int GroupId,
        string Name,
        string SourceLanguage,
        OcrOrientationMode SourceOrientation,
        IReadOnlyList<string> ApprovedReadingOrder,
        string ApprovedSourceText,
        string ApprovedTranslation,
        GeometryBounds SourceBounds,
        GeometryBounds CenteredOverlayBounds,
        GeometryBounds ApprovedOverlayBounds,
        GeometryBounds BubbleBounds,
        IReadOnlyList<int> RawSourceIndexes,
        IReadOnlyList<int> MaskSourceIndexes);

    private sealed record MixedGroupPlacementEvidence(
        int GroupId,
        string GroupName,
        string SourceLanguage,
        OcrOrientationMode SourceOrientation,
        int LineCount,
        int SemanticWidth,
        GeometryBounds CenteredOverlayBounds,
        GeometryBounds LineCountOverlayBounds,
        GeometryBounds RightDampenedOverlayBounds,
        GeometryBounds OverlayBounds,
        int LineCountOffsetY,
        int SemanticRightOverflowOffsetX,
        int SemanticTopAlignmentOffsetY,
        bool AppliedSemanticTopAlignment,
        int RightOverflowBeforeDampening,
        int RightOverflowAfterDampening);

    private sealed record MixedPlacementEvidenceColumn(
        MixedPlacementStage Stage,
        string ColumnRole,
        string Title);

    private sealed record MixedPlacementEvidenceCellMapItem(
        int Position,
        int Row,
        int Column,
        int GroupId,
        string GroupName,
        string SourceOrientation,
        string ColumnRole,
        GeometryBounds? OverlayBounds);

    private sealed record VerticalLongTranslationFitFixture(
        string Id,
        string CaseType,
        string OriginalText,
        IReadOnlyList<GeometryBounds> RawSourceBounds,
        IReadOnlyList<VerticalLongTranslationFitCase> Cases,
        IReadOnlyList<GeometryBounds> ForbiddenRegions);

    private sealed record VerticalLongTranslationFitCase(
        int GroupId,
        string Name,
        string TranslationLengthClass,
        string SourceLanguage,
        OcrOrientationMode SourceOrientation,
        IReadOnlyList<string> ApprovedReadingOrder,
        string ApprovedSourceText,
        string ApprovedTranslation,
        GeometryBounds SourceBounds,
        GeometryBounds InitialOverlayBounds,
        int DesiredTranslationHeight,
        GeometryBounds BubbleBounds,
        IReadOnlyList<int> RawSourceIndexes,
        IReadOnlyList<int> MaskSourceIndexes);

    private sealed record VerticalLongTranslationFitEvidence(
        int GroupId,
        string GroupName,
        string TranslationLengthClass,
        GeometryBounds SemanticBounds,
        GeometryBounds InitialOverlayBounds,
        GeometryBounds UpwardExpandedOverlayBounds,
        GeometryBounds FinalOverlayBounds,
        int DesiredTranslationHeight,
        int UpwardExpansionPixels,
        bool ExpandedUpward,
        bool ReachedSemanticTop,
        bool ReachedSemanticBottom,
        bool WasShrunk,
        double FontScale,
        double BaseFontSizePt,
        double MinFontSizePt,
        double FittedFontSizePt,
        int WidthExpansionPixels,
        bool WidthExpansionLocked,
        bool TextFitsAtBaseFontAfterExpansion,
        bool TextFitsAfterFontReduction,
        int SemanticArea,
        double MaxOverlayAreaRatio,
        double MaxOverlayArea,
        int FinalOverlayArea);

    private sealed record VerticalLongTranslationFitEvidenceColumn(
        VerticalLongTranslationFitStage Stage,
        string ColumnRole,
        string Title);

    private sealed record VerticalLongTranslationFitCellMapItem(
        int Position,
        int Row,
        int Column,
        string FixtureId,
        string ColumnRole,
        IReadOnlyList<VerticalLongTranslationFitOverlayMapItem> Overlays);

    private sealed record VerticalLongTranslationFitOverlayMapItem(
        int GroupId,
        string GroupName,
        string TranslationLengthClass,
        GeometryBounds? OverlayBounds,
        double FontScale,
        double BaseFontSizePt,
        double FittedFontSizePt,
        int WidthExpansionPixels,
        bool WidthExpansionLocked,
        bool TextFitsAtBaseFontAfterExpansion,
        bool TextFitsAfterFontReduction,
        int SemanticArea,
        double MaxOverlayArea,
        int FinalOverlayArea,
        bool ExpandedUpward,
        bool WasShrunk);

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
        OcrPreprocessingSettings Settings,
        PageSegMode? PageSegMode = null);

    private sealed record RealOcrRenderCandidate(
        string Name,
        int? VerticalGlyphWidth = null,
        int? VerticalGlyphHeight = null,
        int? VerticalGlyphStep = null,
        double? VerticalFontSize = null,
        bool UseVerticalTextBlock = false);

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
        string RenderVariant,
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
        string RenderVariant,
        string? PageSegMode,
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
        int OverlayInflation,
        bool UseLineCountOffset,
        int LineCountOffsetStepY,
        bool UseSemanticRightOverflowDampening,
        int SemanticRightOverflowDampeningMinVerticalGroupWidth,
        double SemanticRightOverflowDampeningRatio);

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
        int Inflation,
        bool UseLineCountOffset = false,
        int LineCountOffsetStepY = 0,
        bool UseSemanticRightOverflowDampening = false,
        int SemanticRightOverflowDampeningMinVerticalGroupWidth = 0,
        double SemanticRightOverflowDampeningRatio = 0d);

    private readonly record struct CalibrationOverlayOffset(
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

    private sealed record VisualEvidenceCellMapItem(
        int Position,
        int Row,
        int Column,
        string FixtureId,
        string ColumnRole,
        string? CandidateId);

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

        public static GeometryBounds Union(IReadOnlyList<GeometryBounds> bounds)
        {
            if (bounds.Count == 0)
            {
                return default;
            }

            var x = bounds.Min(item => item.X);
            var y = bounds.Min(item => item.Y);
            var right = bounds.Max(item => item.Right);
            var bottom = bounds.Max(item => item.Bottom);
            return new GeometryBounds(x, y, right - x, bottom - y);
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

    private enum MixedPlacementStage
    {
        Source,
        CenteredOverlay,
        LineCountOverlay,
        FinalGroupRules,
    }

    private enum VerticalLongTranslationFitStage
    {
        Source,
        InitialFit,
        ExpandUpward,
        FinalSimultaneousFit,
    }
}
