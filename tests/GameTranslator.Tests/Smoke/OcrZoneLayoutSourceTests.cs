using System.IO;
using System.Xml.Linq;

namespace GameTranslator.Tests.Smoke;

public sealed class OcrZoneLayoutSourceTests
{
    [Fact]
    public void ShellView_SeparatesResponsiveZonePreviewInspectorAndCollapsedAdvancedSettings()
    {
        var document = LocalizedXaml.Load(Path.Combine(RepositoryRoot.Find(), "src", "GameTranslator.UI", "Views", "ShellView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var surface = document.Descendants(wpf + "Border").Single(e => GetName(e) == "ZoneSurfaceCard");
        var responsive = surface.Ancestors(wpf + "Grid").First();
        Assert.Equal("ZoneResponsivePanels", GetName(responsive));
        Assert.Equal("OnZoneWorkspaceSizeChanged", (string?)responsive.Attribute("SizeChanged"));
        Assert.Null(surface.Attribute("Width"));
        Assert.Contains(responsive.Elements(wpf + "Border"), e => GetName(e) == "ZoneInspector");
        var preview = surface.Descendants(wpf + "Expander").Single();
        Assert.Equal("True", (string?)preview.Attribute("IsExpanded"));
        var viewbox = preview.Descendants(wpf + "Viewbox").Single();
        Assert.Equal("Uniform", (string?)viewbox.Attribute("Stretch"));
        Assert.Equal("300", (string?)viewbox.Attribute("MaxHeight"));
        var advanced = document.Descendants(wpf + "Expander").Single(e => GetName(e) == "AdvancedZoneSettings");
        Assert.Equal("False", (string?)advanced.Attribute("IsExpanded"));
        Assert.Contains(advanced.Descendants(wpf + "Grid"), e => GetName(e) == "SelectedZoneParametersPanel");
        var zoneList = document.Descendants(wpf + "ListBox").Single(e => GetName(e) == "ZoneList");
        Assert.Equal("{Binding OcrZones}", (string?)zoneList.Attribute("ItemsSource"));
        Assert.Equal("{Binding SelectedZone, Mode=TwoWay}", (string?)zoneList.Attribute("SelectedItem"));
    }

    [Fact]
    public void ShellView_KeepsTheZoneWorkspaceCompactAndRoutesLanguagePacksToTheirOwnTab()
    {
        var document = LocalizedXaml.Load(
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
            scrollViewer => (string?)scrollViewer.Attribute("Height") == "360");
    }

    [Fact]
    public void ShellView_ExposesContentLayoutModeAndItsEffectivePolicy()
    {
        var document = LocalizedXaml.Load(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "Views",
                "ShellView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var modeSelectors = document
            .Descendants(wpf + "ComboBox")
            .Where(element => (string?)element.Attribute("ItemsSource") == "{Binding ContentLayoutModeOptions}").ToArray();

        Assert.Equal(2, modeSelectors.Length);
        Assert.All(modeSelectors, modeSelector => Assert.Equal(
            "{Binding SelectedZone.ContentLayoutMode, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged, ValidatesOnNotifyDataErrors=True}",
            (string?)modeSelector.Attribute("SelectedValue")));
        Assert.Contains(
            document.Descendants(wpf + "TextBlock"),
            element => (string?)element.Attribute("Text") == "{Binding SelectedZone.ContentLayoutPolicySummary}");
        Assert.DoesNotContain(
            document.Descendants(wpf + "CheckBox"),
            element => (string?)element.Attribute("Content") == "Expand translated text from source center");
    }

    [Fact]
    public void ShellView_ExposesACompactPerZoneDetectorPresetSelector()
    {
        var document = LocalizedXaml.Load(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "Views",
                "ShellView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var selector = document
            .Descendants(wpf + "ComboBox")
            .Single(element => (string?)element.Attribute("ItemsSource") == "{Binding CandidateDetectorPresetOptions}");

        Assert.Equal(
            "{Binding SelectedZone.DetectorPreset, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged, ValidatesOnNotifyDataErrors=True}",
            (string?)selector.Attribute("SelectedValue"));
        Assert.Contains(
            document.Descendants(wpf + "TextBlock"),
            element => (string?)element.Attribute("Text") == "Detector preset");
    }

    [Fact]
    public void ShellView_ExposesOrientationSpecificCandidateGroupingLimits()
    {
        var document = LocalizedXaml.Load(
            Path.Combine(
                RepositoryRoot.Find(),
                "src",
                "GameTranslator.UI",
                "Views",
                "ShellView.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Contains(
            document.Descendants(wpf + "TextBlock"),
            element => (string?)element.Attribute("Text") == "Candidate grouping - maximum horizontal lines");
        Assert.Contains(
            document.Descendants(wpf + "TextBlock"),
            element => (string?)element.Attribute("Text") == "Candidate grouping - maximum vertical columns");
        Assert.Contains(
            document.Descendants(wpf + "TextBlock"),
            element => (string?)element.Attribute("Text")
                == "Auto follows vertical CJK geometry without a fixed column count; a numeric value is a hard limit.");
        Assert.Contains(
            document.Descendants(wpf + "TextBox"),
            element => ((string?)element.Attribute("Text"))?.Contains("MaximumHorizontalCandidateLines", StringComparison.Ordinal) == true);
        Assert.Contains(
            document.Descendants(wpf + "TextBox"),
            element => ((string?)element.Attribute("Text"))?.Contains("MaximumVerticalCandidateColumns", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ShellView_OverlayHelpUsesItsOwnDeclaredGridRow()
    {
        var document = LocalizedXaml.Load(
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
