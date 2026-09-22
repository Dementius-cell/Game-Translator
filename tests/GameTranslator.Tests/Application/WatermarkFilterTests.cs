using System.Text.Json;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Pipeline;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Application;

public sealed class WatermarkFilterTests
{
    [Theory]
    [InlineData(WatermarkFilterMode.Off)]
    [InlineData(WatermarkFilterMode.Cautious)]
    [InlineData(WatermarkFilterMode.Strong)]
    public void SingleSignalNeverExcludes(WatermarkFilterMode mode)
    {
        Assert.False(WatermarkFilter.Evaluate(mode, new(.2, false, false), "faint text").Excluded);
        Assert.False(WatermarkFilter.Evaluate(mode, new(.9, true, true), "colorful emblem").Excluded);
        Assert.False(WatermarkFilter.Evaluate(mode, new(.9, false, false, 8), "42").Excluded);
        Assert.False(WatermarkFilter.Evaluate(mode, new(.9, false, false), "example.com").Excluded);
    }

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("sample . c0m", true)]
    [InlineData("sample.corn", true)]
    [InlineData("ｗｗｗ．ｅｘａｍｐｌｅ．ｏｒｇ", true)]
    [InlineData("https://example.test/path", true)]
    [InlineData("12.07", false)]
    [InlineData("score.0007", false)]
    [InlineData("3.14 and version 1.2", false)]
    [InlineData("sample.company", false)]
    public void AddressToleranceIsBounded(string text, bool expected) =>
        Assert.Equal(expected, WatermarkFilter.HasWebsiteAddress(text));

    [Fact]
    public void StrongRejectsIllustratedLowContrastLogoButCautiousWaitsForCorroboration()
    {
        var visual = WatermarkFilter.Analyze(Logo());
        Assert.NotNull(visual);
        Assert.True(visual.LogoStructure);
        Assert.True(visual.DetailedLogo);
        Assert.True(WatermarkFilter.Evaluate(WatermarkFilterMode.Strong, visual, "123 mixed").Excluded);
        Assert.False(WatermarkFilter.Evaluate(WatermarkFilterMode.Cautious, visual, "123 mixed").Excluded);
        Assert.True(WatermarkFilter.Evaluate(WatermarkFilterMode.Cautious, visual, "sample.com").Excluded);
        Assert.False(WatermarkFilter.Evaluate(WatermarkFilterMode.Off, visual, "sample.com").Excluded);
    }

    [Theory]
    [InlineData("faint small text", 180, 190)]
    [InlineData("123456 3.14", 0, 255)]
    [InlineData("HP 42 日本語 العربية 한글", 70, 180)]
    [InlineData("colored subtitles example.com", 80, 210)]
    public void UsefulRegularTextSurvivesBothModes(string text, byte dark, byte light)
    {
        var pixels = new byte[120 * 100 * 4];
        for(var y=0;y<100;y++) for(var x=0;x<120;x++)
        {
            var o=(y*120+x)*4; var ink=y%30<12 && x%12<6;
            pixels[o]=ink?dark:light; pixels[o+1]=ink?light:light; pixels[o+2]=ink?dark:light; pixels[o+3]=255;
        }
        var candidate = new TextCandidate(new(0,0,120,100),.9) { SourceCandidateBounds = new[] {
            new BoundingBox(0,0,120,20),new BoundingBox(0,30,115,20),new BoundingBox(0,60,110,20) } };
        var visual=WatermarkFilter.Analyze(new(candidate,Frame(120,100,pixels)));
        Assert.False(visual!.LogoStructure);
        foreach(var mode in new[] { WatermarkFilterMode.Cautious, WatermarkFilterMode.Strong })
            Assert.False(WatermarkFilter.Evaluate(mode,visual with { DistinctPages=8 },text).Excluded);
    }

    [Fact]
    public void DistinctPagesRequireStableChangedContentAndDoNotCountRevisits()
    {
        var history=new WatermarkPageHistory();
        var logo=Logo();
        for(var i=0;i<50;i++) history.Observe(Page(0),new[]{logo});
        Assert.Equal(1,logo.WatermarkEvidence!.DistinctPages);
        history.Observe(Page(1),new[]{logo});
        Assert.Equal(1,logo.WatermarkEvidence!.DistinctPages);
        history.Observe(Page(1),new[]{logo});
        Assert.Equal(2,logo.WatermarkEvidence!.DistinctPages);
        history.Observe(Page(0),new[]{logo});
        Assert.Equal(2,logo.WatermarkEvidence!.DistinctPages);
        history.Observe(Page(2),new[]{logo}); history.Observe(Page(2),new[]{logo});
        Assert.Equal(3,logo.WatermarkEvidence!.DistinctPages);
        Assert.True(WatermarkFilter.Evaluate(WatermarkFilterMode.Cautious,logo.WatermarkEvidence,"unreadable").Excluded);
        new WatermarkPageHistory().Observe(Page(0),new[]{logo});
        Assert.InRange(logo.WatermarkEvidence!.DistinctPages,0,1);
    }

    [Fact]
    public void PageHistoryIgnoresBrightnessAndSmallAnimatedArea()
    {
        var history=new WatermarkPageHistory(); var logo=Logo();
        history.Observe(Page(0),new[]{logo}); history.Observe(Page(0),new[]{logo});
        for(var i=0;i<8;i++)
        {
            var original=Page(0); var p=original.PixelData.ToArray();
            for(var n=0;n<p.Length;n+=4) for(var c=0;c<3;c++) p[n+c]+=(byte)(i+1);
            for(var y=0;y<20;y++) for(var x=0;x<20;x++) p[(y*320+x)*4]=(byte)(i*25);
            history.Observe(Frame(320,320,p),new[]{logo});
        }
        Assert.Equal(1,logo.WatermarkEvidence!.DistinctPages);
    }

    [Fact]
    public void IdenticalColoredPixelsWithRegularDialogueRowsAreNotLogoStructure()
    {
        var logo=Logo();
        var candidate=logo.Candidate with { SourceCandidateBounds=new[]{
            new BoundingBox(0,0,120,25),new BoundingBox(0,35,110,25),new BoundingBox(0,70,115,25) } };
        var visual=WatermarkFilter.Analyze(new(candidate,logo.Frame));
        Assert.False(visual!.LogoStructure);
        Assert.False(WatermarkFilter.Evaluate(WatermarkFilterMode.Strong,visual with { DistinctPages=10 },"example.com mixed 日本語 42").Excluded);
    }

    [Fact]
    public void ProjectionExcludesTranslationAndMaskButPreservesRawGeometryAndDiagnostics()
    {
        var frame=Logo().Frame; var request=new OcrRequest(frame,"en");
        var block=new OcrTextBlock("sample.com",new(1,1,50,12));
        var source=new OcrResult(request,new[]{block},DateTimeOffset.UnixEpoch) {
            WatermarkDecision=WatermarkFilter.Evaluate(WatermarkFilterMode.Strong,new(.3,true,true),block.Text) };
        var projected=TranslationTextGroupingService.CreateTranslationSourceResult(source,new OcrZone());
        Assert.Empty(projected.TextBlocks); Assert.Empty(projected.TextBlockSources);
        Assert.Same(block,Assert.Single(source.TextBlocks));
        Assert.Single(source.TextBlockSources);
        Assert.DoesNotContain(block.Text,source.WatermarkDecision!.Reasons);
    }

    [Fact]
    public void MissingSettingDefaultsOffAndUnknownEnumFailsValidation()
    {
        var profile=JsonSerializer.Deserialize<GameProfile>("{\"Name\":\"Old profile\",\"OcrSettings\":{\"Engine\":\"Tesseract\"}}")!;
        Assert.Equal(WatermarkFilterMode.Off,profile.OcrSettings.WatermarkFilter);
        var result=new ProfileValidator().Validate(profile with { OcrSettings=new() { WatermarkFilter=(WatermarkFilterMode)99 } });
        Assert.Contains(result.Errors,e=>e.Code==ProfileValidationErrorCodes.InvalidOcrSettings);
    }

    public static TextCandidateRegion Logo()
    {
        var pixels=new byte[120*100*4];
        byte[][] colors={new byte[]{130,145,195},new byte[]{130,195,145},new byte[]{195,130,145},new byte[]{195,145,130},new byte[]{145,195,130},new byte[]{145,130,195}};
        for(var y=0;y<100;y++) for(var x=0;x<120;x++)
        {
            var color=colors[(x/10+y/10)%colors.Length];var o=(y*120+x)*4;
            for(var c=0;c<3;c++) pixels[o+c]=color[c];pixels[o+3]=255;
        }
        var candidate=new TextCandidate(new(0,0,120,100),.9) { SourceCandidateBounds=new[] {
            new BoundingBox(0,0,120,20),new BoundingBox(30,50,65,20),new BoundingBox(5,80,110,20) } };
        return new(candidate,Frame(120,100,pixels));
    }

    public static CapturedFrame Page(int kind)
    {
        var pixels=new byte[320*320*4];
        for(var y=0;y<320;y++) for(var x=0;x<320;x++)
        {
            var v=(byte)((kind==0?x<160:kind==1?y<160:(x/40+y/40)%2==0)?40:190);
            var o=(y*320+x)*4;pixels[o]=pixels[o+1]=pixels[o+2]=v;pixels[o+3]=255;
        }
        return Frame(320,320,pixels);
    }

    public static CapturedFrame Frame(int width,int height,byte[] pixels) =>
        new(new(0,0,width,height),width,height,width*4,"Bgra32",pixels,DateTimeOffset.UnixEpoch);
}
