namespace GameTranslator.Infrastructure.Ocr;

internal sealed class BoundedNativeOcrExecutor
{
    private readonly SemaphoreSlim concurrencyLimiter;

    public BoundedNativeOcrExecutor(int maximumConcurrency)
    {
        if (maximumConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        }

        MaximumConcurrency = maximumConcurrency;
        concurrencyLimiter = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    public int MaximumConcurrency { get; }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, TResult> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        await concurrencyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return operation(cancellationToken);
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            concurrencyLimiter.Release();
        }
    }
}
