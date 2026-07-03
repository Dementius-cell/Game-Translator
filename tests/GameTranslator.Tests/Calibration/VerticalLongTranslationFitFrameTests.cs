using System.IO;
using System.Text.Json;

namespace GameTranslator.Tests.Calibration;

public sealed class VerticalLongTranslationFitFrameTests
{
    private const string FixtureId = "vertical-long-translation-fit-frame";

    [Fact]
    public void VerticalLongTranslationFitFrame_WhenPromoted_KeepsAcceptedFinalOverlayContract()
    {
        var fixtureDirectory = Path.Combine(RepositoryRoot.Find(), "artifacts", "calibration", FixtureId);

        using var manifestDocument = OpenJson(Path.Combine(fixtureDirectory, "manifest.json"));
        using var fitRulesDocument = OpenJson(Path.Combine(fixtureDirectory, "fit-rules.json"));
        using var placementMapDocument = OpenJson(Path.Combine(fixtureDirectory, "placement-evidence-map.json"));

        var manifest = manifestDocument.RootElement;
        var fitRules = fitRulesDocument.RootElement;
        var placementMap = placementMapDocument.RootElement;

        Assert.Equal(FixtureId, manifest.GetProperty("Id").GetString());
        Assert.Contains("fit-rules.json", GetStringValues(manifest.GetProperty("generatedArtifacts")));
        Assert.Contains("placement-evidence-map.json", GetStringValues(manifest.GetProperty("generatedArtifacts")));
        Assert.Equal(1, fitRules.GetProperty("schemaVersion").GetInt32());
        Assert.True(fitRules.GetProperty("testOnly").GetBoolean());
        Assert.Equal(FixtureId, fitRules.GetProperty("Id").GetString());
        Assert.Equal("vertical-long-translation-fit", fitRules.GetProperty("rule").GetString());
        Assert.Equal(FixtureId, placementMap.GetProperty("fixtureId").GetString());

        var fitEvidence = fitRules.GetProperty("fitEvidence")
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("GroupId").GetInt32());
        Assert.Equal(3, fitEvidence.Count);

        AssertAcceptedFit(fitEvidence[0], new Bounds(18, 62, 64, 32), 2772d, 2048, expandedUpward: false, reachedTop: false, reachedBottom: false);
        AssertAcceptedFit(fitEvidence[1], new Bounds(152, 40, 72, 78), 7656d, 5616, expandedUpward: true, reachedTop: false, reachedBottom: false);
        AssertAcceptedFit(fitEvidence[2], new Bounds(68, 108, 63, 116), 7400.8d, 7308, expandedUpward: true, reachedTop: true, reachedBottom: true);

        var sourceCell = FindCell(placementMap, "source-groups");
        Assert.All(
            sourceCell.GetProperty("Overlays").EnumerateArray(),
            overlay => Assert.Equal(JsonValueKind.Null, overlay.GetProperty("OverlayBounds").ValueKind));

        var finalCell = FindCell(placementMap, "final-simultaneous-fit");
        var finalOverlays = finalCell.GetProperty("Overlays")
            .EnumerateArray()
            .ToDictionary(item => item.GetProperty("GroupId").GetInt32());
        Assert.Equal(3, finalOverlays.Count);

        foreach (var (groupId, evidence) in fitEvidence)
        {
            var finalOverlay = finalOverlays[groupId];
            Assert.Equal(GetBounds(evidence.GetProperty("FinalOverlayBounds")), GetBounds(finalOverlay.GetProperty("OverlayBounds")));
            Assert.Equal(evidence.GetProperty("FinalOverlayArea").GetDouble(), finalOverlay.GetProperty("FinalOverlayArea").GetDouble(), precision: 3);
            Assert.Equal(0, finalOverlay.GetProperty("WidthExpansionPixels").GetInt32());
            Assert.True(finalOverlay.GetProperty("WidthExpansionLocked").GetBoolean());
            Assert.Equal(14d, finalOverlay.GetProperty("BaseFontSizePt").GetDouble(), precision: 3);
            Assert.Equal(14d, finalOverlay.GetProperty("FittedFontSizePt").GetDouble(), precision: 3);
            Assert.False(finalOverlay.GetProperty("WasShrunk").GetBoolean());
        }

        var evidenceImage = placementMap.GetProperty("evidenceImage").GetString();
        Assert.False(string.IsNullOrWhiteSpace(evidenceImage));
        Assert.True(File.Exists(Path.Combine(fixtureDirectory, evidenceImage)), evidenceImage);
    }

    private static JsonDocument OpenJson(string path)
    {
        Assert.True(File.Exists(path), path);
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonElement FindCell(JsonElement placementMap, string columnRole)
    {
        return placementMap.GetProperty("cells")
            .EnumerateArray()
            .Single(cell => cell.GetProperty("ColumnRole").GetString() == columnRole);
    }

    private static void AssertAcceptedFit(
        JsonElement evidence,
        Bounds expectedFinalBounds,
        double expectedMaxOverlayArea,
        int expectedFinalArea,
        bool expandedUpward,
        bool reachedTop,
        bool reachedBottom)
    {
        Assert.Equal(expectedFinalBounds, GetBounds(evidence.GetProperty("FinalOverlayBounds")));
        Assert.Equal(0, evidence.GetProperty("WidthExpansionPixels").GetInt32());
        Assert.True(evidence.GetProperty("WidthExpansionLocked").GetBoolean());
        Assert.Equal(14d, evidence.GetProperty("BaseFontSizePt").GetDouble(), precision: 3);
        Assert.Equal(14d, evidence.GetProperty("FittedFontSizePt").GetDouble(), precision: 3);
        Assert.False(evidence.GetProperty("WasShrunk").GetBoolean());
        Assert.True(evidence.GetProperty("TextFitsAtBaseFontAfterExpansion").GetBoolean());
        Assert.True(evidence.GetProperty("TextFitsAfterFontReduction").GetBoolean());
        Assert.Equal(expandedUpward, evidence.GetProperty("ExpandedUpward").GetBoolean());
        Assert.Equal(reachedTop, evidence.GetProperty("ReachedSemanticTop").GetBoolean());
        Assert.Equal(reachedBottom, evidence.GetProperty("ReachedSemanticBottom").GetBoolean());
        Assert.Equal(expectedMaxOverlayArea, evidence.GetProperty("MaxOverlayArea").GetDouble(), precision: 3);
        Assert.Equal(expectedFinalArea, evidence.GetProperty("FinalOverlayArea").GetInt32());
        Assert.True(
            evidence.GetProperty("FinalOverlayArea").GetDouble() <= evidence.GetProperty("MaxOverlayArea").GetDouble(),
            "Final overlay area must stay within the accepted semantic-area cap.");
    }

    private static IReadOnlyList<string> GetStringValues(JsonElement array)
    {
        return array.EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
    }

    private static Bounds GetBounds(JsonElement element)
    {
        return new Bounds(
            element.GetProperty("X").GetInt32(),
            element.GetProperty("Y").GetInt32(),
            element.GetProperty("Width").GetInt32(),
            element.GetProperty("Height").GetInt32());
    }

    private readonly record struct Bounds(int X, int Y, int Width, int Height);
}
