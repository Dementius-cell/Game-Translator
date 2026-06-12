using GameTranslator.Application.Capture;

namespace GameTranslator.Tests.Architecture;

public sealed class CapturePublicSeamTests
{
    [Fact]
    public void ApplicationLayer_ExposesCaptureAbstractionsAsPublicSeams()
    {
        AssertPublicApplicationType<ICaptureFrameSource>();
        AssertPublicApplicationType<CaptureRegion>();
        AssertPublicApplicationType<CapturedFrame>();
        AssertPublicApplicationType<CaptureSession>();
        AssertPublicApplicationType<CaptureSessionOptions>();
        AssertPublicApplicationType<CaptureRefreshMetrics>();
        AssertPublicApplicationType<CaptureRefreshResult>();
        AssertPublicApplicationType<CaptureService>();
    }

    [Fact]
    public void CaptureFrameSourceContract_UsesApplicationCaptureModels()
    {
        var captureMethod = typeof(ICaptureFrameSource).GetMethod(nameof(ICaptureFrameSource.CaptureAsync));

        Assert.NotNull(captureMethod);
        Assert.Equal(typeof(Task<CapturedFrame>), captureMethod.ReturnType);
        Assert.Contains(captureMethod.GetParameters(), parameter => parameter.ParameterType == typeof(CaptureRegion));
        Assert.Contains(captureMethod.GetParameters(), parameter => parameter.ParameterType == typeof(CancellationToken));
    }

    [Fact]
    public void CaptureContracts_DoNotDependOnPresentationOrInfrastructureTypes()
    {
        var captureTypes = new[]
        {
            typeof(ICaptureFrameSource),
            typeof(CaptureRegion),
            typeof(CapturedFrame),
            typeof(CaptureSession),
            typeof(CaptureSessionOptions),
            typeof(CaptureRefreshMetrics),
            typeof(CaptureRefreshResult),
            typeof(CaptureService),
        };

        foreach (var captureType in captureTypes)
        {
            Assert.DoesNotContain(
                captureType.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
            Assert.DoesNotContain(
                captureType.GetProperties(),
                property => IsPresentationOrInfrastructureType(property.PropertyType));
            Assert.DoesNotContain(
                captureType.GetMethods().SelectMany(method => method.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
        }
    }

    private static void AssertPublicApplicationType<TType>()
    {
        var type = typeof(TType);

        Assert.True(type.IsPublic, $"{type.FullName} must be public.");
        Assert.Equal("GameTranslator.Application.Capture", type.Namespace);
    }

    private static bool IsPresentationOrInfrastructureType(Type type)
    {
        return type.Namespace?.StartsWith("GameTranslator.UI", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("GameTranslator.Infrastructure", StringComparison.Ordinal) == true;
    }
}
