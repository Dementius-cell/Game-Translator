using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using GameTranslator.Application.Overlay;
using GameTranslator.Domain.Profiles;
using WpfApplication = System.Windows.Application;

namespace GameTranslator.UI.Services;

public sealed class WpfOverlayTextMeasurer : IOverlayTextMeasurer
{
    public OverlayTextMeasurement Measure(OverlayTextMeasurementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            return dispatcher.Invoke(() => MeasureCore(request));
        }

        return MeasureCore(request);
    }

    private static OverlayTextMeasurement MeasureCore(OverlayTextMeasurementRequest request)
    {
        var runProperties = new OverlayTextRunProperties(request.TextStyle);
        var paragraphProperties = new OverlayTextParagraphProperties(runProperties);
        var textSource = new OverlayTextSource(request.Text, runProperties);
        var maxWidth = Math.Max(1, request.MaxWidth);
        var width = 0d;
        var height = 0d;
        var characterIndex = 0;
        var lines = new List<OverlayTextLineMeasurement>();
        TextLineBreak? previousLineBreak = null;

        using var formatter = TextFormatter.Create(TextFormattingMode.Ideal);
        while (characterIndex < textSource.TextLength)
        {
            using var line = formatter.FormatLine(
                textSource,
                characterIndex,
                maxWidth,
                paragraphProperties,
                previousLineBreak);
            var lineWidth = Math.Max(1, (int)Math.Ceiling(Math.Min(maxWidth, line.WidthIncludingTrailingWhitespace)));
            var lineHeight = Math.Max(1, (int)Math.Ceiling(line.Height));

            width = Math.Max(width, lineWidth);
            height += lineHeight;
            lines.Add(new OverlayTextLineMeasurement(
                lineWidth,
                lineHeight,
                Math.Max(0, line.Length),
                line.HasOverflowed));

            var nextCharacterIndex = characterIndex + line.Length;
            if (nextCharacterIndex <= characterIndex)
            {
                break;
            }

            previousLineBreak = line.NewlineLength > 0
                ? null
                : line.GetTextLineBreak();
            characterIndex = nextCharacterIndex;
        }

        return new OverlayTextMeasurement(
            Math.Max(1, (int)Math.Ceiling(width)),
            Math.Max(1, (int)Math.Ceiling(height)),
            lines);
    }

    private sealed class OverlayTextSource : TextSource
    {
        private static readonly char[] NewLineChars = { '\r', '\n' };
        private readonly string text;
        private readonly TextRunProperties runProperties;

        public OverlayTextSource(
            string text,
            TextRunProperties runProperties)
        {
            this.text = string.IsNullOrWhiteSpace(text) ? " " : text;
            this.runProperties = runProperties;
        }

        public int TextLength => text.Length;

        public override TextRun GetTextRun(int textSourceCharacterIndex)
        {
            if (textSourceCharacterIndex >= text.Length)
            {
                return new TextEndOfParagraph(1, runProperties);
            }

            if (text[textSourceCharacterIndex] == '\r')
            {
                var newlineLength = textSourceCharacterIndex + 1 < text.Length
                    && text[textSourceCharacterIndex + 1] == '\n'
                        ? 2
                        : 1;

                return new TextEndOfLine(newlineLength, runProperties);
            }

            if (text[textSourceCharacterIndex] == '\n')
            {
                return new TextEndOfLine(1, runProperties);
            }

            var nextNewLineIndex = text.IndexOfAny(NewLineChars, textSourceCharacterIndex);
            var textRunLength = (nextNewLineIndex < 0 ? text.Length : nextNewLineIndex) - textSourceCharacterIndex;

            return new TextCharacters(text, textSourceCharacterIndex, textRunLength, runProperties);
        }

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(
            int textSourceCharacterIndexLimit)
        {
            var length = Math.Clamp(textSourceCharacterIndexLimit, 0, text.Length);

            return new TextSpan<CultureSpecificCharacterBufferRange>(
                length,
                new CultureSpecificCharacterBufferRange(
                    CultureInfo.CurrentUICulture,
                    new CharacterBufferRange(text, 0, length)));
        }

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(
            int textSourceCharacterIndex)
        {
            return textSourceCharacterIndex;
        }
    }

    private sealed class OverlayTextParagraphProperties : TextParagraphProperties
    {
        public OverlayTextParagraphProperties(TextRunProperties defaultTextRunProperties)
        {
            DefaultTextRunProperties = defaultTextRunProperties;
        }

        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;

        public override TextAlignment TextAlignment => TextAlignment.Center;

        public override bool FirstLineInParagraph => true;

        public override bool AlwaysCollapsible => false;

        public override TextRunProperties DefaultTextRunProperties { get; }

        public override TextWrapping TextWrapping => TextWrapping.Wrap;

        public override double LineHeight => double.NaN;

        public override double Indent => 0;

        public override double ParagraphIndent => 0;

        public override IList<TextTabProperties>? Tabs => null;

        public override TextMarkerProperties? TextMarkerProperties => null;

        public override TextDecorationCollection? TextDecorations => null;

        public override double DefaultIncrementalTab => 4 * DefaultTextRunProperties.FontRenderingEmSize;
    }

    private sealed class OverlayTextRunProperties : TextRunProperties
    {
        private readonly Typeface typeface;
        private readonly double fontSize;

        public OverlayTextRunProperties(OcrZoneTextStyle textStyle)
        {
            var fontFamily = string.IsNullOrWhiteSpace(textStyle.FontFamily)
                ? OcrZoneTextStyle.DefaultFontFamily
                : textStyle.FontFamily;
            var fontWeight = textStyle.IsBold ? FontWeights.Bold : FontWeights.Normal;
            var fontStyle = textStyle.IsItalic ? FontStyles.Italic : FontStyles.Normal;

            typeface = new Typeface(new FontFamily(fontFamily), fontStyle, fontWeight, FontStretches.Normal);
            fontSize = Math.Clamp(
                textStyle.FontSize,
                OcrZoneTextStyle.MinimumFontSize,
                OcrZoneTextStyle.MaximumFontSize);
        }

        public override Typeface Typeface => typeface;

        public override double FontRenderingEmSize => fontSize;

        public override double FontHintingEmSize => fontSize;

        public override TextDecorationCollection? TextDecorations => null;

        public override Brush ForegroundBrush => Brushes.White;

        public override Brush? BackgroundBrush => null;

        public override CultureInfo CultureInfo => CultureInfo.CurrentUICulture;

        public override TextEffectCollection? TextEffects => null;

        public override BaselineAlignment BaselineAlignment => BaselineAlignment.Baseline;

        public override NumberSubstitution? NumberSubstitution => null;

        public override TextRunTypographyProperties? TypographyProperties => null;
    }
}
