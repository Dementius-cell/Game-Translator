using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Application.Overlay;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Smoke;

[Collection(WpfResourceCollection.Name)]
public sealed class WpfTextFitTests
{
    private static Assembly LoadUi() => Assembly.LoadFrom(Environment.GetEnvironmentVariable("GAME_TRANSLATOR_DPI_UI_ASSEMBLY")
        ?? Path.Combine(RepositoryRoot.Find(), "src/GameTranslator.UI/bin/Release/net9.0-windows10.0.19041.0/GameTranslator.UI.dll"));

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        Assert.Null(failure);
    }

    [Fact]
    public void RussianLongWord_IsReportedAsOverflowInsteadOfSplittingIntoLetters() => OnSta(() =>
    {
        var measurer = (IOverlayTextMeasurer)Activator.CreateInstance(LoadUi().GetType("GameTranslator.UI.Services.WpfOverlayTextMeasurer", true)!)!;
        var measured = measurer.Measure(new OverlayTextMeasurementRequest("Достопримечательность", new OcrZoneTextStyle { FontSize = 24 }, 60));
        Assert.Single(measured.Lines);
        Assert.True(measured.Lines[0].HasOverflowed);
        Assert.True(measured.Width > 60);
    });

    [Fact]
    public void AutoFont_RussianTextFitsWithWholeWordLineBreaks() => OnSta(() =>
    {
        var measurer = (IOverlayTextMeasurer)Activator.CreateInstance(LoadUi().GetType("GameTranslator.UI.Services.WpfOverlayTextMeasurer", true)!)!;
        const string text = "Ответственность помогает принимать правильные решения.";
        var frame = new CapturedFrame(new CaptureRegion(0, 0, 500, 500), 500, 500, 2000, "Bgra32", new byte[1000000], DateTimeOffset.UnixEpoch);
        var result = new OcrResult(new OcrRequest(frame, "en", "test"), new[] { new OcrTextBlock(text, new BoundingBox(100, 100, 120, 180)) }, DateTimeOffset.UnixEpoch);
        var item = Assert.Single(new OverlayPositioningService(measurer).CreateSnapshot(result, DateTimeOffset.UnixEpoch, null,
            new OcrZoneTextStyle { AutoFontSize = true, LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter }).TextItems);
        var measured = measurer.Measure(new OverlayTextMeasurementRequest(text, item.TextStyle, item.Width - 16));
        Assert.All(measured.Lines, line => Assert.False(line.HasOverflowed));
        Assert.True(measured.Height + 10 <= item.Height);
        var end = 0;
        foreach (var line in measured.Lines)
        {
            end += line.TextLength;
            if (end > 0 && end < text.Length)
                Assert.False(char.IsLetter(text[end - 1]) && char.IsLetter(text[end]), "Line splits a word.");
        }
    });

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2d)]
    public void ActualTemplate_ContainsLastLineEvenWhenFrameIsTooShort(double dpi) => OnSta(() =>
    {
        var type = LoadUi().GetType("GameTranslator.UI.Views.OverlayWindow", true)!;
        var modelType = type.GetNestedType("OverlayWindowTextItemViewModel", BindingFlags.NonPublic)!;
        var item = new OverlayTextItem("Проверяем последнюю строку: щука, цифры и буквы.", 100, 100, 240, 28,
            new OcrZoneTextStyle { FontSize = 24, LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter });
        var model = modelType.GetMethod("FromDevicePixels")!.Invoke(null, new object[] { item, new Matrix(1 / dpi, 0, 0, 1 / dpi, 0, 0) });
        var window = (Window)Activator.CreateInstance(type)!;
        try
        {
            var template = ((ItemsControl)((Canvas)window.Content).Children[2]).ItemTemplate;
            var border = (Border)template.LoadContent();
            border.DataContext = model;
            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            border.Arrange(new Rect(border.DesiredSize));
            border.UpdateLayout();
            var element = ((Grid)border.Child).Children[2];
            var text = element is Viewbox box ? (TextBlock)box.Child : (TextBlock)element;
            var bounds = text.TransformToAncestor(border).TransformBounds(new Rect(text.RenderSize));
            Assert.True(bounds.Top >= 0 && bounds.Bottom <= border.ActualHeight,
                $"Text bounds {bounds} exceed frame height {border.ActualHeight} at {dpi}.");
        }
        finally { window.Close(); }
    });
}
