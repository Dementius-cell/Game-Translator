using System.IO;
using System.Xml.Linq;

namespace GameTranslator.Tests.Smoke;

public sealed class OcrZoneLayoutSourceTests
{
    [Fact]
    public void ShellView_RendersZoneSurfaceAndZoneDetailsInSeparateRows()
    {
        var document = XDocument.Load(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "Views",
                "ShellView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var surfaceTitle = document
            .Descendants(wpf + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "Interactive zone surface");
        var surfacePanel = surfaceTitle.Ancestors(wpf + "Border").First();
        var responsivePanel = surfacePanel.Ancestors(wpf + "WrapPanel").Single();

        Assert.Equal("0", (string?)responsivePanel.Attribute("Grid.Row"));
        Assert.Equal("ZoneResponsivePanels", GetName(responsivePanel));
        Assert.Equal("570", (string?)surfacePanel.Attribute("Width"));
        Assert.Contains(
            responsivePanel.Elements(wpf + "Border"),
            element => GetName(element) == "OcrPreprocessingCard"
                && (string?)element.Attribute("Width") == "530");

        var surfaceViewbox = surfacePanel
            .Descendants(wpf + "Viewbox")
            .Single();
        Assert.Equal("520", (string?)surfaceViewbox.Attribute("Width"));
        Assert.Equal("292.5", (string?)surfaceViewbox.Attribute("Height"));

        var zoneDetailsPanel = document
            .Descendants(wpf + "Grid")
            .Single(element => GetName(element) == "SelectedZoneParametersPanel");
        Assert.Equal("2", (string?)zoneDetailsPanel.Attribute("Grid.Row"));
        Assert.DoesNotContain(
            zoneDetailsPanel.Elements(wpf + "ListBox"),
            element => (string?)element.Attribute("ItemsSource") == "{Binding OcrZones}");
    }

    [Fact]
    public void ShellView_KeepsTheZoneWorkspaceCompactAndRoutesLanguagePacksToTheirOwnTab()
    {
        var document = XDocument.Load(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "Views",
                "ShellView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var languagePackChecklist = document
            .Descendants(wpf + "ItemsControl")
            .Single(element => (string?)element.Attribute("ItemsSource") == "{Binding OcrLanguagePackChecklistItems}");
        var settingsSection = languagePackChecklist
            .Ancestors(wpf + "Border")
            .First(element => element.Attribute("Visibility") is not null);

        Assert.Equal(
            "{Binding IsOcrPacksTabSelected, Converter={StaticResource BooleanToVisibilityConverter}}",
            (string?)settingsSection.Attribute("Visibility"));
        Assert.Contains(
            languagePackChecklist.Ancestors(wpf + "ScrollViewer"),
            scrollViewer => (string?)scrollViewer.Attribute("Height") == "430");
    }

    [Fact]
    public void ShellView_ExposesContentLayoutModeAndItsEffectivePolicy()
    {
        var document = XDocument.Load(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "Views",
                "ShellView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var modeSelector = document
            .Descendants(wpf + "ComboBox")
            .Single(element => (string?)element.Attribute("ItemsSource") == "{Binding ContentLayoutModeOptions}");

        Assert.Equal(
            "{Binding SelectedZone.ContentLayoutMode, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged, ValidatesOnNotifyDataErrors=True}",
            (string?)modeSelector.Attribute("SelectedValue"));
        Assert.Contains(
            document.Descendants(wpf + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding SelectedZone.ContentLayoutPolicySummary}");
        Assert.DoesNotContain(
            document.Descendants(wpf + "CheckBox"),
            element => (string?)element.Attribute("Content") == "Expand translated text from source center");
    }

    [Fact]
    public void ShellView_OverlayHelpUsesItsOwnDeclaredGridRow()
    {
        var document = XDocument.Load(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "Views",
                "ShellView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var helper = document
            .Descendants(wpf + "TextBlock")
            .Single(element => ((string?)element.Attribute("Text"))?.StartsWith("Use #RRGGBB", StringComparison.Ordinal) == true);
        var settingsGrid = helper.Parent!;
        var declaredRows = settingsGrid
            .Element(wpf + "Grid.RowDefinitions")!
            .Elements(wpf + "RowDefinition")
            .Count();

        Assert.Equal("5", (string?)helper.Attribute("Grid.Row"));
        Assert.True(declaredRows >= 6);
        Assert.Equal("Wrap", (string?)helper.Attribute("TextWrapping"));
    }

    private static string? GetName(XElement element)
    {
        return (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"));
    }
}
