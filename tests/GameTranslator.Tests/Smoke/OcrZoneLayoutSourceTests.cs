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

        Assert.Equal("2", (string?)surfacePanel.Attribute("Grid.Row"));

        var zoneDetailsPanels = document
            .Descendants(wpf + "Grid")
            .Where(IsZoneDetailsPanel)
            .ToArray();

        var zoneDetailsPanel = Assert.Single(zoneDetailsPanels);
        Assert.Equal("4", (string?)zoneDetailsPanel.Attribute("Grid.Row"));
    }

    private static bool IsZoneDetailsPanel(XElement element)
    {
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        return element
            .Elements(wpf + "ListBox")
            .Any(listBox => (string?)listBox.Attribute("ItemsSource") == "{Binding OcrZones}")
            && element
                .Elements(wpf + "Border")
                .Any(border => (string?)border.Attribute("Grid.Column") == "2"
                    && border
                        .Descendants(wpf + "TextBlock")
                        .Any(textBlock => (string?)textBlock.Attribute("Text") == "Zone name"));
    }
}
