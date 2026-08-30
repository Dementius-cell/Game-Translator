using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;

namespace GameTranslator.UI.Diagnostics;

internal static class PortableOcrSmokeRunner
{
    public const string CommandName = "--portable-ocr-smoke";

    private const int FrameWidth = 420;
    private const int FrameHeight = 120;
    private const string SyntheticText = "PORTABLE OCR";

    public static bool TryGetReportPath(IReadOnlyList<string> arguments, out string reportPath)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 2
            && string.Equals(arguments[0], CommandName, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(arguments[1]))
        {
            reportPath = Path.GetFullPath(arguments[1]);
            return true;
        }

        reportPath = string.Empty;
        return false;
    }

    public static int Run(OcrService ocrService, string reportPath)
    {
        ArgumentNullException.ThrowIfNull(ocrService);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            var tessdataPath = Path.Combine(baseDirectory, "tessdata");
            var languagePackPath = Path.Combine(tessdataPath, "eng.traineddata");
            var nativeLibraryPaths = new[]
            {
                Path.Combine(baseDirectory, "x64", "leptonica-1.85.0.dll"),
                Path.Combine(baseDirectory, "x64", "tesseract55.dll"),
            };

            AssertFileExists(languagePackPath, "English Tesseract language pack");
            foreach (var nativeLibraryPath in nativeLibraryPaths)
            {
                AssertFileExists(nativeLibraryPath, "x64 Tesseract native library");
            }

            var frame = CreateSyntheticFrame();
            var request = new OcrRequest(
                frame,
                language: "eng",
                zoneId: "portable-ocr-smoke",
                engineId: OcrSettings.TesseractEngineId,
                orientationMode: OcrOrientationMode.Horizontal,
                layoutMode: OcrLayoutMode.Dialog,
                contentLayoutMode: ContentLayoutMode.DialogComic);
            var result = ocrService.RecognizeAsync(request).GetAwaiter().GetResult();
            var recognizedCharacterCount = result.TextBlocks.Sum(
                block => block.Text.Count(character => !char.IsWhiteSpace(character)));
            if (result.TextBlocks.Count == 0 || recognizedCharacterCount == 0)
            {
                throw new InvalidOperationException(
                    "Packaged Tesseract completed without recognizing the synthetic smoke text.");
            }

            stopwatch.Stop();
            WriteReport(
                reportPath,
                new
                {
                    status = "passed",
                    generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    startedAtUtc = startedAt.ToString("O"),
                    elapsedMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
                    processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    baseDirectory,
                    applicationExecutable = CreateFileRecord(
                        Path.Combine(baseDirectory, "GameTranslator.UI.exe"),
                        baseDirectory),
                    languagePack = CreateFileRecord(languagePackPath, baseDirectory),
                    nativeLibraries = nativeLibraryPaths.Select(path => CreateFileRecord(path, baseDirectory)).ToArray(),
                    syntheticFrame = new
                    {
                        width = frame.Width,
                        height = frame.Height,
                        pixelFormat = frame.PixelFormat,
                        sourceTextRecorded = false,
                    },
                    recognizedBlockCount = result.TextBlocks.Count,
                    recognizedCharacterCount,
                    recognizedTextRecorded = false,
                    exceptionChain = Array.Empty<object>(),
                });
            return 0;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            WriteFailureReport(reportPath, startedAt, stopwatch.Elapsed, exception);
            return 1;
        }
    }

    public static int WriteStartupFailure(string reportPath, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        ArgumentNullException.ThrowIfNull(exception);

        WriteFailureReport(reportPath, DateTimeOffset.UtcNow, TimeSpan.Zero, exception);
        return 1;
    }

    private static CapturedFrame CreateSyntheticFrame()
    {
        var visual = new DrawingVisual();
        using (var drawingContext = visual.RenderOpen())
        {
            drawingContext.DrawRectangle(Brushes.White, null, new Rect(0, 0, FrameWidth, FrameHeight));
            var formattedText = new FormattedText(
                SyntheticText,
                CultureInfo.GetCultureInfo("en-US"),
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                emSize: 64,
                Brushes.Black,
                pixelsPerDip: 1d);
            var origin = new Point(
                Math.Max(0, (FrameWidth - formattedText.WidthIncludingTrailingWhitespace) / 2d),
                Math.Max(0, (FrameHeight - formattedText.Height) / 2d));
            drawingContext.DrawText(formattedText, origin);
        }

        var rendered = new RenderTargetBitmap(
            FrameWidth,
            FrameHeight,
            dpiX: 96,
            dpiY: 96,
            PixelFormats.Pbgra32);
        rendered.Render(visual);
        var converted = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, destinationPalette: null, alphaThreshold: 0d);
        var stride = checked(FrameWidth * 4);
        var pixels = new byte[checked(stride * FrameHeight)];
        converted.CopyPixels(pixels, stride, offset: 0);

        return new CapturedFrame(
            new CaptureRegion(0, 0, FrameWidth, FrameHeight),
            FrameWidth,
            FrameHeight,
            stride,
            "Bgra32",
            pixels,
            DateTimeOffset.UtcNow);
    }

    private static object CreateFileRecord(string path, string baseDirectory)
    {
        AssertFileExists(path, "packaged smoke dependency");
        var file = new FileInfo(path);
        return new
        {
            relativePath = Path.GetRelativePath(baseDirectory, file.FullName).Replace('\\', '/'),
            sizeBytes = file.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.FullName))).ToLowerInvariant(),
        };
    }

    private static void AssertFileExists(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Packaged {description} is missing.", path);
        }
    }

    private static void WriteFailureReport(
        string reportPath,
        DateTimeOffset startedAt,
        TimeSpan elapsed,
        Exception exception)
    {
        WriteReport(
            reportPath,
            new
            {
                status = "failed",
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                startedAtUtc = startedAt.ToString("O"),
                elapsedMs = Math.Round(elapsed.TotalMilliseconds, 1),
                processArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                baseDirectory = Path.GetFullPath(AppContext.BaseDirectory),
                recognizedTextRecorded = false,
                exceptionChain = CreateExceptionChain(exception),
            });
    }

    private static object[] CreateExceptionChain(Exception exception)
    {
        var chain = new List<object>();
        for (Exception? current = exception; current is not null && chain.Count < 8; current = current.InnerException)
        {
            chain.Add(new
            {
                type = current.GetType().FullName ?? current.GetType().Name,
                message = NormalizeMessage(current.Message),
            });
        }

        return chain.ToArray();
    }

    private static string NormalizeMessage(string message)
    {
        var normalized = message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();
        return normalized.Length <= 1_024
            ? normalized
            : normalized[..1_024];
    }

    private static void WriteReport(string reportPath, object report)
    {
        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Portable OCR smoke report directory could not be resolved."));
        var json = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            });
        File.WriteAllText(fullPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
