using GameTranslator.Application.Cache;

namespace GameTranslator.Tests.Architecture;

public sealed class CachePublicSeamTests
{
    [Fact]
    public void ApplicationLayer_ExposesCacheAbstractionsAsPublicSeams()
    {
        AssertPublicApplicationType<ITranslationCacheRepository>();
        AssertPublicApplicationType<TranslationCacheService>();
        AssertPublicApplicationType<TranslationCacheKey>();
        AssertPublicApplicationType<TranslationCacheEntry>();
        AssertPublicApplicationType<TranslationCacheOptions>();
        AssertPublicApplicationType<TranslationCacheStorageOptions>();
        AssertPublicApplicationType<TranslationCacheResult>();
        AssertPublicApplicationType<TranslationCacheCleanupResult>();
    }

    [Fact]
    public void CacheContracts_DoNotDependOnPresentationOrInfrastructureTypes()
    {
        var cacheTypes = new[]
        {
            typeof(ITranslationCacheRepository),
            typeof(TranslationCacheService),
            typeof(TranslationCacheKey),
            typeof(TranslationCacheEntry),
            typeof(TranslationCacheOptions),
            typeof(TranslationCacheStorageOptions),
            typeof(TranslationCacheResult),
            typeof(TranslationCacheCleanupResult),
        };

        foreach (var cacheType in cacheTypes)
        {
            Assert.DoesNotContain(
                cacheType.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
            Assert.DoesNotContain(
                cacheType.GetProperties(),
                property => IsPresentationOrInfrastructureType(property.PropertyType));
            Assert.DoesNotContain(
                cacheType.GetMethods().SelectMany(method => method.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
        }
    }

    private static void AssertPublicApplicationType<TType>()
    {
        var type = typeof(TType);

        Assert.True(type.IsPublic, $"{type.FullName} must be public.");
        Assert.Equal("GameTranslator.Application.Cache", type.Namespace);
    }

    private static bool IsPresentationOrInfrastructureType(Type type)
    {
        return type.Namespace?.StartsWith("GameTranslator.UI", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("GameTranslator.Infrastructure", StringComparison.Ordinal) == true;
    }
}
