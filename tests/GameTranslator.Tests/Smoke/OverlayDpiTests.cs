using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameTranslator.Application.Overlay;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Tests.Smoke;

[Collection(WpfResourceCollection.Name)]
public sealed class OverlayDpiTests
{
    [Fact]
    public void ZoneEditor_PreservesAutoSizeThroughSaveAndDuplicate()
    {
        var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("GAME_TRANSLATOR_DPI_UI_ASSEMBLY") ?? Path.Combine(RepositoryRoot.Find(),
            "src/GameTranslator.UI/bin/Release/net9.0-windows10.0.19041.0/GameTranslator.UI.dll"));
        var type = assembly.GetType("GameTranslator.UI.ViewModels.OcrZoneEditorViewModel", true)!;
        var source = new OcrZone { Name = "Test", AbsoluteBounds = new(0, 0, 200, 100),
            RelativeBounds = new(0, 0, .2, .1), TextStyle = new() { AutoFontSize = true, FontSize = 22 } };
        var editor = type.GetMethod("FromModel")!.Invoke(null, new object[] { source })!;
        Assert.True((bool)type.GetProperty("OverlayAutoFontSize")!.GetValue(editor)!);
        var duplicate = type.GetMethod("CreateDuplicate")!.Invoke(editor, new object[] { "Copy" })!;
        var saved = (OcrZone)type.GetMethod("ToModel")!.Invoke(duplicate, null)!;
        Assert.True(saved.TextStyle.AutoFontSize);
        Assert.Equal(22, saved.TextStyle.FontSize);
        type.GetProperty("OverlayAutoFontSize")!.SetValue(editor, false);
        Assert.False(((OcrZone)type.GetMethod("ToModel")!.Invoke(editor, null)!).TextStyle.AutoFontSize);
    }

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(1.25, false)]
    [InlineData(1.5, false)]
    [InlineData(2.0, false)]
    [InlineData(1.0, true)]
    [InlineData(1.25, true)]
    [InlineData(1.5, true)]
    [InlineData(2.0, true)]
    public void TranslationTemplate_KeepsPhysicalFontPaddingAndTextFitAcrossDpi(double scale, bool auto)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
                var assemblyPath = Environment.GetEnvironmentVariable("GAME_TRANSLATOR_DPI_UI_ASSEMBLY")
                    ?? Path.Combine(RepositoryRoot.Find(), $"src/GameTranslator.UI/bin/{configuration}/net9.0-windows10.0.19041.0/GameTranslator.UI.dll");
                var assembly = Assembly.LoadFrom(assemblyPath);
                var windowType = assembly.GetType("GameTranslator.UI.Views.OverlayWindow", true)!;
                var modelType = windowType.GetNestedType("OverlayWindowTextItemViewModel", BindingFlags.NonPublic)!;
                var factory = modelType.GetMethod("FromDevicePixels", BindingFlags.Public | BindingFlags.Static)!;
                var item = new OverlayTextItem("A short synthetic translation that wraps onto several lines.",
                    100, 100, 220, 150, new OcrZoneTextStyle
                    { FontSize = 24, LayoutMode = OverlayTextLayoutMode.ExpandFromSourceCenter });
                if (auto)
                {
                    var measurer = (IOverlayTextMeasurer)Activator.CreateInstance(assembly.GetType("GameTranslator.UI.Services.WpfOverlayTextMeasurer", true)!)!;
                    var frame = new CapturedFrame(new CaptureRegion(0, 0, 500, 500), 500, 500, 2000,
                        "Bgra32", new byte[1000000], DateTimeOffset.UnixEpoch);
                    var result = new OcrResult(new OcrRequest(frame, "en", "test"),
                        new[] { new OcrTextBlock(item.Text, new BoundingBox(100, 100, 220, 150)) }, DateTimeOffset.UnixEpoch);
                    item = Assert.Single(new OverlayPositioningService(measurer).CreateSnapshot(result,
                        DateTimeOffset.UnixEpoch, null, item.TextStyle with { AutoFontSize = true }).TextItems);
                    Assert.InRange(item.TextStyle.FontSize, 24, 72);
                }
                var model = factory.Invoke(null, new object[] { item, new Matrix(1 / scale, 0, 0, 1 / scale, 0, 0) })!;
                double Read(string name) => (double)modelType.GetProperty(name)!.GetValue(model)!;
                Assert.Equal(item.TextStyle.FontSize, Read("FontSize") * scale, 6);
                Assert.Equal(item.Width + 4, Read("Width") * scale, 6);
                Assert.Equal(item.Height + 4, Read("Height") * scale, 6);
                Assert.Equal(16, (Read("Width") - Read("ContentWidth")) * scale, 6);

                var window = (Window)Activator.CreateInstance(windowType)!;
                try
                {
                    var canvas = (Canvas)window.Content;
                    var items = (ItemsControl)canvas.Children[2];
                    var border = (Border)items.ItemTemplate.LoadContent();
                    border.DataContext = model;
                    border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    border.Arrange(new Rect(border.DesiredSize));
                    border.UpdateLayout();
                    var grid = (Grid)border.Child;
                    var text = (TextBlock)((Viewbox)grid.Children[2]).Child;
                    text.Measure(new Size(Read("ContentWidth"), double.PositiveInfinity));
                    Assert.True(text.DesiredSize.Height <= Read("Height") - 2 / scale,
                        $"Text height {text.DesiredSize.Height} exceeds frame at {scale:P0}");
                    Assert.Equal(item.TextStyle.FontSize, text.FontSize * scale, 6);
                    var evidenceDirectory = Environment.GetEnvironmentVariable("GAME_TRANSLATOR_DPI_EVIDENCE");
                    if (!string.IsNullOrWhiteSpace(evidenceDirectory))
                    {
                        Directory.CreateDirectory(evidenceDirectory);
                        var bitmap = new RenderTargetBitmap(item.Width + 4, item.Height + 4, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        bitmap.Render(border);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(evidenceDirectory, $"overlay-{scale * 100:0}-auto-{auto}.png"));
                        encoder.Save(stream);
                    }
                }
                finally { window.Close(); }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF layout test timed out.");
        Assert.Null(failure);
    }
}
