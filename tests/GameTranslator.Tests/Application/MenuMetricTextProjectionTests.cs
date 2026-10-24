using GameTranslator.Application.Capture;
using GameTranslator.Application.Content;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class MenuMetricTextProjectionTests
{
    private static OcrResult Raw(string[] tokens, int scale = 1, int gap = 20,
        int lines = 1, double confidence = 95, bool mixedPass = false, bool rowEvidence = false)
    {
        var width = 1000 * scale; var height = 100 * scale;
        var frame = new CapturedFrame(new(0, 0, width, height), width, height, width * 4,
            "Bgra32", new byte[width * height * 4], DateTimeOffset.UnixEpoch);
        var request = new OcrRequest(frame, "eng", orientationMode: OcrOrientationMode.Horizontal,
            contentLayoutMode: ContentLayoutMode.StaticMenu)
        { HasMenuValueRowEvidence = rowEvidence, DetectorLineBounds = Enumerable.Range(0, lines).Select(i => new BoundingBox(0, i * 30 * scale, width, 20 * scale)).ToArray() };
        var words = tokens.Select((t, i) => new OcrWord(t, new((30 + i * (60 + gap)) * scale, 10 * scale, 60 * scale, 20 * scale),
            confidence, mixedPass ? $"pass-{i}" : "same-pass")).ToArray();
        return new OcrResult(request, new[] { new OcrTextBlock(string.Join(' ', tokens), new(0, 0, width, 40 * scale)) },
            DateTimeOffset.UnixEpoch, words: words);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PartialMetrics_RequireRowEvidenceAndWideWordSeparation(int scale)
    {
        string[] tokens = ["7", "Power"];
        Assert.Null(MenuMetricTextProjection.TryCreate(Raw(tokens, scale)));
        Assert.Equal("Power", Assert.Single(MenuMetricTextProjection.TryCreate(
            Raw(tokens, scale, gap: 13, rowEvidence: true))!.TextBlocks).Text);
        Assert.Null(MenuMetricTextProjection.TryCreate(Raw(tokens, scale, gap: 5, rowEvidence: true)));
        Assert.Null(MenuMetricTextProjection.TryCreate(Raw(tokens, scale, confidence: 30, rowEvidence: true)));
        Assert.Null(MenuMetricTextProjection.TryCreate(Raw(["Power", "x2"], scale, rowEvidence: true)));
        Assert.Equal(new[] { "Power", "Speed" }, MenuMetricTextProjection.TryCreate(
            Raw(["Power", "7", "Speed"], scale, rowEvidence: true))!.TextBlocks.Select(b => b.Text));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 123)]
    [InlineData(3, 45)]
    public void RowEvidence_RequiresAlignedNearbyWideAndCompactNeighbors(int scale, int offset)
    {
        TextCandidate Candidate(int x, int y, int width) => new(new BoundingBox(
            offset + x * scale, offset + y * scale, width * scale, 20 * scale), .95);
        var request = Raw(["Power"]).Request;
        TextCandidate[] row = [Candidate(0, 0, 140), Candidate(145, 0, 80), Candidate(245, 0, 15)];
        Assert.All(MenuValueRowEvidence.Annotate(row, request), c => Assert.True(c.HasMenuValueRowEvidence));
        Assert.All(MenuValueRowEvidence.Annotate(row.Take(2), request), c => Assert.False(c.HasMenuValueRowEvidence));
        foreach (var last in new[] { Candidate(400, 0, 15), Candidate(245, 30, 15), Candidate(245, 0, 80) })
            Assert.All(MenuValueRowEvidence.Annotate([row[0], row[1], last], request), c => Assert.False(c.HasMenuValueRowEvidence));
        var vertical = new OcrRequest(request.Frame, "eng", orientationMode: OcrOrientationMode.Vertical,
            contentLayoutMode: ContentLayoutMode.StaticMenu);
        Assert.All(MenuValueRowEvidence.Annotate(row, vertical), c => Assert.False(c.HasMenuValueRowEvidence));
        var grouped = row[0] with { SourceCandidateBounds = new[] { row[0].Bounds, row[0].Bounds } };
        Assert.All(MenuValueRowEvidence.Annotate([grouped, row[1], row[2]], request), c => Assert.False(c.HasMenuValueRowEvidence));
        var comic = new OcrRequest(request.Frame, "eng");
        Assert.All(MenuValueRowEvidence.Annotate(row, comic), c => Assert.False(c.HasMenuValueRowEvidence));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Metrics_ProjectSeparateLabelsWithOriginalCoordinates(int scale)
    {
        var raw = Raw(new[] { "7", "Power", "4", "Speed" }, scale);
        var projected = TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = ContentLayoutMode.StaticMenu, TranslationGroupingMode = TranslationGroupingMode.WholeZone });
        Assert.Equal(new[] { "Power", "Speed" }, projected.TextBlocks.Select(b => b.Text));
        Assert.Equal(new[] { raw.Words[1].Bounds, raw.Words[3].Bounds }, projected.TextBlocks.Select(b => b.Bounds));
        Assert.Single(raw.TextBlocks);
        var snapshot = new OverlayPositioningService().CreateSnapshot(projected, DateTimeOffset.UnixEpoch, null,
            new OcrZoneTextStyle { LayoutMode = OverlayTextLayoutMode.KeepWithinSourceBounds });
        Assert.Equal(2, snapshot.TextItems.Count);
        Assert.Equal(2, snapshot.MaskItems.Count);
        foreach (var mask in snapshot.MaskItems)
        foreach (var value in new[] { raw.Words[0].Bounds, raw.Words[2].Bounds })
            Assert.True(mask.X + mask.Width <= value.X || mask.X >= value.Right,
                "Label masking must not hide the original numeric values.");
    }

    [Theory]
    [InlineData("Power", "7", "Speed", "4")]
    [InlineData("Power", "7%", "Speed", "+4")]
    public void Metrics_AcceptTrailingValues(string a, string b, string c, string d) =>
        Assert.Equal(new[] { "Power", "Speed" }, MenuMetricTextProjection.TryCreate(Raw(new[] { a, b, c, d }))!.TextBlocks.Select(x => x.Text));

    [Theory]
    [InlineData("Level", "2", "Requires", "3")]
    [InlineData("Collect", "3", "coins", "5")]
    public void Metrics_KeepOrdinaryWordSpacing(string a, string b, string c, string d) =>
        Assert.Null(MenuMetricTextProjection.TryCreate(Raw(new[] { a, b, c, d }, gap: 5)));

    [Theory]
    [InlineData("7th", "Patrol", "Sector", "2")]
    [InlineData("Power", "Cell", "x2", "Storage")]
    [InlineData("Gain", "3", "points", "5")]
    [InlineData("Power", "7", "Speed", "4.")]
    public void Metrics_KeepNamesAndProse(string a, string b, string c, string d) =>
        Assert.Null(MenuMetricTextProjection.TryCreate(Raw(new[] { a, b, c, d })));

    [Fact]
    public void Metrics_RequireCompleteReliableSingleLineEvidence()
    {
        string[] words = ["Power", "7", "Speed", "4"];
        Assert.Null(MenuMetricTextProjection.TryCreate(Raw(words, lines: 2)));
        Assert.Null(MenuMetricTextProjection.TryCreate(Raw(words, confidence: 30)));
        Assert.Null(MenuMetricTextProjection.TryCreate(Raw(words, mixedPass: true)));
        var raw = Raw(words);
        Assert.Null(MenuMetricTextProjection.TryCreate(new OcrResult(raw.Request, raw.TextBlocks, raw.RecognizedAt,
            words: raw.Words.Skip(1))));
        Assert.Single(TranslationTextGroupingService.CreateTranslationSourceResult(raw,
            new OcrZone { ContentLayoutMode = ContentLayoutMode.DialogComic }).TextBlocks);
    }
}
