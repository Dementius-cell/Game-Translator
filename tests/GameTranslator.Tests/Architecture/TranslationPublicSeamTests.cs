using GameTranslator.Application.Translation;

namespace GameTranslator.Tests.Architecture;

public sealed class TranslationPublicSeamTests
{
    [Fact]
    public void ApplicationLayer_ExposesTranslationAbstractionsAsPublicSeams()
    {
        AssertPublicApplicationType<ITranslatorProvider>();
        AssertPublicApplicationType<TranslateRequest>();
        AssertPublicApplicationType<TranslateResponse>();
        AssertPublicApplicationType<TranslatorCredentials>();
        AssertPublicApplicationType<TranslatorProviderException>();
    }

    [Fact]
    public void TranslatorProviderContract_UsesApplicationTranslationModels()
    {
        var translateMethod = typeof(ITranslatorProvider).GetMethod(nameof(ITranslatorProvider.TranslateAsync));

        Assert.NotNull(translateMethod);
        Assert.Equal(typeof(Task<TranslateResponse>), translateMethod.ReturnType);
        Assert.Contains(translateMethod.GetParameters(), parameter => parameter.ParameterType == typeof(TranslateRequest));
        Assert.Contains(translateMethod.GetParameters(), parameter => parameter.ParameterType == typeof(CancellationToken));
    }

    [Fact]
    public void TranslationContracts_DoNotDependOnPresentationOrInfrastructureTypes()
    {
        var translationTypes = new[]
        {
            typeof(ITranslatorProvider),
            typeof(TranslateRequest),
            typeof(TranslateResponse),
            typeof(TranslatorCredentials),
            typeof(TranslatorProviderException),
        };

        foreach (var translationType in translationTypes)
        {
            Assert.DoesNotContain(
                translationType.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
            Assert.DoesNotContain(
                translationType.GetProperties(),
                property => IsPresentationOrInfrastructureType(property.PropertyType));
            Assert.DoesNotContain(
                translationType.GetMethods().SelectMany(method => method.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
        }
    }

    private static void AssertPublicApplicationType<TType>()
    {
        var type = typeof(TType);

        Assert.True(type.IsPublic, $"{type.FullName} must be public.");
        Assert.Equal("GameTranslator.Application.Translation", type.Namespace);
    }

    private static bool IsPresentationOrInfrastructureType(Type type)
    {
        return type.Namespace?.StartsWith("GameTranslator.UI", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("GameTranslator.Infrastructure", StringComparison.Ordinal) == true;
    }
}
