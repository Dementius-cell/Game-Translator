using GameTranslator.Application.Capture;
using GameTranslator.Application.Ocr;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using ApplicationOcrResult = GameTranslator.Application.Ocr.OcrResult;

namespace GameTranslator.Infrastructure.Ocr;

public sealed class WindowsOcrEngine : IOcrEngine
{
    private const int BytesPerPixel = 4;
    private const string SupportedPixelFormat = "Bgra32";

    public async Task<ApplicationOcrResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var language = CreateLanguage(request.Language);
            var engine = OcrEngine.TryCreateFromLanguage(language)
                ?? throw new OcrEngineException($"Windows OCR language '{request.Language}' is not available on this device.");

            using var bitmap = CreateSoftwareBitmap(request.Frame);
            var nativeResult = await engine.RecognizeAsync(bitmap);
            cancellationToken.ThrowIfCancellationRequested();

            return new ApplicationOcrResult(
                request,
                CreateTextBlocks(nativeResult, request.Frame.Width, request.Frame.Height),
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OcrEngineException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OcrEngineException(
                "Windows OCR failed to recognize text from the captured frame.",
                exception);
        }
    }

    private static Language CreateLanguage(string languageTag)
    {
        try
        {
            var language = new Language(languageTag);

            if (!OcrEngine.IsLanguageSupported(language))
            {
                throw new OcrEngineException($"Windows OCR language '{languageTag}' is not supported.");
            }

            return language;
        }
        catch (OcrEngineException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OcrEngineException($"Windows OCR language '{languageTag}' is invalid.", exception);
        }
    }

    private static SoftwareBitmap CreateSoftwareBitmap(CapturedFrame frame)
    {
        if (!string.Equals(frame.PixelFormat, SupportedPixelFormat, StringComparison.OrdinalIgnoreCase))
        {
            throw new OcrEngineException(
                $"Windows OCR requires {SupportedPixelFormat} captured frames, but received '{frame.PixelFormat}'.");
        }

        var bitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            frame.Width,
            frame.Height,
            BitmapAlphaMode.Premultiplied);

        try
        {
            using var writer = new DataWriter();
            writer.WriteBytes(CreateContiguousBgraPixels(frame));
            bitmap.CopyFromBuffer(writer.DetachBuffer());

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static byte[] CreateContiguousBgraPixels(CapturedFrame frame)
    {
        var targetStride = checked(frame.Width * BytesPerPixel);
        if (frame.Stride < targetStride)
        {
            throw new OcrEngineException(
                "Captured frame stride is too small for BGRA pixel data.");
        }

        var sourceBytes = frame.PixelData.ToArray();
        var targetBytes = new byte[checked(targetStride * frame.Height)];

        if (frame.Stride == targetStride)
        {
            Array.Copy(sourceBytes, targetBytes, targetBytes.Length);
            return targetBytes;
        }

        for (var row = 0; row < frame.Height; row++)
        {
            var sourceOffset = checked(row * frame.Stride);
            var targetOffset = checked(row * targetStride);

            Array.Copy(sourceBytes, sourceOffset, targetBytes, targetOffset, targetStride);
        }

        return targetBytes;
    }

    private static IReadOnlyList<OcrTextBlock> CreateTextBlocks(
        Windows.Media.Ocr.OcrResult nativeResult,
        int frameWidth,
        int frameHeight)
    {
        var blocks = new List<OcrTextBlock>();

        foreach (var line in nativeResult.Lines)
        {
            var bounds = CreateLineBoundingBox(line, frameWidth, frameHeight);
            if (bounds is null || string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            blocks.Add(new OcrTextBlock(line.Text, bounds.Value));
        }

        return blocks;
    }

    private static BoundingBox? CreateLineBoundingBox(OcrLine line, int frameWidth, int frameHeight)
    {
        var words = line.Words.ToArray();
        if (words.Length == 0)
        {
            return null;
        }

        var left = words.Min(word => word.BoundingRect.X);
        var top = words.Min(word => word.BoundingRect.Y);
        var right = words.Max(word => word.BoundingRect.X + word.BoundingRect.Width);
        var bottom = words.Max(word => word.BoundingRect.Y + word.BoundingRect.Height);

        var x = Math.Clamp(checked((int)Math.Floor(left)), 0, frameWidth - 1);
        var y = Math.Clamp(checked((int)Math.Floor(top)), 0, frameHeight - 1);
        var clampedRight = Math.Clamp(checked((int)Math.Ceiling(right)), x + 1, frameWidth);
        var clampedBottom = Math.Clamp(checked((int)Math.Ceiling(bottom)), y + 1, frameHeight);

        return new BoundingBox(
            x,
            y,
            checked(clampedRight - x),
            checked(clampedBottom - y));
    }
}
