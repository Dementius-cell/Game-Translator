namespace GameTranslator.Application.Ocr;

public sealed class OcrService
{
    private readonly IOcrEngine engine;
    private readonly OcrPreprocessor preprocessor;

    public OcrService(IOcrEngine engine)
        : this(engine, new OcrPreprocessor())
    {
    }

    public OcrService(IOcrEngine engine, OcrPreprocessor preprocessor)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        this.preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
    }

    public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var preprocessedFrame = preprocessor.Apply(request.Frame, request.PreprocessingSettings);
        var preprocessedRequest = ReferenceEquals(preprocessedFrame, request.Frame)
            ? request
            : new OcrRequest(preprocessedFrame, request.Language, request.ZoneId, request.PreprocessingSettings);

        return engine.RecognizeAsync(preprocessedRequest, cancellationToken);
    }

    public async Task<IReadOnlyList<OcrResult>> RecognizeAsync(
        IEnumerable<OcrRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        cancellationToken.ThrowIfCancellationRequested();

        var requestList = requests.ToArray();
        var results = new List<OcrResult>(requestList.Length);

        foreach (var request in requestList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RecognizeAsync(request, cancellationToken));
        }

        return results;
    }
}