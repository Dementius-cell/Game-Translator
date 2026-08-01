using System.Globalization;
using GameTranslator.Application.Capture;
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
    private const int ComicRefinementPaddingPixels = 4;
    private const double MinimumComicSourceWordConfidence = 50d;
    private const double QualityUpscaleFallbackScale = 2d;
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
            var recognitionOrientationMode = SelectRecognitionOrientationMode(
                tessdataPath,
                image,
                request.Language,
                request.OrientationMode);
            var language = MapLanguage(request.Language, recognitionOrientationMode);
            using var engine = new Engine(tessdataPath, language, EngineMode.Default);
            cancellationToken.ThrowIfCancellationRequested();

            if (request.LayoutMode is OcrLayoutMode.Comic)
            {
                return Task.FromResult(
                    CreateComicResult(
                        request,
                        engine,
                        image,
                        recognitionOrientationMode,
                        language,
                        cancellationToken));
            }

            var pageSegMode = MapLayoutMode(request.LayoutMode, recognitionOrientationMode);
            engine.DefaultPageSegMode = pageSegMode;
            var recognitionPassId = CreateRecognitionPassId(pageSegMode);
            IReadOnlyList<OcrTextBlock> textBlocks;
            IReadOnlyList<OcrWord> words;
            using (var page = engine.Process(image, pageSegMode))
            {
                cancellationToken.ThrowIfCancellationRequested();
                textBlocks = CreateTextBlocks(page, request.Frame.Width, request.Frame.Height);
                words = CreateWords(page, request.Frame.Width, request.Frame.Height, recognitionPassId);
            }

            if (textBlocks.Count == 0
                && TryCreateQualityUpscaleFallback(
                    request,
                    engine,
                    pageSegMode,
                    language,
                    "quality-upscale-fallback",
                    cancellationToken,
                    out var qualityTextBlocks,
                    out var qualityWords)
                && qualityTextBlocks.Count > 0)
            {
                textBlocks = qualityTextBlocks;
                words = qualityWords;
            }

            return Task.FromResult(
                new ApplicationOcrResult(
                    request,
                    textBlocks,
                    DateTimeOffset.UtcNow,
                    CreateTextBlockSources(textBlocks, recognitionOrientationMode),
                    words: words));
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

    internal static PageSegMode MapLayoutMode(
        OcrLayoutMode layoutMode,
        OcrOrientationMode orientationMode)
    {
        return layoutMode switch
        {
            OcrLayoutMode.Menu or OcrLayoutMode.Comic => PageSegMode.SparseText,
            OcrLayoutMode.Auto or OcrLayoutMode.Dialog => MapOrientationMode(orientationMode),
            _ => MapOrientationMode(orientationMode),
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

    private static OcrOrientationMode SelectRecognitionOrientationMode(
        string tessdataPath,
        PixImage image,
        string languageTag,
        OcrOrientationMode orientationMode)
    {
        if (orientationMode is not OcrOrientationMode.Auto)
        {
            return orientationMode;
        }

        try
        {
            var horizontalLanguage = MapLanguage(languageTag, OcrOrientationMode.Horizontal);
            using var engine = new Engine(tessdataPath, horizontalLanguage, EngineMode.Default);
            using var orientationPage = engine.Process(image, PageSegMode.OsdOnly);
            orientationPage.DetectOrientation(out var orientationDegrees, out var confidence);
            if (confidence < OrientationConfidenceThreshold)
            {
                return OcrOrientationMode.Horizontal;
            }

            return orientationDegrees is 90 or 270
                ? OcrOrientationMode.Vertical
                : OcrOrientationMode.Horizontal;
        }
        catch (TesseractException)
        {
            return OcrOrientationMode.Horizontal;
        }
        catch (InvalidOperationException)
        {
            return OcrOrientationMode.Horizontal;
        }
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

    private static IReadOnlyList<OcrTextBlockSource> CreateTextBlockSources(
        IReadOnlyList<OcrTextBlock> textBlocks,
        OcrOrientationMode orientationMode)
    {
        return textBlocks
            .Select(block => new OcrTextBlockSource(
                block.Bounds,
                new[] { block.Bounds },
                orientationMode))
            .ToArray();
    }

    private static ApplicationOcrResult CreateComicResult(
        OcrRequest request,
        Engine engine,
        PixImage image,
        OcrOrientationMode recognitionOrientationMode,
        string tesseractLanguage,
        CancellationToken cancellationToken)
    {
        const PageSegMode detectionPageSegMode = PageSegMode.SparseText;
        var detectionPassId = CreateRecognitionPassId(detectionPageSegMode, "detection");
        IReadOnlyList<OcrTextBlock> detectedBlocks;
        List<OcrWord> words;
        using (var detectionPage = engine.Process(image, detectionPageSegMode))
        {
            cancellationToken.ThrowIfCancellationRequested();
            detectedBlocks = CreateTextBlocks(detectionPage, request.Frame.Width, request.Frame.Height);
            var detectionWords = CreateWords(
                detectionPage,
                request.Frame.Width,
                request.Frame.Height,
                detectionPassId);
            words = new List<OcrWord>(detectionWords);
        }

        var refinedBlocks = new List<OcrTextBlock>(detectedBlocks.Count);
        var refinementPageSegMode = recognitionOrientationMode is OcrOrientationMode.Vertical
            ? PageSegMode.SingleBlockVertText
            : PageSegMode.SingleLine;
        var refinementPassId = CreateRecognitionPassId(refinementPageSegMode, "line-refinement");

        foreach (var detectedBlock in detectedBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cropBounds = CreatePaddedBounds(
                detectedBlock.Bounds,
                request.Frame.Width,
                request.Frame.Height,
                ComicRefinementPaddingPixels);
            var croppedFrame = CreateCroppedFrame(request.Frame, cropBounds);
            using var croppedImage = PixImage.LoadFromMemory(CreateBitmapBytes(croppedFrame));
            using var refinedPage = engine.Process(croppedImage, refinementPageSegMode);
            var cropWords = TranslateWords(
                CreateWords(
                refinedPage,
                croppedFrame.Width,
                croppedFrame.Height,
                refinementPassId),
                cropBounds.X,
                cropBounds.Y);
            words.AddRange(cropWords);

            var sourceBlock = CreateComicSourceBlock(cropWords);
            if (sourceBlock is null)
            {
                continue;
            }

            refinedBlocks.Add(sourceBlock);
        }

        if (refinedBlocks.Count == 0)
        {
            AddEmptyComicFallback(
                request,
                engine,
                image,
                recognitionOrientationMode,
                refinedBlocks,
                words,
                cancellationToken);
        }

        if (refinedBlocks.Count == 0)
        {
            AddEmptyComicQualityUpscaleFallback(
                request,
                engine,
                recognitionOrientationMode,
                tesseractLanguage,
                refinedBlocks,
                words,
                cancellationToken);
        }

        return new ApplicationOcrResult(
            request,
            refinedBlocks,
            DateTimeOffset.UtcNow,
            CreateTextBlockSources(refinedBlocks, recognitionOrientationMode),
            words: words);
    }

    private static void AddEmptyComicFallback(
        OcrRequest request,
        Engine engine,
        PixImage image,
        OcrOrientationMode recognitionOrientationMode,
        ICollection<OcrTextBlock> refinedBlocks,
        ICollection<OcrWord> words,
        CancellationToken cancellationToken)
    {
        var fallbackPageSegMode = MapOrientationMode(recognitionOrientationMode);
        var fallbackPassId = CreateRecognitionPassId(fallbackPageSegMode, "empty-comic-fallback");
        using var fallbackPage = engine.Process(image, fallbackPageSegMode);
        cancellationToken.ThrowIfCancellationRequested();

        var fallbackWords = CreateWords(
            fallbackPage,
            request.Frame.Width,
            request.Frame.Height,
            fallbackPassId);
        foreach (var word in fallbackWords)
        {
            words.Add(word);
        }

        var fallbackBlock = CreateComicSourceBlock(fallbackWords);
        if (fallbackBlock is not null)
        {
            refinedBlocks.Add(fallbackBlock);
        }
    }

    private static void AddEmptyComicQualityUpscaleFallback(
        OcrRequest request,
        Engine engine,
        OcrOrientationMode recognitionOrientationMode,
        string tesseractLanguage,
        ICollection<OcrTextBlock> refinedBlocks,
        ICollection<OcrWord> words,
        CancellationToken cancellationToken)
    {
        if (!IsCjkOrThaiLanguage(tesseractLanguage)
            || request.Frame.Width < 2
            || request.Frame.Height < 2)
        {
            return;
        }

        try
        {
            var scaledFrame = ScaleFrameBilinear(request.Frame, QualityUpscaleFallbackScale);
            using var scaledImage = PixImage.LoadFromMemory(CreateBitmapBytes(scaledFrame));
            const PageSegMode detectionPageSegMode = PageSegMode.SparseText;
            var detectionPassId = CreateRecognitionPassId(
                detectionPageSegMode,
                "quality-upscale-detection");
            IReadOnlyList<OcrTextBlock> detectedBlocks;
            using (var detectionPage = engine.Process(scaledImage, detectionPageSegMode))
            {
                cancellationToken.ThrowIfCancellationRequested();
                detectedBlocks = CreateTextBlocks(detectionPage, scaledFrame.Width, scaledFrame.Height);
                var detectionWords = MapWordsFromPreprocessedFrame(
                    CreateWords(detectionPage, scaledFrame.Width, scaledFrame.Height, detectionPassId),
                    request.Frame.Width,
                    request.Frame.Height,
                    QualityUpscaleFallbackScale);
                foreach (var word in detectionWords)
                {
                    words.Add(word);
                }
            }

            var refinementPageSegMode = recognitionOrientationMode is OcrOrientationMode.Vertical
                ? PageSegMode.SingleBlockVertText
                : PageSegMode.SingleLine;
            var refinementPassId = CreateRecognitionPassId(
                refinementPageSegMode,
                "quality-upscale-line-refinement");
            var scaledPadding = Math.Max(
                1,
                (int)Math.Ceiling(ComicRefinementPaddingPixels * QualityUpscaleFallbackScale));

            foreach (var detectedBlock in detectedBlocks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cropBounds = CreatePaddedBounds(
                    detectedBlock.Bounds,
                    scaledFrame.Width,
                    scaledFrame.Height,
                    scaledPadding);
                var croppedFrame = CreateCroppedFrame(scaledFrame, cropBounds);
                using var croppedImage = PixImage.LoadFromMemory(CreateBitmapBytes(croppedFrame));
                using var refinedPage = engine.Process(croppedImage, refinementPageSegMode);
                var cropWords = TranslateWords(
                    CreateWords(
                        refinedPage,
                        croppedFrame.Width,
                        croppedFrame.Height,
                        refinementPassId),
                    cropBounds.X,
                    cropBounds.Y);
                var mappedCropWords = MapWordsFromPreprocessedFrame(
                    cropWords,
                    request.Frame.Width,
                    request.Frame.Height,
                    QualityUpscaleFallbackScale);
                foreach (var word in mappedCropWords)
                {
                    words.Add(word);
                }

                var sourceBlock = CreateComicSourceBlock(mappedCropWords);
                if (sourceBlock is not null)
                {
                    refinedBlocks.Add(sourceBlock);
                }
            }
        }
        catch (TesseractException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }
    }

    private static bool TryCreateQualityUpscaleFallback(
        OcrRequest request,
        Engine engine,
        PageSegMode pageSegMode,
        string tesseractLanguage,
        string stage,
        CancellationToken cancellationToken,
        out IReadOnlyList<OcrTextBlock> textBlocks,
        out IReadOnlyList<OcrWord> words)
    {
        textBlocks = Array.Empty<OcrTextBlock>();
        words = Array.Empty<OcrWord>();

        if (!IsCjkOrThaiLanguage(tesseractLanguage)
            || request.Frame.Width < 2
            || request.Frame.Height < 2)
        {
            return false;
        }

        try
        {
            var scaledFrame = ScaleFrameBilinear(request.Frame, QualityUpscaleFallbackScale);
            using var scaledImage = PixImage.LoadFromMemory(CreateBitmapBytes(scaledFrame));
            using var page = engine.Process(scaledImage, pageSegMode);
            cancellationToken.ThrowIfCancellationRequested();

            var recognitionPassId = CreateRecognitionPassId(pageSegMode, stage);
            textBlocks = MapTextBlocksFromPreprocessedFrame(
                CreateTextBlocks(page, scaledFrame.Width, scaledFrame.Height),
                request.Frame.Width,
                request.Frame.Height,
                QualityUpscaleFallbackScale);
            words = MapWordsFromPreprocessedFrame(
                CreateWords(page, scaledFrame.Width, scaledFrame.Height, recognitionPassId),
                request.Frame.Width,
                request.Frame.Height,
                QualityUpscaleFallbackScale);

            return textBlocks.Count > 0 || words.Count > 0;
        }
        catch (TesseractException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsCjkOrThaiLanguage(string tesseractLanguage)
    {
        return tesseractLanguage
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(code => code is "jpn"
                or "jpn_vert"
                or "chi_sim"
                or "chi_sim_vert"
                or "chi_tra"
                or "chi_tra_vert"
                or "kor"
                or "tha");
    }

    private static IReadOnlyList<OcrTextBlock> MapTextBlocksFromPreprocessedFrame(
        IReadOnlyList<OcrTextBlock> textBlocks,
        int originalWidth,
        int originalHeight,
        double scale)
    {
        return textBlocks
            .Select(block => new OcrTextBlock(
                block.Text,
                MapBoundsFromPreprocessedFrame(block.Bounds, originalWidth, originalHeight, scale)))
            .ToArray();
    }

    private static IReadOnlyList<OcrWord> MapWordsFromPreprocessedFrame(
        IReadOnlyList<OcrWord> words,
        int originalWidth,
        int originalHeight,
        double scale)
    {
        return words
            .Select(word => new OcrWord(
                word.Text,
                MapBoundsFromPreprocessedFrame(word.Bounds, originalWidth, originalHeight, scale),
                word.Confidence,
                word.RecognitionPassId))
            .ToArray();
    }

    private static BoundingBox MapBoundsFromPreprocessedFrame(
        BoundingBox bounds,
        int originalWidth,
        int originalHeight,
        double scale)
    {
        var x = Math.Clamp((int)Math.Floor(bounds.X / scale), 0, originalWidth - 1);
        var y = Math.Clamp((int)Math.Floor(bounds.Y / scale), 0, originalHeight - 1);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right / scale), x + 1, originalWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom / scale), y + 1, originalHeight);

        return new BoundingBox(x, y, right - x, bottom - y);
    }

    private static CapturedFrame ScaleFrameBilinear(CapturedFrame frame, double scale)
    {
        var scaledWidth = Math.Max(1, (int)Math.Round(frame.Width * scale, MidpointRounding.AwayFromZero));
        var scaledHeight = Math.Max(1, (int)Math.Round(frame.Height * scale, MidpointRounding.AwayFromZero));
        var scaledStride = checked(scaledWidth * BytesPerPixel);
        var scaledPixels = new byte[checked(scaledStride * scaledHeight)];
        var sourcePixels = frame.PixelData.Span;

        for (var y = 0; y < scaledHeight; y++)
        {
            var sourceY = Math.Clamp(((y + 0.5) / scale) - 0.5, 0, frame.Height - 1);
            var y0 = (int)Math.Floor(sourceY);
            var y1 = Math.Min(frame.Height - 1, y0 + 1);
            var yWeight = sourceY - y0;

            for (var x = 0; x < scaledWidth; x++)
            {
                var sourceX = Math.Clamp(((x + 0.5) / scale) - 0.5, 0, frame.Width - 1);
                var x0 = (int)Math.Floor(sourceX);
                var x1 = Math.Min(frame.Width - 1, x0 + 1);
                var xWeight = sourceX - x0;
                var targetOffset = y * scaledStride + x * BytesPerPixel;

                for (var channel = 0; channel < BytesPerPixel; channel++)
                {
                    var topLeft = sourcePixels[y0 * frame.Stride + x0 * BytesPerPixel + channel];
                    var topRight = sourcePixels[y0 * frame.Stride + x1 * BytesPerPixel + channel];
                    var bottomLeft = sourcePixels[y1 * frame.Stride + x0 * BytesPerPixel + channel];
                    var bottomRight = sourcePixels[y1 * frame.Stride + x1 * BytesPerPixel + channel];
                    var top = topLeft + (topRight - topLeft) * xWeight;
                    var bottom = bottomLeft + (bottomRight - bottomLeft) * xWeight;
                    scaledPixels[targetOffset + channel] = ClampToByte(top + (bottom - top) * yWeight);
                }
            }
        }

        return new CapturedFrame(
            frame.Region,
            scaledWidth,
            scaledHeight,
            scaledStride,
            frame.PixelFormat,
            scaledPixels,
            frame.CapturedAt);
    }

    private static byte ClampToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), byte.MinValue, byte.MaxValue);
    }

    private static CapturedFrame CreateCroppedFrame(CapturedFrame frame, BoundingBox cropBounds)
    {
        var targetStride = checked(cropBounds.Width * BytesPerPixel);
        var pixelData = new byte[checked(targetStride * cropBounds.Height)];
        var sourcePixels = frame.PixelData.Span;

        for (var row = 0; row < cropBounds.Height; row++)
        {
            var sourceOffset = checked((cropBounds.Y + row) * frame.Stride + cropBounds.X * BytesPerPixel);
            var targetOffset = checked(row * targetStride);
            sourcePixels.Slice(sourceOffset, targetStride).CopyTo(pixelData.AsSpan(targetOffset, targetStride));
        }

        return new CapturedFrame(
            new CaptureRegion(
                checked(frame.Region.X + cropBounds.X),
                checked(frame.Region.Y + cropBounds.Y),
                cropBounds.Width,
                cropBounds.Height),
            cropBounds.Width,
            cropBounds.Height,
            targetStride,
            frame.PixelFormat,
            pixelData,
            frame.CapturedAt);
    }

    private static BoundingBox CreatePaddedBounds(
        BoundingBox bounds,
        int frameWidth,
        int frameHeight,
        int padding)
    {
        var left = Math.Max(0, bounds.X - padding);
        var top = Math.Max(0, bounds.Y - padding);
        var right = Math.Min(frameWidth, bounds.Right + padding);
        var bottom = Math.Min(frameHeight, bounds.Bottom + padding);

        return new BoundingBox(left, top, right - left, bottom - top);
    }

    private static OcrTextBlock? CreateComicSourceBlock(IReadOnlyList<OcrWord> words)
    {
        var reliableWords = words
            .Where(word => word.Confidence is >= MinimumComicSourceWordConfidence)
            .ToArray();
        if (reliableWords.Length == 0)
        {
            return null;
        }

        var text = string.Join(
            ' ',
            reliableWords
                .Select(word => word.Text.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new OcrTextBlock(text, CreateCombinedBounds(reliableWords.Select(word => word.Bounds).ToArray()));
    }

    private static IReadOnlyList<OcrWord> TranslateWords(
        IReadOnlyList<OcrWord> words,
        int offsetX,
        int offsetY)
    {
        return words
            .Select(word => new OcrWord(
                word.Text,
                TranslateBounds(word.Bounds, offsetX, offsetY),
                word.Confidence,
                word.RecognitionPassId))
            .ToArray();
    }

    private static BoundingBox TranslateBounds(BoundingBox bounds, int offsetX, int offsetY)
    {
        return new BoundingBox(
            checked(bounds.X + offsetX),
            checked(bounds.Y + offsetY),
            bounds.Width,
            bounds.Height);
    }

    private static BoundingBox CreateCombinedBounds(IReadOnlyList<BoundingBox> bounds)
    {
        var left = bounds.Min(bound => bound.X);
        var top = bounds.Min(bound => bound.Y);
        var right = bounds.Max(bound => bound.Right);
        var bottom = bounds.Max(bound => bound.Bottom);

        return new BoundingBox(left, top, right - left, bottom - top);
    }

    private static IReadOnlyList<OcrWord> CreateWords(
        Page page,
        int frameWidth,
        int frameHeight,
        string recognitionPassId)
    {
        var words = new List<OcrWord>();

        foreach (var layoutBlock in page.Layout)
        {
            foreach (var paragraph in layoutBlock.Paragraphs)
            {
                foreach (var textLine in paragraph.TextLines)
                {
                    foreach (var word in textLine.Words)
                    {
                        if (string.IsNullOrWhiteSpace(word.Text) || !word.BoundingBox.HasValue)
                        {
                            continue;
                        }

                        words.Add(
                            new OcrWord(
                                word.Text,
                                CreateBoundingBox(word.BoundingBox.Value, frameWidth, frameHeight),
                                word.Confidence,
                                recognitionPassId));
                    }
                }
            }
        }

        return words;
    }

    private static string CreateRecognitionPassId(PageSegMode pageSegMode, string? stage = null)
    {
        return string.IsNullOrWhiteSpace(stage)
            ? $"tesseract:{pageSegMode}"
            : $"tesseract:{pageSegMode}:{stage}";
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
