using GameTranslator.Application.Capture;
using GameTranslator.Application.Pipeline;

namespace GameTranslator.Tests.Application;

public sealed class MenuTextAuthorityTests
{
    private static readonly Guid Source = Guid.NewGuid();
    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
    private static CaptureFrameOrigin Origin(int time) => new(Source, Ms(time), DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData("Other")]
    [InlineData("")]
    public void UnvalidatedChangeRevokesButRequiresTwoValidatedObservations(string text)
    {
        var gate = Confirm("Action");
        var version = gate.Version;
        Assert.False(gate.RevokeIfDifferent(Origin(200), [text], Ms(400)));
        Assert.False(gate.RevokeIfDifferent(Origin(400) with { SourceId = Guid.NewGuid() }, [text], Ms(400)));
        Assert.False(gate.RevokeIfDifferent(Origin(400), ["Action"], Ms(400)));
        Assert.True(gate.RevokeIfDifferent(Origin(400), [text], Ms(400)));
        Assert.False(gate.CanPublish(version, Ms(400)));
        Assert.True(gate.Observe(Origin(400), [text], Ms(400)));
        Assert.False(gate.Confirmed);
        Assert.True(gate.Observe(Origin(600), [text], Ms(600)));
        Assert.Equal(text.Length > 0, gate.Confirmed);
    }

    [Theory]
    [InlineData("Power 7", "Power 8")]
    [InlineData("On", "Off")]
    [InlineData("1/15", "2/15")]
    [InlineData("Caption", "caption")]
    [InlineData("Item!", "Item?")]
    [InlineData("Quantity -7", "Quantity 7")]
    [InlineData("Rate 7%", "Rate 7")]
    public void ChangedValue_ImmediatelyRevokesConfirmedVersion(string oldText, string newText)
    {
        var gate = Confirm(oldText);
        var oldVersion = gate.Version;
        Assert.True(gate.CanPublish(oldVersion, Ms(200)));
        Assert.True(gate.Observe(Origin(400), [newText], Ms(400)));
        Assert.False(gate.CanPublish(oldVersion, Ms(400)));
        Assert.False(gate.Confirmed);
        Assert.True(gate.Observe(Origin(600), [newText], Ms(600)));
        Assert.True(gate.CanPublish(gate.Version, Ms(600)));
        Assert.False(gate.CanPublish(oldVersion, Ms(600)));
    }

    [Fact]
    public void NormalizesNfcAndWhitespace_ButKeepsGroupBoundaries()
    {
        var gate = new MenuTextAuthority(2, Ms(200));
        gate.Observe(Origin(0), ["  Cafe\u0301\t menu  ", "Action"], Ms(0));
        gate.Observe(Origin(200), ["Café menu", "Action"], Ms(200));
        Assert.True(gate.Confirmed);
        var version = gate.Version;
        gate.Observe(Origin(400), ["Café menu Action"], Ms(400));
        Assert.False(gate.CanPublish(version, Ms(400)));
    }

    [Fact]
    public void DuplicateOutOfOrderUnknownContextAndOldCompletion_CannotConfirmOrRenew()
    {
        var gate = new MenuTextAuthority(2, Ms(200));
        gate.Observe(Origin(0), ["Action"], Ms(0));
        Assert.False(gate.Observe(Origin(0) with { CapturedAt = DateTimeOffset.MaxValue }, ["Action"], Ms(200)));
        Assert.False(gate.Confirmed);
        gate.Observe(Origin(200), ["Action"], Ms(200));
        var version = gate.Version;
        Assert.False(gate.Observe(Origin(100), ["Other"], Ms(400)));
        Assert.False(gate.Observe(Origin(400) with { SourceId = Guid.NewGuid() }, ["Other"], Ms(400)));
        Assert.True(gate.CanPublish(version, Ms(2200)));
        Assert.False(gate.Observe(Origin(200), ["Action"], Ms(2201)));
        Assert.False(gate.CanPublish(version, Ms(2201)));
        gate.Observe(Origin(2201), ["Action"], Ms(2201));
        Assert.False(gate.Confirmed);
        gate.Observe(Origin(2401), ["Action"], Ms(2401));
        Assert.True(gate.Confirmed);
        Assert.False(gate.CanPublish(version, Ms(2401)));
    }

    [Fact]
    public void EmptyResultRevokesVersion_AndNewTextNeedsTwoFramesAgain()
    {
        var gate = Confirm("Action");
        var version = gate.Version;
        gate.Observe(Origin(400), [], Ms(400));
        Assert.False(gate.CanPublish(version, Ms(400)));
        gate.Observe(Origin(600), ["Action"], Ms(600));
        Assert.False(gate.Confirmed);
        gate.Observe(Origin(800), ["Action"], Ms(800));
        Assert.True(gate.Confirmed);
    }

    [Fact]
    public void StricterUserCountAndDuration_AreBothPreserved()
    {
        var gate = new MenuTextAuthority(3, Ms(1000));
        gate.Observe(Origin(0), ["Action"], Ms(0));
        gate.Observe(Origin(200), ["Action"], Ms(200));
        gate.Observe(Origin(400), ["Action"], Ms(400));
        Assert.False(gate.Confirmed);
        gate.Observe(Origin(1000), ["Action"], Ms(1000));
        Assert.True(gate.Confirmed);
    }

    [Fact]
    public void SameTextRenewsFreshnessWithoutChangingVersion_UsingSourceTime()
    {
        var gate = Confirm("Action");
        var version = gate.Version;
        for (var time = 700; time <= 10200; time += 500)
        {
            gate.Observe(Origin(time), ["Action"], Ms(time + 100));
            Assert.True(gate.CanPublish(version, Ms(time + 100)));
            Assert.Equal(version, gate.Version);
        }
        Assert.True(gate.CanPublish(version, Ms(12200)));
        Assert.False(gate.CanPublish(version, Ms(12201)));
    }

    private static MenuTextAuthority Confirm(string text)
    {
        var gate = new MenuTextAuthority(2, Ms(200));
        gate.Observe(Origin(0), [text], Ms(0));
        gate.Observe(Origin(200), [text], Ms(200));
        return gate;
    }
}
