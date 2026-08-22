namespace GameTranslator.Application.Ocr;

public sealed class OcrService
{
    private readonly IReadOnlyDictionary<string, IOcrEngine> engines;
    private readonly OcrPreprocessor preprocessor;

    public OcrService(IOcrEngine engine)
        : this(new[] { engine }, new OcrPreprocessor())
    {
    }

    public OcrService(IOcrEngine engine, OcrPreprocessor preprocessor)
        : this(new[] { engine }, preprocessor)
    {
    }

    public OcrService(IEnumerable<IOcrEngine> engines)
        : this(engines, new OcrPreprocessor())
    {
    }

    public OcrService(IEnumerable<IOcrEngine> engines, OcrPreprocessor preprocessor)
    {
        ArgumentNullException.ThrowIfNull(engines);

        this.engines = engines
            .Select(engine => engine ?? throw new ArgumentException("OCR engine collection must not contain null items.", nameof(engines)))
            .GroupBy(engine => engine.EngineId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (this.engines.Count == 0)
        {
            throw new ArgumentException("At least one OCR engine must be registered.", nameof(engines));
        }

        this.preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
    }

    public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var engine = SelectEngine(request.EngineId);
        var preprocessedFrame = preprocessor.Apply(request.Frame, request.PreprocessingSettings);
        var preprocessedRequest = ReferenceEquals(preprocessedFrame, request.Frame)
            ? request
            : new OcrRequest(
                preprocessedFrame,
                request.Language,
                request.ZoneId,
                request.PreprocessingSettings,
                request.EngineId,
                request.OrientationMode,
                request.LayoutMode,
                request.ContentLayoutMode);

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

    private IOcrEngine SelectEngine(string engineId)
    {
        if (engines.TryGetValue(engineId, out var engine))
        {
            return engine;
        }

        throw new OcrEngineException($"OCR engine '{engineId}' is not registered.");
    }
}
