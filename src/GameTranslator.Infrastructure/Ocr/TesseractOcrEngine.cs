using System.Globalization;
using GameTranslator.Application.Ocr;
using GameTranslator.Domain.Profiles;
using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Exceptions;
using ApplicationOcrResult = GameTranslator.Application.Ocr.OcrResult;
using PixImage = TesseractOCR.Pix.Image;

namespace GameTranslator.Infrastructure.Ocr;

public sealed class TesseractOcrEngine : IOcrEngine
{
    private const int BytesPerPixel = 4;
    private const float OrientationConfidenceThreshold = 15f;
    private const string SupportedPixelFormat = "Bgra32";
    private readonly string tessdataPath;

    public TesseractOcrEngine()
        : this(Path.Combine(AppContext.BaseDirectory, "tessdata"))
    {
    }

    public TesseractOcrEngine(string tessdataPath)
    {
        this.tessdataPath = string.IsNullOrWhiteSpace(tessdataPath)
            ? Path.Combine(AppContext.BaseDirectory, "tessdata")
            : tessdataPath.Trim();
    }

    public string EngineId => OcrSettings.TesseractEngineId;

    public Task<ApplicationOcrResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var bitmapBytes = CreateBitmapBytes(request.Frame);

            using var image = PixImage.LoadFromMemory(bitmapBytes);
            var pageSegMode = SelectPageSegMode(tessdataPath, image, request.Language, request.OrientationMode);
            var language = MapLanguage(request.Language, GetRecognitionOrientationMode(pageSegMode));
            using var engine = new Engine(tessdataPath, language, EngineMode.Default);
            engine.DefaultPageSegMode = pageSegMode;
            using var page = engine.Process(image, pageSegMode);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                new ApplicationOcrResult(
                    request,
                    CreateTextBlocks(page, request.Frame.Width, request.Frame.Height),
                    DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OcrEngineException)
        {
            throw;
        }
        catch (TesseractException exception)
        {
            throw new OcrEngineException(
                "Tesseract OCR failed to recognize text from the captured frame.",
                exception);
        }
        catch (IOException exception)
        {
            throw new OcrEngineException(
                "Tesseract OCR failed to load required data or image bytes.",
                exception);
        }
        catch (Exception exception)
        {
            throw new OcrEngineException(
                "Tesseract OCR failed to recognize text from the captured frame.",
                exception);
        }
    }

    internal static string MapLanguage(string languageTag)
    {
        return MapLanguage(languageTag, OcrOrientationMode.Horizontal);
    }

    internal static string MapLanguage(string languageTag, OcrOrientationMode orientationMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);

        var parts = languageTag
            .Split(new[] { '+', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => MapSingleLanguage(part, orientationMode))
            .ToArray();

        if (parts.Length == 0)
        {
            throw new OcrEngineException("Tesseract OCR language is required.");
        }

        return string.Join('+', parts);
    }

    internal static PageSegMode MapOrientationMode(OcrOrientationMode orientationMode)
    {
        return orientationMode switch
        {
            OcrOrientationMode.Vertical => PageSegMode.SingleBlockVertText,
            OcrOrientationMode.Auto or OcrOrientationMode.Horizontal => PageSegMode.SingleBlock,
            _ => PageSegMode.SingleBlock,
        };
    }

    internal static byte[] CreateBitmapBytes(GameTranslator.Application.Capture.CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!string.Equals(frame.PixelFormat, SupportedPixelFormat, StringComparison.OrdinalIgnoreCase))
        {
            throw new OcrEngineException(
                $"Tesseract OCR requires {SupportedPixelFormat} captured frames, but received '{frame.PixelFormat}'.");
        }

        var targetStride = checked(frame.Width * BytesPerPixel);
        if (frame.Stride < targetStride)
        {
            throw new OcrEngineException("Captured frame stride is too small for BGRA pixel data.");
        }

        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        var pixelBytesLength = checked(targetStride * frame.Height);
        var fileSize = checked(fileHeaderSize + infoHeaderSize + pixelBytesLength);
        var bitmapBytes = new byte[fileSize];

        using var stream = new MemoryStream(bitmapBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(fileHeaderSize + infoHeaderSize);

        writer.Write(infoHeaderSize);
        writer.Write(frame.Width);
        writer.Write(frame.Height);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(pixelBytesLength);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        var sourceBytes = frame.PixelData.ToArray();
        for (var row = frame.Height - 1; row >= 0; row--)
        {
            var sourceOffset = checked(row * frame.Stride);
            writer.Write(sourceBytes, sourceOffset, targetStride);
        }

        return bitmapBytes;
    }

    private static PageSegMode SelectPageSegMode(
        string tessdataPath,
        PixImage image,
        string languageTag,
        OcrOrientationMode orientationMode)
    {
        if (orientationMode is not OcrOrientationMode.Auto)
        {
            return MapOrientationMode(orientationMode);
        }

        try
        {
            var horizontalLanguage = MapLanguage(languageTag, OcrOrientationMode.Horizontal);
            using var engine = new Engine(tessdataPath, horizontalLanguage, EngineMode.Default);
            using var orientationPage = engine.Process(image, PageSegMode.OsdOnly);
            orientationPage.DetectOrientation(out var orientationDegrees, out var confidence);
            if (confidence < OrientationConfidenceThreshold)
            {
                return PageSegMode.SingleBlock;
            }

            return orientationDegrees is 90 or 270
                ? PageSegMode.SingleBlockVertText
                : PageSegMode.SingleBlock;
        }
        catch (TesseractException)
        {
            return PageSegMode.SingleBlock;
        }
        catch (InvalidOperationException)
        {
            return PageSegMode.SingleBlock;
        }
    }

    private static OcrOrientationMode GetRecognitionOrientationMode(PageSegMode pageSegMode)
    {
        return pageSegMode is PageSegMode.SingleBlockVertText
            ? OcrOrientationMode.Vertical
            : OcrOrientationMode.Horizontal;
    }

    private static IReadOnlyList<OcrTextBlock> CreateTextBlocks(
        Page page,
        int frameWidth,
        int frameHeight)
    {
        var blocks = new List<OcrTextBlock>();

        foreach (var layoutBlock in page.Layout)
        {
            foreach (var paragraph in layoutBlock.Paragraphs)
            {
                foreach (var textLine in paragraph.TextLines)
                {
                    if (string.IsNullOrWhiteSpace(textLine.Text) || !textLine.BoundingBox.HasValue)
                    {
                        continue;
                    }

                    var bounds = CreateBoundingBox(textLine.BoundingBox.Value, frameWidth, frameHeight);
                    blocks.Add(new OcrTextBlock(textLine.Text, bounds));
                }
            }
        }

        return blocks;
    }

    private static BoundingBox CreateBoundingBox(
        Rect rect,
        int frameWidth,
        int frameHeight)
    {
        var x = Math.Clamp(rect.X1, 0, frameWidth - 1);
        var y = Math.Clamp(rect.Y1, 0, frameHeight - 1);
        var right = Math.Clamp(rect.X2, x + 1, frameWidth);
        var bottom = Math.Clamp(rect.Y2, y + 1, frameHeight);

        return new BoundingBox(
            x,
            y,
            checked(right - x),
            checked(bottom - y));
    }

    private static string MapSingleLanguage(string languageTag, OcrOrientationMode orientationMode)
    {
        var directCode = languageTag.Trim().Replace('-', '_').ToLower(CultureInfo.InvariantCulture);
        var normalized = directCode.Replace('_', '-');
        var useVerticalModel = orientationMode is OcrOrientationMode.Vertical;

        return normalized switch
        {
            "en" or "en-us" or "en-gb" or "eng" => "eng",
            "ru" or "ru-ru" or "rus" => "rus",
            "ja" or "ja-jp" or "jpn" => useVerticalModel ? "jpn_vert" : "jpn",
            "ja-vert" or "ja-jp-vert" or "jpn-vert" => "jpn_vert",
            "zh" or "zh-cn" or "zh-hans" or "chi-sim" => useVerticalModel ? "chi_sim_vert" : "chi_sim",
            "zh-vert" or "zh-cn-vert" or "zh-hans-vert" or "chi-sim-vert" => "chi_sim_vert",
            "zh-tw" or "zh-hk" or "zh-mo" or "zh-hant" or "chi-tra" => useVerticalModel ? "chi_tra_vert" : "chi_tra",
            "zh-tw-vert" or "zh-hk-vert" or "zh-mo-vert" or "zh-hant-vert" or "chi-tra-vert" => "chi_tra_vert",
            "ko" or "ko-kr" or "kor" => "kor",
            "fr" or "fr-fr" or "fra" => "fra",
            "de" or "de-de" or "deu" => "deu",
            "es" or "es-es" or "spa" => "spa",
            "it" or "it-it" or "ita" => "ita",
            "pt" or "pt-br" or "pt-pt" or "por" => "por",
            "auto" => throw new OcrEngineException("Tesseract OCR does not support automatic language detection."),
            _ when TesseractLanguageCatalog.TryGetTrainedDataCode(directCode, out var trainedDataCode) => trainedDataCode,
            _ when normalized.Length == 3 => normalized,
            _ => throw new OcrEngineException($"Tesseract OCR language '{languageTag}' is not mapped to a traineddata code."),
        };
    }
}
