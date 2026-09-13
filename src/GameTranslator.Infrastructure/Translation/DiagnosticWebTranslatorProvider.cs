using GameTranslator.Application.Translation;

namespace GameTranslator.Infrastructure.Translation;

/// <summary>Explicit local diagnostic fault window; never manufactures a successful translation.</summary>
public sealed class DiagnosticWebTranslatorProvider : ITranslatorProvider, IDisposable
{
    private readonly ITranslatorProvider inner;
    private readonly string scenario;
    private readonly TimeProvider clock;
    private readonly object sync = new();
    private DateTimeOffset? faultUntil;

    public DiagnosticWebTranslatorProvider(ITranslatorProvider inner, string scenario, TimeProvider? clock = null)
    {
        this.inner = inner;
        this.scenario = scenario is "timeout" or "429" or "captcha" ? scenario
            : throw new ArgumentException("Unknown diagnostic scenario.", nameof(scenario));
        this.clock = clock ?? TimeProvider.System;
    }

    public string ProviderId => inner.ProviderId;
    public void Dispose() => (inner as IDisposable)?.Dispose();

    public static ITranslatorProvider Wrap(ITranslatorProvider provider)
    {
        var scenario = Environment.GetEnvironmentVariable("GAME_TRANSLATOR_TEST_FAULT");
        return scenario is "timeout" or "429" or "captcha"
            ? new DiagnosticWebTranslatorProvider(provider, scenario) : provider;
    }

    public Task<TranslateResponse> TranslateAsync(TranslateRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        DateTimeOffset until;
        lock (sync)
        {
            faultUntil ??= now.AddSeconds(45);
            until = faultUntil.Value;
        }
        if (now >= until)
        {
            return inner.TranslateAsync(request, cancellationToken);
        }

        var kind = scenario switch
        {
            "timeout" => TranslatorProviderFailureKind.Timeout,
            "429" => TranslatorProviderFailureKind.Throttled,
            _ => TranslatorProviderFailureKind.UserVerificationRequired,
        };
        request.Diagnostics?.MarkProviderInvocationStarted(ProviderId, now);
        request.Diagnostics?.MarkProviderInvocationCompleted(TranslationProviderInvocationOutcome.Failed, now, kind);
        var message = $"[SIMULATED {scenario}] Local diagnostic fault; no HTTP request sent. Window ends after 45 seconds from first uncached request. CAPTCHA requires Stop/Start after the window.";
        return Task.FromException<TranslateResponse>(scenario == "429"
            ? new TranslatorProviderException(ProviderId, System.Net.HttpStatusCode.TooManyRequests, kind, message,
                retryAfter: until - now, nextRetryAt: until)
            : new TranslatorProviderException(ProviderId, kind, message));
    }
}
