using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class WatermarkContextAnalyzerTests
{
    [Theory]
    [InlineData(40, 50, 100, 100, 0)]
    [InlineData(170, 180, 120, 90, 1)]
    [InlineData(85, 90, 80, 130, 2)]
    public void SingleLineUsesIllustratedSurroundingsWithoutTemplates(int x, int y, int width, int height, int style)
    {
        var page = Page(x, y, width, height, style);
        var region = Crop(page, new(x + 8, y + height - 26, width - 16, 18));
        Assert.False(WatermarkFilter.Analyze(region)!.LogoStructure);
        var evidence = WatermarkContextAnalyzer.Analyze(page, region);
        Assert.NotNull(evidence?.ContextBounds);
        Assert.True(WatermarkFilter.Evaluate(WatermarkFilterMode.Strong, evidence, "unreadable").Excluded);
        Assert.False(WatermarkFilter.Evaluate(WatermarkFilterMode.Cautious, evidence, "unreadable").Excluded);
        Assert.True(WatermarkFilter.Evaluate(WatermarkFilterMode.Cautious, evidence! with { DistinctPages = 3 }, "unreadable").Excluded);
        Assert.Equal(1, region.Candidate.SourceCandidateCount);
        Assert.Equal(width - 16, region.Frame.Width);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void RegularTextCardsAndPlainColorDoNotBecomeEmblems(int style)
    {
        var page = Page(80, 80, 120, 120, style);
        var region = Crop(page, new(88, 170, 104, 18));
        var evidence = WatermarkContextAnalyzer.Analyze(page, region);
        Assert.False(WatermarkFilter.Evaluate(WatermarkFilterMode.Strong, evidence! with { DistinctPages = 8 }, "example.com").Excluded);
    }

    [Fact]
    public void NearbyCandidateDoesNotBorrowEmblemEvidence()
    {
        var page = Page(40, 50, 100, 100, 0);
        var evidence = WatermarkContextAnalyzer.Analyze(page, Crop(page, new(155, 80, 90, 20)));
        Assert.False(WatermarkFilter.Evaluate(WatermarkFilterMode.Strong, evidence, "example.com").Excluded);
    }

    [Fact]
    public void ContextAndStructuralChangesInvalidateDecisionButPageCountsBelowThresholdDoNot()
    {
        var a = new WatermarkVisualEvidence(.4, true, true) { ContextFingerprint = 1 };
        var key = WatermarkFilter.CreateEvidenceKey(WatermarkFilterMode.Cautious, a);
        Assert.Equal(key, WatermarkFilter.CreateEvidenceKey(WatermarkFilterMode.Cautious, a with { DistinctPages = 2 }));
        Assert.NotEqual(key, WatermarkFilter.CreateEvidenceKey(WatermarkFilterMode.Cautious, a with { DistinctPages = 3 }));
        Assert.NotEqual(key, WatermarkFilter.CreateEvidenceKey(WatermarkFilterMode.Cautious, a with { ContextFingerprint = 2 }));
        Assert.NotEqual(key, WatermarkFilter.CreateEvidenceKey(WatermarkFilterMode.Cautious, a with { LogoStructure = false }));
        Assert.Equal(default, WatermarkFilter.CreateEvidenceKey(WatermarkFilterMode.Off, a));
    }

    [Fact]
    public void BorderCandidatesRemainBounded()
    {
        var page = Page(0, 0, 100, 100, 0);
        foreach(var box in new[] { new BoundingBox(0,0,40,20), new BoundingBox(300,300,20,20), new BoundingBox(0,300,20,20) })
            WatermarkContextAnalyzer.Analyze(page, Crop(page,box));
    }

    [Fact]
    public void HistoryCollectsFragmentsBeforeTheyAreRecognizedAsLogo()
    {
        var history=new WatermarkPageHistory();
        var region=Crop(Page(40,50,100,100,0),new(48,124,84,18));
        Assert.False(WatermarkFilter.Analyze(region)!.LogoStructure);
        foreach(var page in new[]{0,0,1,1,2,2}) history.Observe(WatermarkFilterTests.Page(page),new[]{region});
        Assert.Equal(3,region.WatermarkEvidence!.DistinctPages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BorderlessIllustrationUsesAlignedOcrFragmentsWithoutChangingTheirBounds(bool dark)
    {
        var frame=Page(40,50,120,120,0);
        var pixels=frame.PixelData.ToArray();
        for(var y=0;y<320;y++) for(var x=0;x<320;x++)
            if(Math.Pow((x-100)/60d,2)+Math.Pow((y-110)/60d,2)>1)
                for(var c=0;c<3;c++) pixels[(y*320+x)*4+c]=dark?(byte)0:(byte)255;
        frame=new(new(0,0,320,320),320,320,1280,"Bgra32",pixels,DateTimeOffset.UnixEpoch);
        var lines=new[]{new BoundingBox(48,65,104,22),new BoundingBox(75,114,50,20),new BoundingBox(48,145,104,20)};
        var regions=lines.Select(b=>Crop(frame,b)).ToArray();
        foreach(var region in regions)
        {
            var evidence=WatermarkContextAnalyzer.Analyze(frame,region,regions);
            Assert.True(WatermarkFilter.Evaluate(WatermarkFilterMode.Strong,evidence,"unreadable").Excluded);
            Assert.Single(region.Candidate.SourceCandidateBounds);
            Assert.Contains(region.Candidate.Bounds,lines);
        }
    }

    [Fact]
    public void NeighborRowsDoNotTurnRegularTextIntoLogoOrBorrowAnotherColumnsIllustration()
    {
        var frame=Page(40,50,120,120,3);
        var lines=new[]{new BoundingBox(48,55,104,20),new BoundingBox(75,110,50,20),new BoundingBox(48,140,104,20)};
        var regions=lines.Select(b=>Crop(frame,b)).ToArray();
        foreach(var region in regions)
            Assert.False(WatermarkFilter.Evaluate(WatermarkFilterMode.Strong,
                WatermarkContextAnalyzer.Analyze(frame,region,regions),"example.com").Excluded);
        var other=Crop(frame,new(190,65,100,20));
        Assert.False(WatermarkFilter.Evaluate(WatermarkFilterMode.Strong,
            WatermarkContextAnalyzer.Analyze(frame,other,regions.Append(other).ToArray()),"example.com").Excluded);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BackgroundBoundaryDoesNotBecomeIllustrationContrast(bool vertical, bool grouped)
    {
        var frame=Page(40,50,120,120,0);
        var pixels=frame.PixelData.ToArray();
        for(var y=50;y<170;y++) for(var x=40;x<160;x++)
        {
            var shift=(vertical ? x<100 : y<110) ? -90 : 70;
            for(var c=0;c<3;c++) pixels[(y*320+x)*4+c]=(byte)(pixels[(y*320+x)*4+c]+shift);
        }
        frame=new(new(0,0,320,320),320,320,1280,"Bgra32",pixels,DateTimeOffset.UnixEpoch);
        var region=Crop(frame,new(48,55,104,110));
        if(grouped) region=new(new TextCandidate(region.Candidate.Bounds,.9)
        { SourceCandidateBounds=new[]{new BoundingBox(48,55,104,22),new BoundingBox(75,110,50,20),new BoundingBox(48,145,104,20)} },region.Frame);
        var evidence=WatermarkContextAnalyzer.Analyze(frame,region);
        Assert.NotNull(evidence?.ContextBounds);
        Assert.True(evidence.ContextGlobalContrast>.62);
        Assert.InRange(evidence.LocalContrast,.02,.4);
        var decision=WatermarkFilter.Evaluate(WatermarkFilterMode.Strong,evidence,"unreadable");
        Assert.True(decision.Excluded);
        Assert.Equal(evidence.ContextGlobalContrast,decision.ContextGlobalContrast);
    }

    [Fact]
    public void HighContrastInsideTexturedTilesIsNotLoweredByTheNewMeasurement()
    {
        var frame=Page(40,50,120,120,0);
        var pixels=frame.PixelData.ToArray();
        for(var y=50;y<170;y++) for(var x=40;x<160;x++)
        {
            var n=(unchecked((uint)x*73856093u)^unchecked((uint)y*19349663u))%5;
            var offset=(y*320+x)*4;
            pixels[offset]=(byte)(n==1||n==2?255:0);
            pixels[offset+1]=(byte)(n==1||n==3?255:0);
            pixels[offset+2]=(byte)(n==1||n==4?255:0);
        }
        frame=new(new(0,0,320,320),320,320,1280,"Bgra32",pixels,DateTimeOffset.UnixEpoch);
        var evidence=WatermarkContextAnalyzer.Analyze(frame,Crop(frame,new(48,55,104,110)));
        Assert.NotNull(evidence?.ContextBounds);
        Assert.True(evidence.LocalContrast>=.62);
        Assert.False(WatermarkFilter.Evaluate(WatermarkFilterMode.Strong,evidence,"high contrast illustration").Excluded);
    }

    internal static CapturedFrame Page(int x, int y, int width, int height, int style)
    {
        var p = Enumerable.Repeat((byte)255, 320 * 320 * 4).ToArray();
        for(var yy=y; yy<y+height; yy++) for(var xx=x; xx<x+width; xx++)
        {
            var o=(yy*320+xx)*4;
            var n=(int)((unchecked((uint)(xx-x)*73856093u) ^ unchecked((uint)(yy-y)*19349663u)) % 6);
            var dark= style == 3 && (yy-y)%30<9 && (xx-x)%12<6;
            if(style >= 3) { p[o]=dark?(byte)70:(byte)165; p[o+1]=dark?(byte)70:(byte)175; p[o+2]=dark?(byte)70:(byte)165; }
            else { p[o]=(byte)(100+((n+style)%3)*35); p[o+1]=(byte)(100+((n+style+1)%3)*35); p[o+2]=(byte)(100+((n+style+2)%3)*35); }
        }
        return new(new(0,0,320,320),320,320,1280,"Bgra32",p,DateTimeOffset.UnixEpoch);
    }

    internal static TextCandidateRegion Crop(CapturedFrame page, BoundingBox b)
    {
        var p=new byte[b.Width*b.Height*4];
        for(var y=0;y<b.Height;y++) page.PixelData.Span.Slice((b.Y+y)*page.Stride+b.X*4,b.Width*4).CopyTo(p.AsSpan(y*b.Width*4));
        return new(new(b,.9),new(new(0,0,b.Width,b.Height),b.Width,b.Height,b.Width*4,"Bgra32",p,DateTimeOffset.UnixEpoch));
    }
}
