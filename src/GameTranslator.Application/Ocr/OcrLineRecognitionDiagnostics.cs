namespace GameTranslator.Application.Ocr;

/// <summary>Bounded coverage evidence for detector-guided line recognition, without text or pixels.</summary>
public sealed record OcrLineRecognitionDiagnostics
{
    public OcrLineRecognitionDiagnostics(int expectedLines, int recognizedLines)
    {
        if (expectedLines is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(expectedLines));
        if (recognizedLines < 0 || recognizedLines > expectedLines) throw new ArgumentOutOfRangeException(nameof(recognizedLines));
        ExpectedLines = expectedLines;
        RecognizedLines = recognizedLines;
    }
    public int ExpectedLines { get; }
    public int RecognizedLines { get; }
    public int MissingLines => ExpectedLines - RecognizedLines;
}
