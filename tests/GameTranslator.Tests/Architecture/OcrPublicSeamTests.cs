using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Architecture;

public sealed class OcrPublicSeamTests
{
    [Fact]
    public void ApplicationLayer_ExposesOcrAbstractionsAsPublicSeams()
    {
        AssertPublicApplicationType<IOcrEngine>();
        AssertPublicApplicationType<OcrRequest>();
        AssertPublicApplicationType<OcrResult>();
        AssertPublicApplicationType<OcrTextBlock>();
        AssertPublicApplicationType<BoundingBox>();
        AssertPublicApplicationType<OcrEngineException>();
        AssertPublicApplicationType<OcrService>();
    }

    [Fact]
    public void OcrEngineContract_UsesApplicationOcrAndCaptureModels()
    {
        var recognizeMethod = typeof(IOcrEngine).GetMethod(nameof(IOcrEngine.RecognizeAsync));

        Assert.NotNull(recognizeMethod);
        Assert.Equal(typeof(Task<OcrResult>), recognizeMethod.ReturnType);
        Assert.Contains(recognizeMethod.GetParameters(), parameter => parameter.ParameterType == typeof(OcrRequest));
        Assert.Contains(recognizeMethod.GetParameters(), parameter => parameter.ParameterType == typeof(CancellationToken));

        var requestProperties = typeof(OcrRequest).GetProperties();
        Assert.Contains(requestProperties, property => property.PropertyType == typeof(CapturedFrame));
        Assert.Contains(requestProperties, property => property.PropertyType == typeof(CaptureRegion));
    }

    [Fact]
    public void OcrContracts_DoNotDependOnPresentationOrInfrastructureTypes()
    {
        var ocrTypes = new[]
        {
            typeof(IOcrEngine),
            typeof(OcrRequest),
            typeof(OcrResult),
            typeof(OcrTextBlock),
            typeof(BoundingBox),
            typeof(OcrEngineException),
            typeof(OcrService),
        };

        foreach (var ocrType in ocrTypes)
        {
            Assert.DoesNotContain(
                ocrType.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
            Assert.DoesNotContain(
                ocrType.GetProperties(),
                property => IsPresentationOrInfrastructureType(property.PropertyType));
            Assert.DoesNotContain(
                ocrType.GetMethods().SelectMany(method => method.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
        }
    }

    private static void AssertPublicApplicationType<TType>()
    {
        var type = typeof(TType);

        Assert.True(type.IsPublic, $"{type.FullName} must be public.");
        Assert.Equal("GameTranslator.Application.Ocr", type.Namespace);
    }

    private static bool IsPresentationOrInfrastructureType(Type type)
    {
        return type.Namespace?.StartsWith("GameTranslator.UI", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("GameTranslator.Infrastructure", StringComparison.Ordinal) == true;
    }
}
