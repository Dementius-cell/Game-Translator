using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;
using GameTranslator.Application.Ocr;

namespace GameTranslator.Tests.Smoke;

[Collection(WpfResourceCollection.Name)]
public sealed class LocalizedControlTests
{
    [Theory]
    [InlineData("ru-RU", "Light")]
    [InlineData("ru-RU", "Dark")]
    [InlineData("en-US", "Light")]
    [InlineData("en-US", "Dark")]
    public void OcrControlsAndArea_RenderLocalizedTextWithoutChangingCodesOrNames(string culture, string theme)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var numberCulture = CultureInfo.CurrentCulture;
                var root = RepositoryRoot.Find();
                var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "src/GameTranslator.UI/bin", configuration,
                    "net9.0-windows10.0.19041.0/GameTranslator.UI.dll"));
                Type Ui(string name) => assembly.GetType("GameTranslator.UI." + name, true)!;
                string Text(string key) => (string)Ui("Localization.UiText").GetMethod("Get")!.Invoke(null, new object[] { key })!;
                var converter = (IValueConverter)Activator.CreateInstance(Ui("Localization.LocalizedTextConverter"))!;
                var panel = new StackPanel();
                panel.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/GameTranslator.UI;component/Themes/{theme}.xaml"),
                });
                var itemType = Ui("ViewModels.OcrAutoLanguageItemViewModel");
                foreach (var language in TesseractLanguageCatalog.Languages)
                {
                    var item = Activator.CreateInstance(itemType, language.Code, language.Name, false, (Action)(() => { }))!;
                    var checkbox = new CheckBox { DataContext = item };
                    checkbox.SetBinding(ContentControl.ContentProperty, new Binding("DisplayName"));
                    checkbox.SetResourceReference(Control.ForegroundProperty, "ThemeText");
                    panel.Children.Add(checkbox);
                    checkbox.GetBindingExpression(ContentControl.ContentProperty)!.UpdateTarget();
                    var display = Assert.IsType<string>(checkbox.Content);
                    Assert.EndsWith($"({language.Code})", display);
                    Assert.Equal(culture == "ru-RU", display.Any(c => c is >= 'А' and <= 'я' or 'ё' or 'Ё'));
                    Assert.True((bool)itemType.GetMethod("Matches")!.Invoke(item, new object[] { language.Name })!);
                    Assert.True((bool)itemType.GetMethod("Matches")!.Invoke(item, new object[] { display.Split(" (")[0] })!);
                }
                var languageOption = Activator.CreateInstance(Ui("ViewModels.LanguageOption"), "jpn_vert", "Japanese vertical (Tesseract OCR)")!;
                Assert.Equal(culture == "ru-RU" ? "jpn_vert Японский (вертикальный) (Tesseract OCR)" : "jpn_vert Japanese vertical (Tesseract OCR)",
                    converter.Convert(languageOption, typeof(string), null!, numberCulture));

                // Read the real checklist binding so a missing converter remains observable.
                var shell = XDocument.Load(Path.Combine(root, "src/GameTranslator.UI/Views/ShellView.xaml"));
                var labelBinding = shell.Descendants().Attributes("Text").Single(a =>
                    a.Value.StartsWith("{Binding DisplayName,") && a.Value.Contains("LocalizedText")
                    && (string?)a.Parent!.Attribute("TextWrapping") == "Wrap");
                Assert.Contains("LocalizedText", labelBinding.Value);
                var label = new TextBlock { DataContext = new { DisplayName = "Tesseract Japanese vertical" } };
                label.SetBinding(TextBlock.TextProperty, new Binding("DisplayName") { Converter = converter });
                panel.Children.Add(label);
                label.GetBindingExpression(TextBlock.TextProperty)!.UpdateTarget();
                Assert.Equal(culture == "ru-RU" ? "Tesseract: японский (вертикальный)" : "Tesseract Japanese vertical", label.Text);

                Assert.Contains(shell.Descendants().Attributes("Text"), a => a.Value.Contains("StringFormat={loc:Text ZoneAreaFormat}"));
                var area = new TextBlock { DataContext = new { Area = 42 } };
                area.SetBinding(TextBlock.TextProperty, new Binding("Area") { StringFormat = Text("ZoneAreaFormat") });
                panel.Children.Add(area);
                area.GetBindingExpression(TextBlock.TextProperty)!.UpdateTarget();
                Assert.Equal(culture == "ru-RU" ? "Площадь: 42 пикс." : "Area 42 px", area.Text);

                var zone = Activator.CreateInstance(Ui("ViewModels.OcrZoneEditorViewModel"))!;
                zone.GetType().GetProperty("Name")!.SetValue(zone, "Ready 日本語");
                Assert.Equal("Ready 日本語", zone.GetType().GetProperty("DisplayName")!.GetValue(zone));
                panel.Measure(new Size(600, double.PositiveInfinity));
                panel.Arrange(new Rect(panel.DesiredSize));
                Assert.True(panel.DesiredSize.Height > 0);
                Assert.Equal(numberCulture, CultureInfo.CurrentCulture);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(failure);
    }
}
