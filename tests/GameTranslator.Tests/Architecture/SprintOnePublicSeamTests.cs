using GameTranslator.Application.Abstractions;

namespace GameTranslator.Tests.Architecture;

public sealed class SprintOnePublicSeamTests
{
    [Fact]
    public void ApplicationLayer_ExposesNavigationDialogSettingsAndLoggingAbstractions()
    {
        AssertPublicInterface<INavigationService>();
        AssertPublicInterface<IDialogService>();
        AssertPublicInterface<ISettingsService>();
        AssertPublicInterface<IApplicationLogger>();
    }

    [Fact]
    public void NavigationServiceContract_UsesPublicViewModelNavigationSeam()
    {
        var interfaceType = typeof(INavigationService);

        Assert.NotNull(interfaceType.GetProperty(nameof(INavigationService.CurrentViewModel)));
        Assert.NotNull(interfaceType.GetMethod(nameof(INavigationService.NavigateTo)));
    }

    [Fact]
    public void ServiceContracts_DoNotDependOnPresentationOrInfrastructureTypes()
    {
        var applicationAbstractions = new[]
        {
            typeof(INavigationService),
            typeof(IDialogService),
            typeof(ISettingsService),
            typeof(IApplicationLogger),
        };

        foreach (var abstraction in applicationAbstractions)
        {
            Assert.DoesNotContain(
                abstraction.GetMembers().SelectMany(member => member.GetCustomAttributesData()),
                attribute => attribute.AttributeType.Namespace?.StartsWith("GameTranslator.UI", StringComparison.Ordinal) == true
                    || attribute.AttributeType.Namespace?.StartsWith("GameTranslator.Infrastructure", StringComparison.Ordinal) == true);

            Assert.DoesNotContain(
                abstraction.GetMethods().SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)),
                type => type.Namespace?.StartsWith("GameTranslator.UI", StringComparison.Ordinal) == true
                    || type.Namespace?.StartsWith("GameTranslator.Infrastructure", StringComparison.Ordinal) == true);
        }
    }

    private static void AssertPublicInterface<TInterface>()
    {
        var type = typeof(TInterface);

        Assert.True(type.IsInterface, $"{type.FullName} must be an interface.");
        Assert.True(type.IsPublic, $"{type.FullName} must be public.");
        Assert.Equal("GameTranslator.Application.Abstractions", type.Namespace);
    }
}
