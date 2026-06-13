namespace GameTranslator.Application.Ocr;

public sealed class OcrService
{
    private readonly IOcrEngine engine;

    public OcrService(IOcrEngine engine)
    {
        this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return engine.RecognizeAsync(request, cancellationToken);
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
