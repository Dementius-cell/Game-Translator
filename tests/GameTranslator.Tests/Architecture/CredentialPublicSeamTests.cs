using GameTranslator.Application.Credentials;

namespace GameTranslator.Tests.Architecture;

public sealed class CredentialPublicSeamTests
{
    [Fact]
    public void ApplicationLayer_ExposesCredentialAbstractionsAsPublicSeams()
    {
        AssertPublicApplicationType<ICredentialStorage>();
        AssertPublicApplicationType<TranslatorCredentialRecord>();
        AssertPublicApplicationType<TranslatorCredentialService>();
        AssertPublicApplicationType<CredentialStorageException>();
    }

    [Fact]
    public void CredentialContracts_DoNotDependOnPresentationOrInfrastructureTypes()
    {
        var credentialTypes = new[]
        {
            typeof(ICredentialStorage),
            typeof(TranslatorCredentialRecord),
            typeof(TranslatorCredentialService),
            typeof(CredentialStorageException),
        };

        foreach (var credentialType in credentialTypes)
        {
            Assert.DoesNotContain(
                credentialType.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
            Assert.DoesNotContain(
                credentialType.GetProperties(),
                property => IsPresentationOrInfrastructureType(property.PropertyType));
            Assert.DoesNotContain(
                credentialType.GetMethods().SelectMany(method => method.GetParameters()),
                parameter => IsPresentationOrInfrastructureType(parameter.ParameterType));
        }
    }

    private static void AssertPublicApplicationType<TType>()
    {
        var type = typeof(TType);

        Assert.True(type.IsPublic, $"{type.FullName} must be public.");
        Assert.Equal("GameTranslator.Application.Credentials", type.Namespace);
    }

    private static bool IsPresentationOrInfrastructureType(Type type)
    {
        return type.Namespace?.StartsWith("GameTranslator.UI", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("GameTranslator.Infrastructure", StringComparison.Ordinal) == true;
    }
}
