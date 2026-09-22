using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
namespace GameTranslator.Tests.Application;
public sealed class ShortHorizontalLineGroupingTests
{
    private static TextCandidate C(int x,int y,int w,int h) => new(new BoundingBox(x,y,w,h),0.9);
    private static TextCandidate[] Example() => [C(1050,332,55,22),C(1045,356,66,28),C(1052,386,53,22),C(1043,414,73,21),C(1036,441,81,21),C(1043,468,70,21),C(1069,498,17,17),C(1032,523,93,19),C(1040,550,78,20),C(1057,576,41,22),C(1037,603,84,24),C(1027,630,105,20),C(1044,657,68,21)];
    [Theory]
    [InlineData(0.85)]
    [InlineData(1.0)]
    [InlineData(1.25)]
    public void CenteredSingleLetter_PreservesOneThirteenLineParagraph(double scale)
    {
        var input=Example().Select(c=>C((int)Math.Round(c.Bounds.X*scale),(int)Math.Round(c.Bounds.Y*scale),(int)Math.Round(c.Bounds.Width*scale),(int)Math.Round(c.Bounds.Height*scale))).ToArray();
        var result=new BoundedTextCandidateGroupingService().Group(input,1600,WritingSystemGroupingProfile.SpacedLeftToRight);
        var group=Assert.Single(result);
        Assert.Equal(input.Select(c=>c.Bounds),group.SourceCandidateBounds);
    }
    [Fact]
    public void ShortLine_DoesNotBridgeLargeParagraphGap()
    {
        var input=new[]{C(0,0,100,20),C(45,45,10,20),C(0,90,100,20)};
        var result=new BoundedTextCandidateGroupingService().Group(input,400,WritingSystemGroupingProfile.SpacedLeftToRight);
        Assert.Equal(3,result.Count);
    }
    [Fact]
    public void ShortLine_OutsideCommonColumnStaysSeparate()
    {
        var input=new[]{C(0,0,100,20),C(120,25,10,20),C(0,50,100,20)};
        var result=new BoundedTextCandidateGroupingService().Group(input,400,WritingSystemGroupingProfile.SpacedLeftToRight);
        Assert.Contains(result,c=>c.Bounds==input[1].Bounds && c.SourceCandidateCount==1);
    }
    [Fact]
    public void TallVerticalText_IsNotTreatedAsShortHorizontalLine()
    {
        var input=new[]{C(0,0,100,20),C(45,25,10,70),C(0,100,100,20)};
        var result=new BoundedTextCandidateGroupingService().Group(input,400,WritingSystemGroupingProfile.SpacedLeftToRight);
        Assert.Equal(3,result.Count);
    }
    [Fact]
    public void ExplicitLineCap_IsStillRespected()
    {
        var result=new BoundedTextCandidateGroupingService().Group(Example(),1600,WritingSystemGroupingProfile.SpacedLeftToRight,new OcrCandidateGroupingSettings{MaximumHorizontalLines=6});
        Assert.All(result,c=>Assert.InRange(c.SourceCandidateCount,1,6));
        Assert.Equal(13,result.Sum(c=>c.SourceCandidateCount));
    }
    [Fact]
    public void VerticalCjkProfile_KeepsExistingBehavior()
    {
        var result=new BoundedTextCandidateGroupingService().Group(Example(),1600,WritingSystemGroupingProfile.CjkVertical);
        Assert.Contains(result,c=>c.Bounds==new BoundingBox(1069,498,17,17));
    }
}
