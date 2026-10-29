using System.Text.RegularExpressions;
using GameTranslator.Application.Translation;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.Application.Content;

/// <summary>
/// Keeps recognized values and explicit key hints authoritative for menu labels.
/// This checks translation output, not OCR correctness.
/// </summary>
public static partial class MenuTranslationIntegrity
{
    public const string Warning = "Menu translation retained the source because a number or key hint changed.";

    public static TranslateResponse Validate(IReadOnlyList<string> sources, TranslateResponse response)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(response);
        if (sources.Count != response.TranslatedTexts.Count)
            throw new ArgumentException("Menu translation count must match source count.", nameof(response));

        var retained = false;
        var texts = response.TranslatedTexts.Select((translated, index) =>
        {
            if (IsStandaloneControlHint(sources[index])
                ? string.Equals(sources[index].Trim(), translated.Trim(), StringComparison.Ordinal)
                : Tokens(sources[index]).SequenceEqual(Tokens(translated), StringComparer.Ordinal))
                return translated;
            retained = true;
            return sources[index];
        }).ToArray();
        return retained
            ? new TranslateResponse(texts, response.TranslatedAt, response.ProviderId,
                string.Join(" ", new[] { response.DiagnosticMessage, Warning }.Where(s => s.Length > 0)))
            : response;
    }

    private static IEnumerable<string> Tokens(string text) =>
        ProtectedTokens().Matches(text).Select(match => match.Value);

    public static bool IsNumericDisplay(string text) => NumericDisplay().IsMatch(text.Trim());

    // Match the entire element only: these letters inside prose are ordinary text.
    public static bool IsStandaloneControlHint(string text) => StandaloneControlHint().IsMatch(text.Trim());

    [GeneratedRegex(@"^(?:[ABXYEQRFWSD]|LB|RB|LT|RT|L1|L2|L3|R1|R2|R3|Esc|ESC|Enter|Shift|Ctrl|Alt|Tab|Space|SPACE|Backspace|[Ff](?:[1-9]|1[0-2]))$", RegexOptions.CultureInvariant)]
    private static partial Regex StandaloneControlHint();

    // OCR can split one key glyph into several letters. Only a detector-isolated
    // light keycap with one enclosed dark glyph supports excluding that raw result.
    // Keep the original OCR untouched; this evidence belongs to translation projection.
    internal static bool HasStandaloneKeycapEvidence(OcrResult result)
    {
        var request = result.Request;
        var frame = request.Frame;
        if (request.ContentLayoutMode != ContentLayoutMode.StaticMenu
            || request.OrientationMode != OcrOrientationMode.Horizontal
            || request.DetectorLineBounds.Count != 1 || result.TextBlocks.Count != 1
            || frame.PixelFormat != "Bgra32" || frame.Width is < 12 or > 256 || frame.Height is < 12 or > 256
            || frame.Width < frame.Height * .8 || frame.Width > frame.Height * 1.3
            || request.DetectorLineBounds[0].Width * request.DetectorLineBounds[0].Height < frame.Width * frame.Height * .8) return false;
        var text = result.TextBlocks[0].Text.Trim();
        if (text.Length is < 1 or > 3 || text.Any(c => !char.IsAsciiLetter(c))) return false;
        var area = frame.Width * frame.Height;
        var dark = new bool[area];
        var pixels = frame.PixelData.Span;
        var bright = 0;
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
        {
            var offset = y * frame.Stride + x * 4;
            var luminance = pixels[offset] * .0722 + pixels[offset + 1] * .7152 + pixels[offset + 2] * .2126;
            dark[y * frame.Width + x] = luminance < 70;
            if (luminance > 180) bright++;
        }
        if (bright < area * .6 || bright > area * .9) return false;
        var queue = new Queue<int>();
        var glyphs = 0;
        for (var seed = 0; seed < area; seed++)
        {
            if (!dark[seed]) continue;
            dark[seed] = false;
            queue.Enqueue(seed);
            int count = 0, left = frame.Width, top = frame.Height, right = 0, bottom = 0;
            while (queue.TryDequeue(out var index))
            {
                var x = index % frame.Width;
                var y = index / frame.Width;
                count++;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                for (var ny = Math.Max(0, y - 1); ny <= Math.Min(frame.Height - 1, y + 1); ny++)
                for (var nx = Math.Max(0, x - 1); nx <= Math.Min(frame.Width - 1, x + 1); nx++)
                {
                    var next = ny * frame.Width + nx;
                    if (!dark[next]) continue;
                    dark[next] = false;
                    queue.Enqueue(next);
                }
            }
            // Outer background is allowed, but disconnected letters/dots are not.
            if (left == 0 || top == 0 || right == frame.Width - 1 || bottom == frame.Height - 1) continue;
            if (count < area * .005) continue;
            if (++glyphs > 1 || count < area * .04 || count > area * .35
                || bottom - top + 1 < frame.Height * .4 || bottom - top + 1 > frame.Height * .8
                || right - left + 1 < frame.Width * .1 || right - left + 1 > frame.Width * .65
                || left < frame.Width * .12 || frame.Width - 1 - right < frame.Width * .12
                || top < frame.Height * .12 || frame.Height - 1 - bottom < frame.Height * .08) return false;
        }
        return glyphs == 1;
    }

    [GeneratedRegex(@"\[[A-Za-z0-9+ ]{1,24}\]|(?<!\w)[xX×]\s?\d+|[+\-−]?\d+(?:[.,:/]\d+)*(?:\s?(?:[%％]|(?:[KMGT]i?[Bb]|Hz|MHz|GHz|FPS|ms)\b))?", RegexOptions.CultureInvariant)]
    private static partial Regex ProtectedTokens();

    [GeneratedRegex(@"^[+\-−]?\d+(?:\s*[.,:/*×+\-]\s*\d+)*(?:\s?(?:%|％|[KMGT]i?[Bb]|Hz|MHz|GHz|FPS|ms))?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDisplay();
}
