using System.IO;
using System.Xml.Linq;

namespace GameTranslator.Tests.Smoke;

public sealed class ShellWorkspaceTabsSourceTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ShellView_DefinesSixWorkspaceTabsWithoutDuplicatingProfileNavigation()
    {
        var document = LoadShellView();
        var workspaceTabs = document
            .Descendants(Wpf + "TabControl")
            .Single(element => (string?)element.Attribute("SelectedIndex") ==
                "{Binding SelectedWorkspaceTabIndex, Mode=TwoWay}");
        var headers = workspaceTabs
            .Elements(Wpf + "TabItem")
            .Select(element => (string?)element.Attribute("Header"))
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Zones & OCR",
                "Translation",
                "Overlay",
                "Live & Diagnostics",
                "OCR Packs",
                "Hotkeys & Settings",
            },
            headers);
        Assert.DoesNotContain("Profile", headers);
        Assert.DoesNotContain(
            document.Descendants(Wpf + "TextBlock")
                .Single(element => (string?)element.Attribute("Text") == "Profiles")
                .Ancestors(),
            ancestor => ancestor == workspaceTabs);
        Assert.Equal(
            "{StaticResource WorkspaceTabControlStyle}",
            (string?)workspaceTabs.Attribute("Style"));
        Assert.All(
            workspaceTabs.Elements(Wpf + "TabItem"),
            tab => Assert.Equal(
                "{StaticResource WorkspaceTabItemStyle}",
                (string?)tab.Attribute("Style")));
    }

    [Theory]
    [InlineData("Translator settings", "IsTranslationTabSelected")]
    [InlineData("Overlay preview", "IsOverlayTabSelected")]
    [InlineData("Overlay settings", "IsOverlayTabSelected")]
    [InlineData("OCR language packs", "IsOcrPacksTabSelected")]
    [InlineData("Global hotkeys", "IsHotkeysSettingsTabSelected")]
    [InlineData("OCR zones", "IsZonesOcrOrLiveDiagnosticsTabSelected")]
    public void ShellView_RoutesMajorSectionsToTheirWorkspaceTab(string title, string visibilityProperty)
    {
        var document = LoadShellView();
        var titleElement = document
            .Descendants(Wpf + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == title);
        var section = titleElement
            .Ancestors(Wpf + "Border")
            .First(element => element.Attribute("Visibility") is not null);

        Assert.Equal(
            $"{{Binding {visibilityProperty}, Converter={{StaticResource BooleanToVisibilityConverter}}}}",
            (string?)section.Attribute("Visibility"));
    }

    [Fact]
    public void ShellView_UsesOneLiveControlBlockOnlyOnLiveDiagnosticsTab()
    {
        var document = LoadShellView();
        var startButtons = document
            .Descendants(Wpf + "Button")
            .Where(element => (string?)element.Attribute("Content") == "Start live")
            .ToArray();
        var stopButtons = document
            .Descendants(Wpf + "Button")
            .Where(element => (string?)element.Attribute("Content") == "Stop live")
            .ToArray();
        Assert.Equal(2, startButtons.Length);
        Assert.Equal(2, stopButtons.Length);
        var startButton = startButtons.Single(button =>
            button.Ancestors(Wpf + "Border")
                .Any(element => (string?)element.Attribute("Visibility") ==
                    "{Binding IsLiveDiagnosticsTabSelected, Converter={StaticResource BooleanToVisibilityConverter}}"));
        var stopButton = stopButtons.Single(button =>
            button.Ancestors(Wpf + "Border")
                .Any(element => (string?)element.Attribute("Visibility") ==
                    "{Binding IsLiveDiagnosticsTabSelected, Converter={StaticResource BooleanToVisibilityConverter}}"));
        var liveSection = startButton
            .Ancestors(Wpf + "Border")
            .First(element => element.Attribute("Visibility") is not null);

        Assert.Same(liveSection, stopButton.Ancestors(Wpf + "Border").First(element => element.Attribute("Visibility") is not null));
        Assert.Equal(
            "{Binding IsLiveDiagnosticsTabSelected, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)liveSection.Attribute("Visibility"));
        Assert.Contains(
            liveSection.Descendants(Wpf + "Button"),
            element => (string?)element.Attribute("Content") == "Open live reports");
        Assert.Equal(
            "{StaticResource PrimaryButtonStyle}",
            (string?)startButton.Attribute("Style"));

        var headerStartButton = Assert.Single(startButtons.Except(new[] { startButton }));
        var headerStopButton = Assert.Single(stopButtons.Except(new[] { stopButton }));
        Assert.Equal("{Binding StartLiveTranslationCommand}", (string?)headerStartButton.Attribute("Command"));
        Assert.Equal("{Binding StopLiveTranslationCommand}", (string?)headerStopButton.Attribute("Command"));
    }

    [Fact]
    public void ShellView_UsesTheApprovedCompactProfileHeaderAndModernVisualResources()
    {
        var document = LoadShellView();
        var resources = document
            .Root!
            .Element(Wpf + "UserControl.Resources")!;
        var resourceKeys = resources
            .Elements(Wpf + "Style")
            .Select(style => (string?)style.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(key => key is not null)
            .ToArray();

        Assert.Contains("CardBorderStyle", resourceKeys);
        Assert.Contains("PrimaryButtonStyle", resourceKeys);
        Assert.Contains("WorkspaceTabControlStyle", resourceKeys);
        Assert.Contains("WorkspaceTabItemStyle", resourceKeys);
        Assert.Contains(
            document.Descendants(Wpf + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding ApplicationName}"
                && (string?)element.Attribute("FontSize") == "22");
        Assert.Contains(
            document.Descendants(Wpf + "Run"),
            element => (string?)element.Attribute("Text") == "{Binding ActiveProfileName, Mode=OneWay}");

        var profileDetails = document
            .Descendants(Wpf + "Expander")
            .Single(element => (string?)element.Attribute("Header") == "Edit profile details");
        Assert.Equal("False", (string?)profileDetails.Attribute("IsExpanded"));
    }

    [Fact]
    public void ShellView_UsesDenseReadableSpacingScaleAcrossSharedChrome()
    {
        var document = LoadShellView();
        var resources = document.Root!.Element(Wpf + "UserControl.Resources")!;

        AssertStyleSetter(resources, "ModernButtonStyle", "MinHeight", "30");
        AssertStyleSetter(resources, "ModernButtonStyle", "Padding", "10,4");
        AssertStyleSetter(resources, "PrimaryButtonStyle", "MinHeight", "32");
        AssertStyleSetter(resources, "CompactButtonStyle", "MinHeight", "26");
        AssertStyleSetter(resources, "CardBorderStyle", "Padding", "10");
        AssertStyleSetter(resources, "SubtleCardBorderStyle", "Padding", "8");
        AssertStyleSetter(resources, "WorkspaceTabItemStyle", "Padding", "10,7");

        var textBoxStyle = resources
            .Elements(Wpf + "Style")
            .Single(style => style.Attribute(Xaml + "Key") is null
                && (string?)style.Attribute("TargetType") == "TextBox");
        Assert.Equal(
            "30",
            (string?)textBoxStyle.Elements(Wpf + "Setter")
                .Single(setter => (string?)setter.Attribute("Property") == "MinHeight")
                .Attribute("Value"));

        var workspaceTemplate = resources.Elements(Wpf + "DataTemplate").Single();
        var workspaceGrid = workspaceTemplate.Element(Wpf + "Grid")!;
        var workspaceColumns = workspaceGrid
            .Element(Wpf + "Grid.ColumnDefinitions")!
            .Elements(Wpf + "ColumnDefinition")
            .Select(column => (string?)column.Attribute("Width"))
            .ToArray();
        Assert.Equal(new[] { "280", "1", "*" }, workspaceColumns);

        var sidebar = workspaceGrid
            .Elements(Wpf + "Border")
            .Single(border => (string?)border.Attribute("Grid.Column") == "0");
        Assert.Equal("14,10,14,10", (string?)sidebar.Attribute("Padding"));

        var workspaceScrollViewer = document
            .Descendants(Wpf + "ScrollViewer")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "WorkspaceScrollViewer");
        Assert.Equal("18,12,18,16", (string?)workspaceScrollViewer.Element(Wpf + "Border")!.Attribute("Padding"));

        var rootGrid = document.Root!.Elements(Wpf + "Grid").Single();
        var rootRows = rootGrid
            .Element(Wpf + "Grid.RowDefinitions")!
            .Elements(Wpf + "RowDefinition")
            .Select(row => (string?)row.Attribute("Height"))
            .ToArray();
        Assert.Equal(new[] { "40", "*", "24" }, rootRows);
    }

    [Fact]
    public void ShellView_ShowsPipelineWarningsAndErrorsWithDistinctStatusColors()
    {
        var document = LoadShellView();
        var resources = document.Root!.Element(Wpf + "UserControl.Resources")!;
        var statusStyle = resources
            .Elements(Wpf + "Style")
            .Single(style => (string?)style.Attribute(Xaml + "Key") == "PipelineStatusTextStyle");
        var triggers = statusStyle
            .Element(Wpf + "Style.Triggers")!
            .Elements(Wpf + "DataTrigger")
            .ToDictionary(
                trigger => (string)trigger.Attribute("Value")!,
                trigger => (string)trigger.Element(Wpf + "Setter")!.Attribute("Value")!);

        Assert.Equal("#B45309", triggers["Warning"]);
        Assert.Equal("#B91C1C", triggers["Error"]);
        Assert.All(
            statusStyle.Element(Wpf + "Style.Triggers")!.Elements(Wpf + "DataTrigger"),
            trigger => Assert.Equal(
                "{Binding PipelineStatusSeverity}",
                (string?)trigger.Attribute("Binding")));

        var pipelineStatus = document
            .Descendants(Wpf + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding PipelineStatus}");
        Assert.Equal("{StaticResource PipelineStatusTextStyle}", (string?)pipelineStatus.Attribute("Style"));
    }

    [Fact]
    public void ShellView_ProfileToolbarKeepsAllFourActionsReadable()
    {
        var document = LoadShellView();
        var commands = new[]
        {
            "{Binding BeginCreateProfileCommand}",
            "{Binding ImportProfileCommand}",
            "{Binding ExportSelectedProfileCommand}",
            "{Binding RefreshProfilesCommand}",
        };

        foreach (var command in commands)
        {
            var button = document
                .Descendants(Wpf + "Button")
                .Single(element => (string?)element.Attribute("Command") == command);
            Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("Content")));
            Assert.Equal(
                "{StaticResource CompactButtonStyle}",
                (string?)button.Attribute("Style"));
        }
    }

    [Fact]
    public void ShellView_TranslationCardIsCompactAndHidesUnusedCredentialFieldsForWebProviders()
    {
        var document = LoadShellView();
        var translatorTitle = document
            .Descendants(Wpf + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "Translator settings");
        var translationCard = translatorTitle
            .Ancestors(Wpf + "Border")
            .First(element => element.Attribute("Visibility") is not null);

        Assert.Equal("700", (string?)translationCard.Attribute("MaxWidth"));
        Assert.Equal("Left", (string?)translationCard.Attribute("HorizontalAlignment"));

        var credentialVisibility = "{Binding RequiresStoredTranslatorCredentials, Converter={StaticResource BooleanToVisibilityConverter}}";
        foreach (var label in new[] { "Endpoint", "Project / folder id", "Region / location", "API key / token" })
        {
            var fieldLabel = document
                .Descendants(Wpf + "TextBlock")
                .Single(element => (string?)element.Attribute("Text") == label);
            Assert.Equal(credentialVisibility, (string?)fieldLabel.Attribute("Visibility"));
        }
    }

    [Fact]
    public void ShellView_ExposesInlineProfileRenameAndMovableZoneSurface()
    {
        var document = LoadShellView();

        var profileRenameEditor = document
            .Descendants(Wpf + "TextBox")
            .Single(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "ProfileRenameTextBox");
        Assert.Equal("OnProfileRenameKeyDown", (string?)profileRenameEditor.Attribute("KeyDown"));
        Assert.Equal("OnProfileRenameLostKeyboardFocus", (string?)profileRenameEditor.Attribute("LostKeyboardFocus"));

        var zone = document
            .Descendants(Wpf + "Grid")
            .Single(element => (string?)element.Attribute("MouseLeftButtonDown") == "OnZoneSurfaceZoneMouseLeftButtonDown");
        Assert.Equal("SizeAll", (string?)zone.Attribute("Cursor"));

        Assert.Contains(
            document.Descendants(Wpf + "Button"),
            element => (string?)element.Attribute("Content") == "Earlier"
                && ((string?)element.Attribute("ToolTip"))?.Contains("processing order", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(
            document.Descendants(Wpf + "Button"),
            element => (string?)element.Attribute("Content") == "Later"
                && ((string?)element.Attribute("ToolTip"))?.Contains("processing order", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void MainWindow_OpensAtAUsableCenteredWorkspaceSize()
    {
        var document = XDocument.Load(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "MainWindow.xaml"));
        var window = document.Root!;

        Assert.Equal("1280", (string?)window.Attribute("Width"));
        Assert.Equal("820", (string?)window.Attribute("Height"));
        Assert.Equal("1024", (string?)window.Attribute("MinWidth"));
        Assert.Equal("640", (string?)window.Attribute("MinHeight"));
        Assert.Equal("CenterScreen", (string?)window.Attribute("WindowStartupLocation"));
    }

    private static XDocument LoadShellView()
    {
        return XDocument.Load(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "Views",
                "ShellView.xaml"));
    }

    private static void AssertStyleSetter(
        XElement resources,
        string styleKey,
        string property,
        string expectedValue)
    {
        var style = resources
            .Elements(Wpf + "Style")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == styleKey);
        var setter = style
            .Elements(Wpf + "Setter")
            .Single(element => (string?)element.Attribute("Property") == property);
        Assert.Equal(expectedValue, (string?)setter.Attribute("Value"));
    }
}
