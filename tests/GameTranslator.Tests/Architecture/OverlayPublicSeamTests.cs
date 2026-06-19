using GameTranslator.Application.Overlay;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Architecture;

public sealed class OverlayPublicSeamTests
{
    [Fact]
    public void ApplicationLayer_ExposesOverlayAbstractionsAsPublicSeams()
    {
        AssertPublicApplicationType<IOverlayService>();
        AssertPublicApplicationType<OverlaySnapshot>();
        AssertPublicApplicationType<OverlayTextItem>();
        AssertPublicApplicationType<OverlayMaskItem>();
        AssertPublicApplicationType<OverlayPositioningService>();
    }

    [Fact]
    public void OverlayContracts_DoNotDependOnPresentationOrInfrastructureTypes()
    {
        var overlayTypes = new[]
        {
            typeof(IOverlayService),
            typeof(OverlaySnapshot),
            typeof(OverlayTextItem),
            typeof(OverlayMaskItem),
            typeof(OverlayPositioningService),
        };

        foreach (var overlayType in overlayTypes)
        {
            Assert.DoesNotContain(
                overlayType.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
            Assert.DoesNotContain(
                overlayType.GetProperties(),
                property => IsPresentationOrInfrastructureType(property.PropertyType));
            Assert.DoesNotContain(
                overlayType.GetMethods().SelectMany(method => method.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
        }
    }

    [Fact]
    public void OverlayTextItem_RejectsInvalidBounds()
    {
        Assert.Throws<ArgumentException>(() => new OverlayTextItem(string.Empty, 0, 0, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayTextItem("text", -1, 0, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayTextItem("text", 0, -1, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayTextItem("text", 0, 0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayTextItem("text", 0, 0, 10, 0));
    }

    [Fact]
    public void OverlayMaskItem_RejectsInvalidMaskSettingsAndBounds()
    {
        Assert.Throws<ArgumentException>(() => new OverlayMaskItem(OverlayMaskMode.Solid, string.Empty, 1, 0, 0, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayMaskItem(OverlayMaskMode.Solid, "#000000", -0.1, 0, 0, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayMaskItem(OverlayMaskMode.Solid, "#000000", 1.1, 0, 0, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayMaskItem(OverlayMaskMode.Solid, "#000000", 1, -1, 0, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayMaskItem(OverlayMaskMode.Solid, "#000000", 1, 0, -1, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayMaskItem(OverlayMaskMode.Solid, "#000000", 1, 0, 0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OverlayMaskItem(OverlayMaskMode.Solid, "#000000", 1, 0, 0, 10, 0));
    }

    private static void AssertPublicApplicationType<TType>()
    {
        var type = typeof(TType);

        Assert.True(type.IsPublic, $"{type.FullName} must be public.");
        Assert.Equal("GameTranslator.Application.Overlay", type.Namespace);
    }

    private static bool IsPresentationOrInfrastructureType(Type type)
    {
        return type.Namespace?.StartsWith("GameTranslator.UI", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("GameTranslator.Infrastructure", StringComparison.Ordinal) == true;
    }
}
