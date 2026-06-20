using GameTranslator.Application.Hotkeys;

namespace GameTranslator.Tests.Architecture;

public sealed class HotkeyPublicSeamTests
{
    [Fact]
    public void ApplicationLayer_ExposesHotkeyAbstractionsAsPublicSeams()
    {
        AssertPublicApplicationType<GlobalHotkeyAction>();
        AssertPublicApplicationType<GlobalHotkeyModifiers>();
        AssertPublicApplicationType<GlobalHotkeyGesture>();
        AssertPublicApplicationType<GlobalHotkeyBinding>();
        AssertPublicApplicationType<GlobalHotkeyRegistration>();
        AssertPublicApplicationType<GlobalHotkeyRegistrationResult>();
        AssertPublicApplicationType<GlobalHotkeyRegistrationStatus>();
        AssertPublicApplicationType<GlobalHotkeyConfigurationResult>();
        AssertPublicApplicationType<GlobalHotkeyPressedEventArgs>();
        AssertPublicApplicationType<GlobalHotkeyRegisteredEventArgs>();
        AssertPublicApplicationType<IGlobalHotkeyRegistrar>();
        AssertPublicApplicationType<GlobalHotkeyService>();
    }

    [Fact]
    public void HotkeyContracts_DoNotDependOnPresentationOrInfrastructureTypes()
    {
        var publicTypes = new[]
        {
            typeof(GlobalHotkeyGesture),
            typeof(GlobalHotkeyBinding),
            typeof(GlobalHotkeyRegistration),
            typeof(GlobalHotkeyRegistrationResult),
            typeof(GlobalHotkeyRegistrationStatus),
            typeof(GlobalHotkeyConfigurationResult),
            typeof(GlobalHotkeyPressedEventArgs),
            typeof(GlobalHotkeyRegisteredEventArgs),
            typeof(IGlobalHotkeyRegistrar),
            typeof(GlobalHotkeyService),
        };

        foreach (var type in publicTypes)
        {
            Assert.DoesNotContain(type.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => IsForbiddenDependency(parameter.ParameterType));
            Assert.DoesNotContain(type.GetProperties(), property => IsForbiddenDependency(property.PropertyType));
            Assert.DoesNotContain(type.GetEvents(), @event => IsForbiddenDependency(@event.EventHandlerType!));
        }
    }

    private static void AssertPublicApplicationType<TType>()
    {
        var type = typeof(TType);

        Assert.True(type.IsPublic || type.IsNestedPublic);
        Assert.Equal("GameTranslator.Application.Hotkeys", type.Namespace);
        Assert.DoesNotContain(
            type.GetCustomAttributes(inherit: false),
            attribute => IsForbiddenDependency(attribute.GetType()));
    }

    private static bool IsForbiddenDependency(Type type)
    {
        if (type.IsGenericType)
        {
            return type.GetGenericArguments().Any(IsForbiddenDependency);
        }

        return type.Namespace?.StartsWith("GameTranslator.UI", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("GameTranslator.Infrastructure", StringComparison.Ordinal) == true;
    }
}
