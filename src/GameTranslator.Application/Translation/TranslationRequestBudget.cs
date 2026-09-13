namespace GameTranslator.Application.Translation;

/// <summary>Bounds one uncached Bing invocation, including admission to the translation queue.</summary>
internal static class TranslationRequestBudget
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    internal static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> action,
        SemaphoreSlim? limiter,
        TimeProvider clock,
        CancellationToken cancellationToken,
        TranslationProviderRequestDiagnostics? diagnostics)
    {
        using var deadline = new CancellationTokenSource(Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var acquired = false;
        Task<T>? invocation = null;
        try
        {
            if (limiter is not null)
            {
                await limiter.WaitAsync(linked.Token);
                acquired = true;
            }

            invocation = action(linked.Token);
            var result = await invocation.WaitAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            diagnostics?.MarkBudgetExceeded(clock.GetUtcNow());
            throw new TranslatorProviderException("BingWeb", TranslatorProviderFailureKind.Timeout,
                "BingWeb translation exceeded the 30-second limit, including queue and service initialization.", exception);
        }
        finally
        {
            if (invocation is { IsCompleted: false })
            {
                // A misbehaving adapter must not keep the caller waiting or exceed admission
                // concurrency. Its result is discarded; its slot is held until it really exits.
                _ = invocation.ContinueWith(task =>
                {
                    _ = task.Exception;
                    if (acquired)
                    {
                        // A completed one-shot pipeline can already have disposed its limiter.
                        try { limiter!.Release(); }
                        catch (ObjectDisposedException) { }
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else if (acquired) limiter!.Release();
        }
    }
}
