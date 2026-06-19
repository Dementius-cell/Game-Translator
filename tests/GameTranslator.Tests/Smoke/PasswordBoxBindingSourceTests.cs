using System.IO;

namespace GameTranslator.Tests.Smoke;

public sealed class PasswordBoxBindingSourceTests
{
    [Fact]
    public void ShellView_EnablesPasswordBoxBindingForTranslatorCredentialSecret()
    {
        var shellViewSource = File.ReadAllText(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "Views",
                "ShellView.xaml"));

        Assert.Contains("PasswordBoxBinding.IsEnabled=\"True\"", shellViewSource, StringComparison.Ordinal);
        Assert.Contains("PasswordBoxBinding.BoundPassword=\"{Binding TranslatorCredentialSecret", shellViewSource, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordBoxBinding_SubscribesToPasswordChangedThroughExplicitAttachedFlag()
    {
        var bindingSource = File.ReadAllText(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "Behaviors",
                "PasswordBoxBinding.cs"));

        Assert.Contains("IsEnabledProperty", bindingSource, StringComparison.Ordinal);
        Assert.Contains("OnIsEnabledChanged", bindingSource, StringComparison.Ordinal);
        Assert.Contains("passwordBox.PasswordChanged += OnPasswordChanged", bindingSource, StringComparison.Ordinal);
    }
}
